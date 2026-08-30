using System.Text;
using D2BotNG.Core.Protos.Captures;
using TooltipEngine = D2ItemToolkit.TooltipEngine;

namespace D2BotNG.Capture;

/// <summary>
/// Turns a <see cref="SearchItemsRequest" /> into SQL for <see cref="CaptureStore.SearchItems" />.
///
/// The shape is STAT-FIRST. Every condition becomes a CTE producing the set of top-level item ids
/// it matches, built by scanning the stat table — where the selectivity lives, and what
/// `stat_by_stat` indexes — and mapping back to the owning item. The item query then semi-joins
/// against those sets. Driving the other way, a correlated EXISTS per candidate item, can never
/// use a stat index and cannot count matches at all.
///
/// Counting is why a group aggregates rather than simply AND-ing: each condition's branch is
/// DISTINCT per item, so UNION ALL across the branches then GROUP BY item id makes COUNT(*)
/// exactly "how many conditions this item satisfied", and min_matches falls out as a HAVING.
///
/// A malformed request is REJECTED, never repaired — see <see cref="Validate" />. Quietly
/// dropping a bound or clamping a count turns "this filter is wrong" into "here are results",
/// and the silent repair usually errs towards matching MORE than was asked for, which is the
/// direction a caller cannot detect.
/// </summary>
internal sealed class SearchQueryBuilder
{
    /// <summary>
    /// Ceiling on bound parameters in one search. SQLite has no array binding, so every element
    /// of every repeated field expands to a parameter of its own, and past 32766 the query fails
    /// at execution with an error no caller can act on. This bounds the IN lists, which are the
    /// only things that grow parameters without also growing the expression tree.
    /// </summary>
    private const int MaxParameters = 10_000;

    /// <summary>
    /// Ceiling on any list that becomes a CHAIN of SQL terms rather than an IN list. SQLite caps
    /// the depth of an expression tree at 1000 and the arms of a compound SELECT at 500, and both
    /// are reached one to two orders of magnitude below <see cref="MaxParameters" /> — a group of
    /// 501 conditions or 700-odd specific_items fails at execution while the parameter count is
    /// still trivial. The chains do not share a budget with each other, so this applies to each
    /// separately, and it sits far below every engine limit rather than near any of them.
    /// </summary>
    private const int MaxChainTerms = 128;

    /// <summary>dwFlags ITEMFLAG_RUNEWORD, the bit that says magic_prefix_0 is a runes.txt id.</summary>
    private const long RunewordFlag = 0x4000000;

    /// <summary>The container.name values a request may name; anything else is a caller error.</summary>
    private static readonly string[] ContainerNames =
    [
        CaptureSchema.ContainerEquipped, CaptureSchema.ContainerInventory, CaptureSchema.ContainerCube,
        CaptureSchema.ContainerBelt, CaptureSchema.ContainerStash,
    ];

    private readonly List<(string Name, object? Value)> _parameters = [];
    private readonly StringBuilder _ctes = new();
    private readonly StringBuilder _where = new("i.parent_id IS NULL");
    private int _cteCount;

