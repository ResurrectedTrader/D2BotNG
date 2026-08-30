using D2BotNG.Core.Protos.Captures;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Data.Sqlite;

namespace D2BotNG.Capture;

/// <summary>
/// The read half of <see cref="CaptureStore" />: reassembling captured characters and items out
/// of the decomposed tables, and searching items by stat.
/// </summary>
public sealed partial class CaptureStore
{
    /// <summary>Page size used when a search request does not ask for one.</summary>
    private const int DefaultSearchLimit = 200;

    /// <summary>Upper bound on a search page, so one call cannot ask for the whole database.</summary>
    private const int MaxSearchLimit = 1000;

    /// <summary>Unit types stamped when a wearer is rebuilt from its row: UNIT_PLAYER, UNIT_MONSTER.</summary>
    private const int PlayerUnitType = 0;

    private const int MonsterUnitType = 1;

    /// <summary>
    /// Metadata for every captured character: one row each, one query, no joins. Deliberately
    /// not a Character — a selector has no use for stats or inventories, and shipping a
    /// half-filled one would leave a caller unable to tell "no items" from "not loaded".
    /// </summary>
    public List<CharacterSummary> ListCharacters()
    {
        lock (_lock) return ReadSummaries(null);
    }

    /// <summary>
    /// Every summary, or just one profile's. Same columns either way.
    ///
    /// Takes no lock: the callers do, and <see cref="Apply" /> reads its result inside the very
    /// transaction's lock so the summary it hands back cannot describe some later snapshot.
    /// </summary>
    private List<CharacterSummary> ReadSummaries(string? profile)
    {
        var summaries = new List<CharacterSummary>();
        if (_connection == null) return summaries;

        (string Name, object? Value)[] args = profile == null ? [] : [("$profile", profile)];
        Read(
            $"""
             SELECT profile, account, realm, char_flags, ladder, difficulty,
                    char_name, char_class, level, updated_at
               FROM character
              {(profile == null ? "" : "WHERE profile = $profile")}
              ORDER BY profile
             """,
            reader => summaries.Add(new CharacterSummary
            {
                Profile = reader.GetString(0),
                Identity = ReadIdentity(reader),
                Name = reader.GetString(6),
                ClassId = reader.GetInt32(7),
                Level = reader.GetInt32(8),
                UpdatedAt = ToTimestamp(reader.GetInt64(9)),
            }),
            args);

        return summaries;
    }

    /// <summary>
    /// Both character queries select the identity columns first and in the same order, so one
    /// reader serves both. A NULL difficulty means no identity section has named one yet; 0
    /// (Normal) is the closest truth to serve, and the store itself keeps the distinction.
    /// </summary>
    private static Identity ReadIdentity(SqliteDataReader reader) => new()
    {
        Account = reader.GetString(1),
        Realm = reader.GetString(2),
        CharFlags = (uint)reader.GetInt64(3),
        Ladder = reader.GetInt32(4) != 0,
        Difficulty = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
    };

    /// <summary>
    /// One character, whole — both wearers with every container, item, stat list and socket
    /// filler. This is what a viewer renders from, so nothing is left unloaded.
    /// </summary>
    public Character? GetCharacter(string profile)
    {
        lock (_lock)
        {
            if (_connection == null) return null;

            Character? character = null;
            Read(
                """
                SELECT profile, account, realm, char_flags, ladder, difficulty,
                       char_name, char_class, flags_ex, area, hand, game_id, updated_at,
                       area_entered_at
                  FROM character WHERE profile = $p
                """,
                reader => character = new Character
                {
                    Profile = reader.GetString(0),
                    Identity = ReadIdentity(reader),
                    // The player IS the character — name, class, flags and position live on the
                    // wearer, exactly as the engine reports them.
                    Player = new Unit
                    {
                        UnitType = PlayerUnitType,
                        Name = reader.GetString(6),
                        ClassId = reader.GetInt32(7),
                        FlagsEx = (uint)reader.GetInt64(8),
                        Area = reader.GetInt32(9),
                        Hand = reader.GetInt32(10),
                    },
                    GameId = reader.GetString(11),
                    UpdatedAt = ToTimestamp(reader.GetInt64(12)),
                    AreaEnteredAt = reader.IsDBNull(13) ? null : ToTimestamp(reader.GetInt64(13)),
                },
                ("$p", profile));

            if (character == null) return null;

            Populate(character);
            ReadContainers(character);

            return character;
        }
    }