    public SearchQueryBuilder(SearchItemsRequest request, TooltipEngine tooltip)
    {
        Validate(request, tooltip);

        AppendIn("i.profile", request.Profiles);

        AppendIdentity(request);

        AppendIn("i.quality", request.Qualities.Select(q => (object)q));
        AppendIn("i.gfx_index", request.GfxIndexes.Select(g => (object)g));
        // Columns resolved from the game's tables at ingest. NULL for a unit the tables do not
        // describe, and NULL satisfies neither IN nor a comparison, so such an item simply never
        // matches these — which is the honest answer for an item nothing is known about.
        AppendIn("i.tier", request.Tiers.Select(t => (object)(int)t));

        if (request.ItemTypes.Count > 0)
        {
            // An item stores the LEAF category, so a request naming a broad one is expanded
            // DOWNWARDS through the Equiv1/Equiv2 closure — once per query, rather than writing a
            // row per ancestor per item. Comparing the stored value directly instead is the bug
            // ResurrectedTrade ships: it makes every interior row (Weapon, Any Armor) match
            // nothing, because no item stores anything but its leaf.
            var descendants = request.ItemTypes.SelectMany(tooltip.Types.Descendants).Distinct();
            var list = string.Join(", ", descendants.Select((id, i) => Param($"ty{i}", id)));
            _where.Append($"\n   AND (i.type_0 IN ({list}) OR i.type_1 IN ({list}))");
        }

        AppendRange("i.item_level", request.ItemLevel, "il");
        AppendRange("i.req_level", request.RequiredLevel, "rl");
        AppendRange("i.req_str", request.RequiredStrength, "rs");
        AppendRange("i.req_dex", request.RequiredDexterity, "rd");

        AppendIn("c.name", request.Containers);

        if (request.HasItemFlagsAll)
        {
            var p = Param("fa", (long)request.ItemFlagsAll);
            _where.Append($"\n   AND (i.item_flags & {p}) = {p}");
        }

        if (request.HasItemFlagsNone)
            _where.Append($"\n   AND (i.item_flags & {Param("fn", (long)request.ItemFlagsNone)}) = 0");

        // A bare condition is its own single-member AND — no counting machinery needed.
        for (var c = 0; c < request.Conditions.Count; c++)
        {
            var branch = AppendCondition(request.Conditions[c], $"c{c}");
            _where.Append($"\n   AND i.id IN (SELECT iid FROM {branch})");
        }

        for (var g = 0; g < request.Groups.Count; g++)
            AppendGroup(request.Groups[g], g);

        // Ordering last, so its CTE name cannot collide with a condition's and so the parameter
        // budget below counts it too.
        AppendOrdering(request.Ordering);

        // The CTE block is only legal in front of a SELECT, so it stays empty when unused.
        if (_ctes.Length > 0)
        {
            _ctes.Insert(0, "WITH ");
            _ctes.Append('\n');
        }

        // Both are finished here and each is read twice — the count query and the page query — so
        // they are rendered once rather than on every access.
        Ctes = _ctes.ToString();
        Where = _where.ToString();

        // Counted after the fact rather than guessed per field: the total is what SQLite limits,
        // and it is the product of several independent lists.
        if (_parameters.Count > MaxParameters)
        {
            throw new InvalidSearchRequestException(
                $"the request expands to {_parameters.Count} bound values, over the limit of "
                + $"{MaxParameters}; narrow the filter lists");
        }
    }

    /// <summary>The <c>WITH …</c> prefix, or empty when nothing filters on stats.</summary>
    public string Ctes { get; }

    /// <summary>The item-side predicate, without the <c>WHERE</c> keyword.</summary>
    public string Where { get; }

    /// <summary>
    /// The join the ORDER BY needs, or empty. Only the page query takes it: the count is the same
    /// either way, and a LEFT JOIN it does not need is work for nothing.
    /// </summary>
    public string OrderJoin { get; private set; } = "";

    /// <summary>
    /// The full ordering, without the <c>ORDER BY</c> keyword — the requested key if there is one,
    /// then the tiebreakers that make an OFFSET page stable.
    /// </summary>
    public string OrderBy { get; private set; } = DefaultOrder;

    public (string Name, object? Value)[] Parameters => _parameters.ToArray();

    /// <summary>
    /// Where a row sits, which is also the natural reading order: by character, then container,
    /// then position on the grid.
    ///
    /// This is the tail of EVERY ordering, not just the default. owner/page/id are not decoration:
    /// container names repeat across the player and the merc and across stash pages, so without a
    /// unique final key OFFSET paging can repeat a row on one page and skip it on the next.
    /// </summary>
    private const string DefaultOrder = "i.profile, c.name, c.owner, c.page, i.y, i.x, i.id";

    /// <summary>
    /// Builds the ORDER BY, and for a stat key the CTE and LEFT JOIN that feed it.
    ///
    /// An item the key is absent from — no such stat, or a column the game tables could not fill —
    /// sorts LAST in BOTH directions. SQLite would otherwise put those NULLs first on an ascending
    /// sort, which buries every real answer under the items that do not have the thing at all.
    /// </summary>
    private void AppendOrdering(Ordering? ordering)
    {
        if (ordering == null) return;

        string key;
        switch (ordering.ByCase)
        {
            case Ordering.ByOneofCase.Column:
                key = ordering.Column switch
                {
                    ItemColumn.RequiredLevel => "i.req_level",
                    ItemColumn.RequiredStrength => "i.req_str",
                    ItemColumn.RequiredDexterity => "i.req_dex",
                    ItemColumn.Quality => "i.quality",
                    ItemColumn.Tier => "i.tier",
                    ItemColumn.ItemLevel => "i.item_level",
                    ItemColumn.DamageOneHandMax => "i.damage_1h_max",
                    ItemColumn.DamageTwoHandMax => "i.damage_2h_max",
                    ItemColumn.DamageThrowMax => "i.damage_throw_max",
                    // Unreachable: validation rejects an undefined column. Present so a value added
                    // to the enum without a column here fails loudly instead of ordering by nothing.
                    _ => throw new InvalidSearchRequestException(
                        $"ordering.column {(int)ordering.Column} has no column mapping"),
                };
                break;

            case Ordering.ByOneofCase.Stat:
                key = "o.v";
                AppendStatOrderCte(ordering.Stat);
                OrderJoin = "\n  LEFT JOIN ord o ON o.iid = i.id";
                break;

            // No key chosen. The message exists — a caller may have set only `descending` — so the
            // direction still applies, to the default order.
            default:
                OrderBy = Reverse(DefaultOrder, ordering.Descending);
                return;
        }

        var direction = ordering.Descending ? " DESC" : "";
        OrderBy = $"({key} IS NULL), {key}{direction}, {DefaultOrder}";
    }

    /// <summary>
    /// Flips the default order, so "descending" means something even with no key chosen. Applied
    /// per column because SQLite's DESC binds to one term, not to the list.
    /// </summary>
    private static string Reverse(string order, bool descending) =>
        descending
            ? string.Join(", ", order.Split(", ").Select(term => $"{term} DESC"))
            : order;

    /// <summary>
    /// One row per item: the best single source of the ordering stat.
    ///
    /// MAX, never SUM, and the same in both directions — this has to agree with what a condition
    /// matches on, which is "has a source of at least N". Summing would rank an item above the one
    /// the filter actually selected, and taking a MIN when sorting ascending would rank items by
    /// their WORST source, which nobody means by "sort by enhanced damage".
    /// </summary>
    private void AppendStatOrderCte(StatOrder order)
    {
        if (order.Surface == StatSurface.Merged)
        {
            // No MAX and no GROUP BY: the merged surface is already one row per (item, stat, layer).
            // A MAX survives only because `stat_ids` may name several stats that share a scale, and
            // for a layer RANGE, where several layers of one stat can match.
            var mergedWhere = new StringBuilder(
                In("ms.stat_id", order.StatIds.Select(id => (object)id), "o"));
            mergedWhere.Append(LayerClause(StatSelector.Of(order), "ms", "o"));

            // Same column choice as a condition's, so a page is ranked by the number it was
            // filtered by rather than by a total the filter never looked at.
            var orderColumn = order.HasSockets && order.Sockets == SocketScope.HostOnly
                ? "ms.value_host"
                : "ms.value";

            AppendCte("ord",
                $"""
                 SELECT ms.item_id AS iid, MAX({orderColumn}) AS v
                       FROM merged_stat ms
                      WHERE {mergedWhere}
                      GROUP BY ms.item_id
                 """);
            return;
        }

        var (selected, join, scopeFilter) = ScopeSql(order.Sockets);

        var where = new StringBuilder(In("st.stat_id", order.StatIds.Select(id => (object)id), "o"));
        where.Append(LayerClause(StatSelector.Of(order), "st", "o"));
        where.Append(scopeFilter);
        where.Append(ListScope(order.Lists, "o"));

        AppendCte("ord",
            $"""
             SELECT {selected} AS iid, MAX(st.value) AS v
                   FROM stat st
                   JOIN statlist sl ON sl.id = st.statlist_id{join}
                  WHERE {where}
                  GROUP BY {selected}
             """);
    }