    /// <summary>
    /// Items matching the request. Groups are AND-ed; within a group the predicates are counted
    /// and the count constrained, which is what expresses OR, at-least-N and negation.
    ///
    /// Generated STAT-FIRST: each predicate becomes a small set of matching item ids built from
    /// the stat side (where the selectivity is, and where the index is), and the item query then
    /// semi-joins against those sets. The previous shape hung a correlated EXISTS off every
    /// candidate item, which could never use a stat index and could not express counting at all.
    /// </summary>
    public SearchItemsResponse SearchItems(SearchItemsRequest request)
    {
        // Pagination is the store's own contract, so it is checked here rather than in the SQL
        // builder — but on the same terms: rejected, not repaired. Clamping an oversized limit
        // would drop every item past the ceiling from a page the caller believes is complete,
        // and its paging arithmetic depends on getting the size it asked for.
        if (request.Offset < 0)
            throw new InvalidSearchRequestException($"offset {request.Offset} is negative");

        if (request.Limit < 0 || request.Limit > MaxSearchLimit)
        {
            throw new InvalidSearchRequestException(
                $"limit {request.Limit} is outside 0..{MaxSearchLimit} (0 = server default)");
        }

        // Built before the lock is taken: it validates and composes, touching no connection, and
        // it is also where a malformed request throws. Doing that inside the lock would stall
        // ingest for pure CPU work — and, on the rejection path, for work that never had a query
        // to run.
        var sql = new SearchQueryBuilder(request, _tooltip);
        var args = sql.Parameters;

        lock (_lock)
        {
            var response = new SearchItemsResponse();
            if (_connection == null) return response;

            // The container join is not optional here: the predicate may filter on c.name or
            // c.owner, so the count must be taken over the same shape as the page.
            using (var count = Command(
                       $"""
                        {sql.Ctes}SELECT COUNT(*)
                          FROM item i
                          JOIN container c ON c.id = i.container_id
                         WHERE {sql.Where}
                        """, null, args))
            {
                response.Total = Convert.ToInt32(count.ExecuteScalar() ?? 0);
            }

            var limit = request.Limit > 0 ? request.Limit : DefaultSearchLimit;
            var matches = new List<(long RootId, ItemMatch Match)>();

            using (var command = Command(
                       $"""
                        {sql.Ctes}SELECT i.id, i.profile, c.owner, c.name, c.page
                          FROM item i
                          JOIN container c ON c.id = i.container_id{sql.OrderJoin}
                         WHERE {sql.Where}
                         ORDER BY {sql.OrderBy}
                         LIMIT {limit} OFFSET {request.Offset}
                        """, null, args))
            {
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    matches.Add((reader.GetInt64(0), new ItemMatch
                    {
                        Profile = reader.GetString(1),
                        Owner = (Owner)reader.GetInt32(2),
                        Container = reader.GetString(3),
                        Page = reader.GetInt32(4),
                    }));
                }
            }

            if (matches.Count == 0) return response;

            // Rebuild the whole page in one pass, so a result is the complete item — socket
            // fillers, stat lists and all — rather than a row the caller would have to finish.
            // Per-match rebuilds would be three queries EACH, up to 3000 for a full page, every
            // one of them holding the store's lock against ingest.
            //
            // The ids are interpolated rather than parameterised on purpose: they are longs this
            // method just read out of the database, so there is nothing to inject, and a page of
            // 1000 would otherwise sit near SQLite's bound-parameter ceiling.
            var rootIds = string.Join(',', matches.Select(m => m.RootId));
            var trees = ReadItemTree($"i.root_id IN ({rootIds})")
                .ToDictionary(tree => tree.Id, tree => tree.Unit);

            foreach (var (rootId, match) in matches)
            {
                if (!trees.TryGetValue(rootId, out var item)) continue;
                match.Item = item;
                response.Results.Add(match);
            }

            return response;
        }
    }

    // -----------------------------------------------------------------------
    // Character
    // -----------------------------------------------------------------------

    /// <summary>Everything hanging off the character row: the merc, both wearers' stats and
    /// skills, and the accumulated totals.</summary>
    private void Populate(Character character)
    {
        var byOwner = ("$p", (object?)character.Profile);

        // The merc row is read FIRST because it decides whether there is a merc at all. Its
        // stats and skills are fingerprinted separately from its unit document, so owner-1 rows
        // can outlive it; conjuring a wearer from those would serve a mercenary that was never
        // captured, with no name and the wrong unit type.
        Read("SELECT name, class_id, flags_ex FROM merc WHERE profile = $p",
            reader => character.Merc = new Unit
            {
                // A mercenary is a wearer, so it rebuilds into the same message as the player.
                UnitType = MonsterUnitType,
                Name = reader.GetString(0),
                ClassId = reader.GetInt32(1),
                FlagsEx = (uint)reader.GetInt64(2),
            },
            byOwner);

        Read("SELECT owner, stat_id, value FROM wearer_stat WHERE profile = $p ORDER BY owner, stat_id",
            reader => Wearer(reader.GetInt32(0))?.Stats.Add(
                new Stat { Id = reader.GetInt32(1), Value = reader.GetInt64(2) }),
            byOwner);

        Read(
            """
            SELECT owner, skill_id, hard_points, level FROM wearer_skill
             WHERE profile = $p ORDER BY owner, skill_id
            """,
            reader => Wearer(reader.GetInt32(0))?.Skills.Add(new Skill
            {
                SkillId = reader.GetInt32(1),
                HardPoints = reader.GetInt32(2),
                Level = reader.GetInt32(3),
            }),
            byOwner);

        var byDifficulty = new Dictionary<int, Progression>();
        Read(
            """
            SELECT difficulty, kind, entry_id FROM progression
             WHERE profile = $p ORDER BY difficulty, kind, entry_id
            """,
            reader =>
            {
                var difficulty = reader.GetInt32(0);
                if (!byDifficulty.TryGetValue(difficulty, out var progression))
                {
                    progression = new Progression { Difficulty = difficulty };
                    byDifficulty[difficulty] = progression;
                    character.Progression.Add(progression);
                }

                if (reader.GetInt32(1) == CaptureSchema.ProgressionQuest) progression.Quests.Add(reader.GetInt32(2));
                else progression.Waypoints.Add(reader.GetInt32(2));
            },
            byOwner);

        Read(
            """
            SELECT difficulty, super_unique, entry_id, spec, count FROM kill
             WHERE profile = $p ORDER BY difficulty, super_unique, entry_id, spec
            """,
            reader => character.Kills.Add(new Kill
            {
                Difficulty = reader.GetInt32(0),
                SuperUnique = reader.GetInt32(1) != 0,
                Id = reader.GetInt32(2),
                Spec = reader.GetInt32(3),
                Count = reader.GetInt64(4),
            }),
            byOwner);

        Read(
            """
            SELECT difficulty, area, milliseconds FROM area_time
             WHERE profile = $p ORDER BY difficulty, area
            """,
            reader => character.AreaTime.Add(new AreaTime
            {
                Difficulty = reader.GetInt32(0),
                Area = reader.GetInt32(1),
                Milliseconds = reader.GetInt64(2),
            }),
            byOwner);

        return;

        // Null for a merc that has no row, which drops its orphaned stats and skills.
        Unit? Wearer(int owner) =>
            owner == CaptureSchema.OwnerPlayer ? character.Player : character.Merc;
    }

    // -----------------------------------------------------------------------
    // Containers and items
    // -----------------------------------------------------------------------

    /// <summary>
    /// Rebuilds both wearers' containers in the shape the engine sent them. Storage keeps one row
    /// per page, so the stash's rows are folded back under `pages` — leaving the served document
    /// the same one that was captured.
    /// </summary>
    private void ReadContainers(Character character)
    {
        // Both owners in one query, for the same reason the items below are read in one: a
        // per-wearer call costs a container query plus ReadItemTree's three, so a character with a
        // mercenary would take eight queries where four do — all of them holding the store's lock
        // against ingest, on the path the UI refetches from.
        var rows = new List<(long Id, int Owner, string Name, string Label, int Page, int Width, int Height)>();
        using (var command = Command(
                   """
                   SELECT id, owner, name, label, page, width, height FROM container
                    WHERE profile = $p ORDER BY owner, name, page
                   """, null, ("$p", character.Profile)))
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                    reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6)));
            }
        }

        // Every container in ONE call. ReadItemTree costs three queries whatever the predicate
        // matches, so calling it per container makes a character fetch 18-45 queries where three
        // do. Ids are interpolated for the same reason the search page does it: they are longs
        // this method just read out of this database, so there is nothing to inject.
        var byContainer = new Dictionary<long, List<Unit>>();
        if (rows.Count > 0)
        {
            var ids = string.Join(',', rows.Select(row => row.Id));
            foreach (var (_, containerId, unit) in ReadItemTree($"i.container_id IN ({ids})"))
            {
                if (!byContainer.TryGetValue(containerId, out var bucket))
                    byContainer[containerId] = bucket = [];

                bucket.Add(unit);
            }
        }

        character.Player.Containers = new Containers();
        if (character.Merc != null) character.Merc.Containers = new Containers();

        foreach (var (id, owner, name, label, page, width, height) in rows)
        {
            // Null for a merc that has no row, which drops its orphaned containers — the same
            // rule Populate applies to orphaned stats and skills.
            var containers = owner == CaptureSchema.OwnerPlayer
                ? character.Player.Containers
                : character.Merc?.Containers;
            if (containers == null) continue;

            List<Unit> items = byContainer.TryGetValue(id, out var contents) ? contents : [];
            switch (name)
            {
                case CaptureSchema.ContainerEquipped:
                    containers.Equipped = Grid();
                    break;
                case CaptureSchema.ContainerInventory:
                    containers.Inventory = Grid();
                    break;
                case CaptureSchema.ContainerCube:
                    containers.Cube = Grid();
                    break;
                case CaptureSchema.ContainerBelt:
                    containers.Belt = Grid();
                    break;
                case CaptureSchema.ContainerStash:
                    // Storage keeps one row per page; the stash itself is only their holder, so
                    // the rows become pages under it rather than one of them becoming the stash.
                    (containers.Stash ??= new Stash()).Pages.Add(new StashPage
                    {
                        Index = page,
                        Name = label,
                        Width = width,
                        Height = height,
                        Items = { items },
                    });
                    break;
            }

            continue;

            Container Grid() => new() { Width = width, Height = height, Items = { items } };
        }
    }

    /// <summary>
    /// Loads every item matching <paramref name="where" /> and reassembles the socket nesting,
    /// returning the roots paired with their row id and the container they sit in.
    ///
    /// Three queries regardless of how many items match — items, then all their stat lists, then
    /// all those lists' stats — so the caller widens the predicate rather than calling this in a
    /// loop, and buckets the results by container id afterwards. A root's own id IS its root_id,
    /// which is what lets a caller map results back.
    ///
    /// The predicate carries no bound values, deliberately: it selects on row ids this class just
    /// read out of this same database, so both callers interpolate them and a page of a thousand
    /// costs no parameters at all.
    /// </summary>
    private List<(long Id, long ContainerId, Unit Unit)> ReadItemTree(string where)
    {
        var records = new Dictionary<long, Unit>();
        var order = new List<long>();
        var parents = new Dictionary<long, long?>();
        var containers = new Dictionary<long, long>();

        using (var command = Command(
                   $"""
                    SELECT i.id, i.parent_id, i.socket_index, i.gid, i.unit_type, i.class_id, i.code, i.quality,
                           i.item_flags, i.format, i.file_index, i.rare_prefix, i.rare_suffix, i.auto_affix,
                           i.ear_level, i.player_name, i.gfx_index, i.title,
                           i.location, i.x, i.y, i.width, i.height,
                           i.magic_prefix_0, i.magic_prefix_1, i.magic_prefix_2,
                           i.magic_suffix_0, i.magic_suffix_1, i.magic_suffix_2,
                           i.item_level, i.container_id
                      FROM item i
                     WHERE {where}
                     ORDER BY i.parent_id NULLS FIRST, i.socket_index, i.id
                    """, null))
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var record = new Unit
                {
                    Gid = (uint)reader.GetInt64(3),
                    UnitType = reader.GetInt32(4),
                    ClassId = reader.GetInt32(5),
                    Code = reader.GetString(6),
                    Quality = reader.GetInt32(7),
                    ItemFlags = (uint)reader.GetInt64(8),
                    Format = reader.GetInt32(9),
                    FileIndex = reader.GetInt32(10),
                    RarePrefix = reader.GetInt32(11),
                    RareSuffix = reader.GetInt32(12),
                    AutoAffix = reader.GetInt32(13),
                    EarLevel = reader.GetInt32(14),
                    PlayerName = reader.GetString(15),
                    GfxIndex = reader.GetInt32(16),
                    Title = reader.GetString(17),
                    Location = reader.GetInt32(18),
                    X = reader.GetInt32(19),
                    Y = reader.GetInt32(20),
                    Width = reader.GetInt32(21),
                    Height = reader.GetInt32(22),
                    // Appended after the magic prefix/suffix block rather than beside file_index,
                    // so the 23..28 slot indices below keep meaning what they say. Anything else
                    // this query grows goes on the end for the same reason — container_id at 30.
                    ItemLevel = reader.GetInt32(29),
                };
                // All three slots, always: the position is the slot, so trailing empties are part
                // of the shape rather than padding to be trimmed.
                for (var slot = 0; slot < 3; slot++)
                {
                    record.MagicPrefix.Add(reader.GetInt32(23 + slot));
                    record.MagicSuffix.Add(reader.GetInt32(26 + slot));
                }

                records[id] = record;
                order.Add(id);
                parents[id] = reader.IsDBNull(1) ? null : reader.GetInt64(1);
                containers[id] = reader.GetInt64(30);
            }
        }

        if (records.Count == 0) return [];

        AttachStats(records, where);

        // The query ordered fillers by socket index within each parent, so appending in row order
        // reproduces the socket ordinals the engine sent.
        var roots = new List<(long, long, Unit)>();
        foreach (var id in order)
        {
            var parent = parents[id];
            if (parent == null) roots.Add((id, containers[id], records[id]));
            else if (records.TryGetValue(parent.Value, out var host)) host.Sockets.Add(records[id]);
        }

        return roots;
    }

    private void AttachStats(Dictionary<long, Unit> records, string where)
    {
        var statLists = new Dictionary<long, StatList>();

        using (var command = Command(
                   $"""
                    SELECT sl.id, sl.item_id, sl.state_no, sl.flags
                      FROM statlist sl
                      JOIN item i ON i.id = sl.item_id
                     WHERE {where}
                     ORDER BY sl.item_id, sl.ordinal
                    """, null))
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!records.TryGetValue(reader.GetInt64(1), out var record)) continue;
                var list = new StatList { StateNo = reader.GetInt32(2), Flags = (uint)reader.GetInt64(3) };
                statLists[reader.GetInt64(0)] = list;
                record.StatsLists.Add(list);
            }
        }

        if (statLists.Count == 0) return;

        using (var command = Command(
                   $"""
                    SELECT st.statlist_id, st.stat_id, st.value, st.layer
                      FROM stat st
                      JOIN statlist sl ON sl.id = st.statlist_id
                      JOIN item i ON i.id = sl.item_id
                     WHERE {where}
                     ORDER BY st.statlist_id, st.ordinal
                    """, null))
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!statLists.TryGetValue(reader.GetInt64(0), out var list)) continue;
                list.Stats.Add(new Stat
                {
                    Id = reader.GetInt32(1),
                    Value = reader.GetInt64(2),
                    Layer = reader.GetInt32(3),
                });
            }
        }
    }

    /// <summary>Runs a query outside any transaction and hands each row to <paramref name="row" />.</summary>
    private void Read(string sql, Action<SqliteDataReader> row,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, null, parameters);
        using var reader = command.ExecuteReader();
        while (reader.Read()) row(reader);
    }

    private static Timestamp ToTimestamp(long epochMs) =>
        Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeMilliseconds(epochMs));

}