    private void AppendGroup(StatConditionGroup group, int groupIndex)
    {
        // Validation guarantees at least one condition and an in-range min_matches, so every
        // condition contributes a branch and the count below means what the caller wrote.
        var branches = new List<string>();
        for (var c = 0; c < group.Conditions.Count; c++)
            branches.Add(AppendCondition(group.Conditions[c], $"g{groupIndex}c{c}"));

        // A group of one IS its branch. The branch already yields one row per item, and validation
        // pins min_matches to 1..1 here, so the wrapper could only ever ask "at least one" of a set
        // with nothing to count. Negation reads the same set either way, so it collapses too.
        string name;
        if (branches.Count == 1)
        {
            name = branches[0];
        }
        else
        {
            var min = group.HasMinMatches ? group.MinMatches : branches.Count;

            name = $"g{groupIndex}";
            var union = string.Join("\n    UNION ALL\n    ", branches.Select(b => $"SELECT iid FROM {b}"));
            AppendCte(name,
                $"""
                 SELECT iid FROM (
                     {union}
                 ) GROUP BY iid HAVING COUNT(*) >= {min}
                 """);
        }

        // NOT IN is safe because iid comes from NOT NULL columns; were that to change this would
        // need NOT EXISTS, since NOT IN over a NULL yields no rows at all.
        _where.Append($"\n   AND i.id {(group.Negate ? "NOT IN" : "IN")} (SELECT iid FROM {name})");
    }

    /// <summary>Emits a condition's CTE and returns its name.</summary>
    private string AppendCondition(StatCondition condition, string name)
    {
        if (condition.Surface == StatSurface.Merged) return AppendMergedCondition(condition, name);

        var (selected, join, scopeFilter) = ScopeSql(condition.Sockets);

        var where = new StringBuilder(In("st.stat_id", condition.StatIds.Select(id => (object)id), name));
        where.Append(LayerClause(StatSelector.Of(condition), "st", name));

        if (condition.HasMinValue) where.Append($" AND st.value >= {Param($"{name}min", condition.MinValue)}");
        if (condition.HasMaxValue) where.Append($" AND st.value <= {Param($"{name}max", condition.MaxValue)}");

        where.Append(scopeFilter);
        where.Append(ListScope(condition.Lists, name));

        AppendCte(name,
            $"""
             SELECT DISTINCT {selected} AS iid
                   FROM stat st
                   JOIN statlist sl ON sl.id = st.statlist_id{join}
                  WHERE {where}
             """);

        return name;
    }

    /// <summary>
    /// The same condition against the MERGED surface — one row per (item, stat, layer), holding
    /// what the item's sources add up to.
    ///
    /// <para>
    /// Simpler than the raw form because there is nothing to map back: `merged_stat.item_id` is
    /// already the top-level item, so there is no statlist join and no socket-scope join through
    /// `root_id`. DISTINCT is needed all the same, for the reason the raw branch needs it: the
    /// primary key is (item_id, stat_id, layer), so one row per KEY and not one per item — a
    /// condition naming several stat_ids, or spanning a layer range, matches an item several
    /// times over. Inside a group those rows are counted, and that single condition would then
    /// satisfy "at least 2 of these" on its own.
    /// </para>
    /// <para>
    /// Both scope fields are refused here rather than ignored (see <c>Validate</c>): they select
    /// among sources, and this surface has none.
    /// </para>
    /// </summary>
    private string AppendMergedCondition(StatCondition condition, string name)
    {
        // Which TOTAL: the item with its socket fillers folded in, or without them. `value_host` is
        // NULL for a stat only a filler grants, and a NULL satisfies no comparison, so an item whose
        // 30 comes entirely from a rune simply fails a bound asked of its own total.
        var column = condition.HasSockets && condition.Sockets == SocketScope.HostOnly
            ? "ms.value_host"
            : "ms.value";

        var where = new StringBuilder(In("ms.stat_id", condition.StatIds.Select(id => (object)id), name));
        where.Append(LayerClause(StatSelector.Of(condition), "ms", name));

        if (condition.HasMinValue) where.Append($" AND {column} >= {Param($"{name}min", condition.MinValue)}");
        if (condition.HasMaxValue) where.Append($" AND {column} <= {Param($"{name}max", condition.MaxValue)}");

        AppendCte(name,
            $"""
             SELECT DISTINCT ms.item_id AS iid
                   FROM merged_stat ms
                  WHERE {where}
             """);

        return name;
    }

    /// <summary>
    /// How a stat row maps back to the item that counts, per socket scope: what to select as the
    /// item id, the join that needs, and any extra filter.
    ///
    /// HOST_ONLY needs no item join: statlist.item_id is already the owning unit, and the outer
    /// `parent_id IS NULL` keeps fillers from being results. The other scopes map a filler back to
    /// its host through root_id. The default arm is HOST_ONLY and only that — validation has
    /// already rejected any value this enum does not define, so a newer client's scope cannot land
    /// here and be answered as though it were the host's.
    /// </summary>
    private static (string Selected, string Join, string Filter) ScopeSql(SocketScope scope) =>
        scope switch
        {
            SocketScope.WithFillers => ("si.root_id", "\n        JOIN item si ON si.id = sl.item_id", ""),
            SocketScope.FillersOnly => ("si.root_id", "\n        JOIN item si ON si.id = sl.item_id",
                " AND si.parent_id IS NOT NULL"),
            _ => ("sl.item_id", "", ""),
        };

    /// <summary>
    /// Provenance. The stat list is already joined for every condition, so this costs no extra
    /// join — and absent means "any list", which is why the common case never mentions it.
    /// </summary>
    private string ListScope(StatListScope? scope, string prefix)
    {
        if (scope == null) return "";

        var sql = new StringBuilder();
        if (scope.ExcludeStates.Count > 0)
        {
            var names = scope.ExcludeStates.Select((s, i) => Param($"{prefix}xs{i}", s));
            sql.Append($" AND sl.state_no NOT IN ({string.Join(", ", names)})");
        }

        // 0x40 is STATLIST_MAGIC: set on the mods the item GRANTS, clear on its base array, which
        // carries STATLIST_EXTENDED (0x80000000) instead. Both sit at state_no 0, so this bit is
        // the only thing separating them — flags_all = 0x40 asks for the granted mods alone,
        // flags_none = 0x40 for the base alone.
        if (scope.HasFlagsAll)
        {
            var p = Param($"{prefix}fa", (long)scope.FlagsAll);
            sql.Append($" AND (sl.flags & {p}) = {p}");
        }

        if (scope.HasFlagsNone)
            sql.Append($" AND (sl.flags & {Param($"{prefix}fn", (long)scope.FlagsNone)}) = 0");

        return sql.ToString();
    }

    // -----------------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Rejects a request this builder cannot honour literally. Everything here was once "repaired"
    /// in silence instead, and every repair changed the answer — mostly towards matching MORE
    /// than was asked for, which no caller can spot from results.
    /// </summary>
    /// <exception cref="InvalidSearchRequestException">The request is not answerable as written.</exception>
    private static void Validate(SearchItemsRequest request, TooltipEngine tooltip)
    {
        Range(request.ItemLevel, "item_level");
        Range(request.RequiredLevel, "required_level");
        Range(request.RequiredStrength, "required_strength");
        Range(request.RequiredDexterity, "required_dexterity");

        // An out-of-range tier is a caller error, not an empty result: the column stores only the
        // three the game has, so a fourth would filter to nothing and read as "no such items".
        foreach (var tier in request.Tiers)
        {
            if (!Enum.IsDefined(tier)) throw new InvalidSearchRequestException($"unknown tier {(int)tier}");
        }

        // Same argument one axis over. The tree yields NOTHING for a row it does not have, so an
        // ItemTypes.txt index out of range would expand to an empty IN list — which SQLite accepts,
        // and which answers "no such items" to a question that names no category at all.
        foreach (var type in request.ItemTypes)
        {
            if (tooltip.Types.RowAt(type) == null)
                throw new InvalidSearchRequestException($"unknown item_type row {type}");
        }

        // Each of these becomes a chain of SQL terms rather than an IN list.
        Chain("conditions", request.Conditions.Count);
        Chain("groups", request.Groups.Count);
        Chain("specific_items", request.SpecificItems.Count);

        foreach (var container in request.Containers)
        {
            if (!ContainerNames.Contains(container))
            {
                throw new InvalidSearchRequestException(
                    $"unknown container '{container}'; expected one of {string.Join(", ", ContainerNames)}");
            }
        }

        for (var c = 0; c < request.Conditions.Count; c++)
            ValidateCondition(request.Conditions[c], $"conditions[{c}]");

        for (var g = 0; g < request.Groups.Count; g++)
        {
            var group = request.Groups[g];
            if (group.Conditions.Count == 0)
            {
                // Silently meaningless positively, and actively wrong negated: "exclude items
                // matching all of nothing" excludes everything, which is never what was meant.
                throw new InvalidSearchRequestException($"groups[{g}] has no conditions");
            }

            // The group's branches become the arms of one compound SELECT, whose own cap is
            // lower than the expression-depth one above.
            Chain($"groups[{g}].conditions", group.Conditions.Count);

            for (var c = 0; c < group.Conditions.Count; c++)
                ValidateCondition(group.Conditions[c], $"groups[{g}].conditions[{c}]");

            // Only the range is checked; absent still means "all of them".
            if (group.HasMinMatches && (group.MinMatches < 1 || group.MinMatches > group.Conditions.Count))
            {
                throw new InvalidSearchRequestException(
                    $"groups[{g}].min_matches is {group.MinMatches}, outside 1..{group.Conditions.Count}");
            }
        }

        ValidateOrdering(request.Ordering);

        return;

        static void Chain(string field, int count)
        {
            if (count > MaxChainTerms)
            {
                throw new InvalidSearchRequestException(
                    $"{field} has {count} entries, over the limit of {MaxChainTerms}");
            }
        }

        static void Range(Int32Range? range, string field)
        {
            if (range is { HasMin: true, HasMax: true } && range.Max < range.Min)
            {
                throw new InvalidSearchRequestException(
                    $"{field} has max {range.Max} below min {range.Min}");
            }
        }
    }

    /// <summary>
    /// An unanswerable ordering is refused for the same reason an unanswerable filter is: silently
    /// falling back to the default order would return a page that looks sorted and is not, and the
    /// caller reads the first row as the best match.
    /// </summary>
    private static void ValidateOrdering(Ordering? ordering)
    {
        if (ordering == null) return;

        switch (ordering.ByCase)
        {
            case Ordering.ByOneofCase.Column when !Enum.IsDefined(ordering.Column):
                throw new InvalidSearchRequestException(
                    $"ordering names unknown column {(int)ordering.Column}");

            case Ordering.ByOneofCase.Stat:
                ValidateSelector(StatSelector.Of(ordering.Stat), "ordering.stat");
                break;
        }
    }

    /// <summary>
    /// Everything that is true of naming a stat, wherever it is named. A condition and an ordering
    /// describe the stats they read identically, so a rule holding for one and not the other would
    /// be an oversight rather than a distinction — and on the ordering side it is the least visible
    /// kind, since a scope quietly widened there simply puts a different item at the top of a page
    /// that still looks sorted.
    /// </summary>
    /// <param name="selector">The stat, scope and surface the condition or ordering names.</param>
    /// <param name="path">Where in the request this sits, so a message names the field it came from.</param>
    private static void ValidateSelector(in StatSelector selector, string path)
    {
        // Naming no stats selects no rows, so it can only ever mean one of two opposite things
        // depending on where it sits. Neither is worth guessing at.
        if (selector.StatIdCount == 0)
            throw new InvalidSearchRequestException($"{path} names no stat_ids");

        if (selector.HasSockets && !Enum.IsDefined(selector.Sockets))
            throw new InvalidSearchRequestException($"{path} has unknown sockets {(int)selector.Sockets}");

        if (!Enum.IsDefined(selector.Surface))
            throw new InvalidSearchRequestException($"{path} has unknown surface {(int)selector.Surface}");

        // The merged surface stores two totals — with the socket fillers and without — so
        // HOST_ONLY and WITH_FILLERS both mean something here, the same thing they mean on the raw
        // surface one granularity down. FILLERS_ONLY does not: there is no total for "what the
        // gems alone add up to", and answering it from either column would answer a different
        // question. `lists` selects among sources, and a total has none.
        if (selector.Surface == StatSurface.Merged)
        {
            if (selector.HasSockets && selector.Sockets == SocketScope.FillersOnly)
            {
                throw new InvalidSearchRequestException(
                    $"{path} asks for the fillers' own total, which is not stored; "
                    + "use the raw surface to match what a gem or rune grants");
            }

            if (selector.Lists != null)
            {
                throw new InvalidSearchRequestException(
                    $"{path} sets lists on the merged surface, which has no stat lists; "
                    + "use the raw surface to select among sources");
            }
        }

        // layer_max is an upper bound ON layer, not a bound of its own — without a lower bound
        // there is no range at all, and emitting nothing for it matches every layer instead.
        if (selector.HasLayerMax && !selector.HasLayer)
            throw new InvalidSearchRequestException($"{path} sets layer_max without layer");

        if (selector.HasLayerMax && selector.LayerMax < selector.Layer)
        {
            throw new InvalidSearchRequestException(
                $"{path} has layer_max {selector.LayerMax} below layer {selector.Layer}");
        }
    }

    private static void ValidateCondition(StatCondition condition, string path)
    {
        ValidateSelector(StatSelector.Of(condition), path);

        // The value bounds are the condition's alone: an ordering ranks BY the value and so has
        // nothing to bound.
        if (condition.HasMinValue && condition.HasMaxValue && condition.MaxValue < condition.MinValue)
        {
            throw new InvalidSearchRequestException(
                $"{path} has max_value {condition.MaxValue} below min_value {condition.MinValue}");
        }
    }

    // -----------------------------------------------------------------------
    // Plumbing
    // -----------------------------------------------------------------------

    private void AppendCte(string name, string body)
    {
        if (_cteCount++ > 0) _ctes.Append(",\n");
        _ctes.Append($"{name} AS (\n{body}\n)");
    }

    /// <summary>
    /// The layer scope, or empty when there is none. One helper for all four sites — both surfaces
    /// times a condition and an ordering — because the clause is the same question every time and
    /// the alias is the only thing that differs; deriving the parameter names from one prefix is
    /// what keeps two of them from colliding once several CTEs sit in the same statement.
    /// </summary>
    private string LayerClause(in StatSelector selector, string alias, string prefix)
    {
        // Validation has already rejected layer_max without a layer, so an absent layer really is
        // "any layer" and not half a range.
        if (!selector.HasLayer) return "";

        return selector.HasLayerMax
            ? $" AND {alias}.layer BETWEEN {Param($"{prefix}l", selector.Layer)} "
              + $"AND {Param($"{prefix}lx", selector.LayerMax)}"
            : $" AND {alias}.layer = {Param($"{prefix}l", selector.Layer)}";
    }

    /// <summary>An inclusive bound on a nullable column; an unset end is simply not constrained.</summary>
    private void AppendRange(string column, Int32Range? range, string prefix)
    {
        if (range == null) return;
        if (range.HasMin) _where.Append($"\n   AND {column} >= {Param($"{prefix}min", range.Min)}");
        if (range.HasMax) _where.Append($"\n   AND {column} <= {Param($"{prefix}max", range.Max)}");
    }

    /// <summary>
    /// Which item, across the four fields that can name one.
    ///
    /// <para>
    /// Values WITHIN a field are alternatives and OR; the fields themselves AND, like every other
    /// filter on the request. That is what lets "Treachery, on a Wire Fleece" be asked — a runeword
    /// and a base item are different questions about the same item, not two candidates for it.
    /// </para>
    /// <para>
    /// The consequence a caller has to know: naming a base AND a specific unique is a
    /// CONTRADICTION unless the unique happens to sit on that base, and it answers with nothing.
    /// The store cannot tell an intentional pairing from a mistaken one, so it answers literally
    /// and the UI is what warns.
    /// </para>
    /// <para>
    /// A base item is an items.txt row (<c>class_ids</c>, or <c>codes</c> by name); a unique or set
    /// piece is a (quality, file_index) pair, because the game overloads that column per quality;
    /// a runeword is a string id the game parks in the first magic-prefix slot. The runeword's flag
    /// test belongs to that field rather than being a narrowing of its own — the prefix slot is an
    /// affix index on everything else, so without the pairing a magic item whose prefix index
    /// collides with a runeword's string id would match.
    /// </para>
    /// </summary>
    private void AppendIdentity(SearchItemsRequest request)
    {
        AppendIn("i.class_id", request.ClassIds.Select(id => (object)id));

        if (request.Runewords.Count > 0)
        {
            AppendIn("i.magic_prefix_0", request.Runewords.Select(id => (object)id));
            var flag = Param("rw", RunewordFlag);
            _where.Append($"\n   AND (i.item_flags & {flag}) = {flag}");
        }

        if (request.SpecificItems.Count > 0)
        {
            // `file_index >= 0` says nothing the equality beside it does not, and is what makes
            // this filter cheap: `item_by_specific` is PARTIAL on exactly that predicate, and
            // SQLite uses a partial index only where the query's own terms IMPLY the index's —
            // `file_index = ?` does not imply `>= 0`, so without it every specific-item search is
            // a full table scan. It sits inside each OR arm rather than beside them so that every
            // arm implies it on its own, which is what the OR optimization looks at. It also holds
            // the contract literally: an unidentified item stores -1 and must never match.
            var pairs = request.SpecificItems.Select((item, i) =>
                $"(i.quality = {Param($"sq{i}", item.Quality)} "
                + $"AND i.file_index = {Param($"sf{i}", item.FileIndex)} AND i.file_index >= 0)");
            _where.Append($"\n   AND ({string.Join(" OR ", pairs)})");
        }
    }

    private void AppendIn(string column, IEnumerable<object> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return;
        _where.Append($"\n   AND {In(column, list, $"p{_parameters.Count}")}");
    }

    /// <summary>SQLite has no array binding, so an IN list expands to numbered parameters.</summary>
    private string In(string column, IEnumerable<object> values, string prefix)
    {
        var names = values.Select((v, i) => Param($"{prefix}_{i}", v)).ToList();
        return names.Count == 1 ? $"{column} = {names[0]}" : $"{column} IN ({string.Join(", ", names)})";
    }

    private string Param(string name, object value)
    {
        var parameter = $"${name}";
        _parameters.Add((parameter, value));
        return parameter;
    }

    /// <summary>
    /// How a request names a stat: which ids, from which sources, on which surface, within which
    /// layer range. <see cref="StatCondition" /> and <see cref="StatOrder" /> say all of that the
    /// same way but are unrelated generated types with no common base, so each projects into this
    /// and the rules that read only these fields — validation, the layer clause — are written once.
    ///
    /// The count rather than the ids, because nothing shared looks at more than whether there are
    /// any; the ids themselves are bound at the call site that knows which column they filter.
    /// </summary>
    private readonly record struct StatSelector(
        int StatIdCount,
        bool HasSockets,
        SocketScope Sockets,
        StatSurface Surface,
        StatListScope? Lists,
        bool HasLayer,
        int Layer,
        bool HasLayerMax,
        int LayerMax)
    {
        public static StatSelector Of(StatCondition condition) => new(
            condition.StatIds.Count, condition.HasSockets, condition.Sockets, condition.Surface,
            condition.Lists, condition.HasLayer, condition.Layer, condition.HasLayerMax,
            condition.LayerMax);

        // An ordering names one exact layer or none: a span of layers is a filtering shape, and
        // ranking across one would sort by which skill an item carries rather than by a value.
        public static StatSelector Of(StatOrder order) => new(
            order.StatIds.Count, order.HasSockets, order.Sockets, order.Surface,
            order.Lists, order.HasLayer, order.Layer, HasLayerMax: false, LayerMax: 0);
    }
}

/// <summary>
/// A caller error in a <see cref="SearchItemsRequest" />. Its own type rather than
/// <see cref="ArgumentException" /> so the service can map exactly this to InvalidArgument
/// without also swallowing an argument error thrown from inside the data provider.
/// </summary>
internal sealed class InvalidSearchRequestException(string message) : Exception(message);
