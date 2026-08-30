using Microsoft.Data.Sqlite;

namespace D2BotNG.Capture;

/// <summary>
/// The captures database schema, and its versioned upgrade path.
///
/// Items are decomposed rather than stored as a blob because searching them by stat is the
/// whole reason this store exists: a stat predicate has to be a join, not a scan through
/// deserialized documents. Everything else — identity, skills, progression, kills, area time —
/// is stored relationally too, but only because it is cheap to; nothing queries across it yet.
///
/// The database is derived state, not a system of record. It holds what running bots reported,
/// and a bot re-reports its whole character on the next game it enters, so a corrupt or deleted
/// file costs the history of stopped profiles and nothing else. That is why the recovery path
/// for a schema too new to read is to delete and recreate rather than to fail startup.
/// </summary>
internal static class CaptureSchema
{
    /// <summary>
    /// Bumped whenever <see cref="Ddl" /> changes shape. A file at any OTHER version — older or
    /// newer — is deleted and recreated rather than migrated, because nothing has shipped and so
    /// there is no file in the world to preserve. Migrating only backwards-versioned files would
    /// also contradict this store's own policy: a file it cannot read is discarded precisely
    /// because captures are derived state, and that is no less true of an old one than a new one.
    ///
    /// A migration chain earns its place the day a bump would cost the accumulated kill and
    /// area-time totals — the only things a bot does not re-report on its next game.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Owner discriminator on containers, stats and skills. Matches the Owner proto enum.
    /// </summary>
    public const int OwnerPlayer = 0;

    public const int OwnerMerc = 1;

    /// <summary>Kind discriminator on the progression table.</summary>
    public const int ProgressionQuest = 0;

    public const int ProgressionWaypoint = 1;

    // The container.name column. The proto names these as fields; storage keeps them as one
    // table keyed by name, so these are the bridge between the two.
    public const string ContainerEquipped = "equipped";
    public const string ContainerInventory = "inventory";
    public const string ContainerCube = "cube";
    public const string ContainerBelt = "belt";
    public const string ContainerStash = "stash";

    private const string Ddl = """
        -- One row per profile: the player wearer's identity and live position.
        CREATE TABLE character (
            profile          TEXT    PRIMARY KEY,
            account          TEXT    NOT NULL DEFAULT '',
            realm            TEXT    NOT NULL DEFAULT '',
            char_name        TEXT    NOT NULL DEFAULT '',
            char_class       INTEGER NOT NULL DEFAULT 0,
            level            INTEGER NOT NULL DEFAULT 0,
            char_flags       INTEGER NOT NULL DEFAULT 0,
            flags_ex         INTEGER NOT NULL DEFAULT 0,
            ladder           INTEGER NOT NULL DEFAULT 0,
            -- NULL until an identity section names it. 0 is a real difficulty (Normal), so a
            -- default would be indistinguishable from "never told" — and kills accumulated
            -- under the wrong difficulty never self-correct.
            difficulty       INTEGER,
            area             INTEGER NOT NULL DEFAULT 0,
            hand             INTEGER NOT NULL DEFAULT 0,
            game_id          TEXT    NOT NULL DEFAULT '',
            updated_at       INTEGER NOT NULL DEFAULT 0,  -- epoch ms, game-side assembly time
            area_entered_at  INTEGER                      -- epoch ms; NULL until a real in-game entry
        ) STRICT;

        -- The active mercenary. Deleted when a keyframe arrives without one - see the dismissal
        -- rule on CaptureStore.Apply, which is not something the engine can report directly.
        CREATE TABLE merc (
            profile   TEXT PRIMARY KEY REFERENCES character(profile) ON DELETE CASCADE,
            name      TEXT    NOT NULL DEFAULT '',
            class_id  INTEGER NOT NULL DEFAULT 0,
            flags_ex  INTEGER NOT NULL DEFAULT 0
        ) STRICT;

        -- Merged wearer stats: GetStat off FullStats, so they already carry gear contributions.
        -- Unlike item stats these are NOT raw - the engine sign-extends per itemstatcost.
        CREATE TABLE wearer_stat (
            profile  TEXT    NOT NULL REFERENCES character(profile) ON DELETE CASCADE,
            owner    INTEGER NOT NULL,
            stat_id  INTEGER NOT NULL,
            value    INTEGER NOT NULL,
            PRIMARY KEY (profile, owner, stat_id)
        ) STRICT;

        CREATE TABLE wearer_skill (
            profile      TEXT    NOT NULL REFERENCES character(profile) ON DELETE CASCADE,
            owner        INTEGER NOT NULL,
            skill_id     INTEGER NOT NULL,
            hard_points  INTEGER NOT NULL,
            level        INTEGER NOT NULL,  -- bonused; the gear contribution is level - hard_points
            PRIMARY KEY (profile, owner, skill_id)
        ) STRICT;

        -- Completed quests / active waypoints, per difficulty. The engine reports only the
        -- difficulty it is currently in, so rows for the others persist from when they were
        -- last played - which is the point of keying by difficulty rather than replacing.
        CREATE TABLE progression (
            profile     TEXT    NOT NULL REFERENCES character(profile) ON DELETE CASCADE,
            difficulty  INTEGER NOT NULL,
            kind        INTEGER NOT NULL,  -- 0 quest, 1 waypoint
            entry_id    INTEGER NOT NULL,
            PRIMARY KEY (profile, difficulty, kind, entry_id)
        ) STRICT;

        -- Lifetime kill counts, accumulated from the per-send deltas the engine reports.
        -- Regular monsters are keyed by (class id, SpecType rarity) and super-uniques by
        -- SuperUniques.txt index; super_unique keeps the two disjoint, so a super-unique is
        -- never also counted under its class.
        CREATE TABLE kill (
            profile       TEXT    NOT NULL REFERENCES character(profile) ON DELETE CASCADE,
            difficulty    INTEGER NOT NULL,
            super_unique  INTEGER NOT NULL,
            entry_id      INTEGER NOT NULL,
            spec          INTEGER NOT NULL,
            count         INTEGER NOT NULL,
            PRIMARY KEY (profile, difficulty, super_unique, entry_id, spec)
        ) STRICT;

        -- Lifetime milliseconds spent per area, accumulated from the gap between in-game updates.
        CREATE TABLE area_time (
            profile       TEXT    NOT NULL REFERENCES character(profile) ON DELETE CASCADE,
            difficulty    INTEGER NOT NULL,
            area          INTEGER NOT NULL,
            milliseconds  INTEGER NOT NULL,
            PRIMARY KEY (profile, difficulty, area)
        ) STRICT;

        -- A grid (inventory/stash page/cube/belt) or slot set (equipped). Replaced wholesale
        -- whenever the engine re-sends it, which it does only when its contents changed.
        CREATE TABLE container (
            id       INTEGER PRIMARY KEY,
            profile  TEXT    NOT NULL REFERENCES character(profile) ON DELETE CASCADE,
            owner    INTEGER NOT NULL,
            name     TEXT    NOT NULL,  -- equipped | inventory | cube | belt | stash
            label    TEXT    NOT NULL DEFAULT '',
            page     INTEGER NOT NULL DEFAULT 0,
            width    INTEGER NOT NULL DEFAULT 0,
            height   INTEGER NOT NULL DEFAULT 0,
            UNIQUE (profile, owner, name, page)
        ) STRICT;

        -- One row per captured unit: a stored item, or a socket filler inside one.
        --
        -- gid is the game-session unit id and is unique only within one game_id - the same
        -- physical item gets a new one next game - so it cannot be the key. id is the surrogate
        -- the child tables join on. parent_id/socket_index carry ownership (which item this is
        -- socketed into, and which socket); root_id is the denormalisation that makes the common
        -- query cheap, since "this item INCLUDING its socketed gems" is then `root_id = ?`
        -- rather than a recursive CTE. A top-level item is its own root.
        CREATE TABLE item (
            id            INTEGER PRIMARY KEY,
            container_id  INTEGER NOT NULL REFERENCES container(id) ON DELETE CASCADE,
            parent_id     INTEGER REFERENCES item(id) ON DELETE CASCADE,
            root_id       INTEGER NOT NULL,
            socket_index  INTEGER,  -- NULL when parent_id IS NULL
            profile       TEXT    NOT NULL,  -- denormalised: search filters by profile before joining

            gid           INTEGER NOT NULL,
            unit_type     INTEGER NOT NULL,
            class_id      INTEGER NOT NULL,
            code          TEXT    NOT NULL,
            quality       INTEGER NOT NULL,
            item_flags    INTEGER NOT NULL,
            format        INTEGER NOT NULL,
            file_index    INTEGER NOT NULL,
            -- dwItemLevel. Not a requirement and not the character's level: what the item rolled
            -- at, and so which affixes it could have. Searchable in its own right, and what the
            -- tooltip library needs to narrow item-level-dependent roll ranges.
            item_level    INTEGER NOT NULL,
            rare_prefix   INTEGER NOT NULL,
            rare_suffix   INTEGER NOT NULL,
            auto_affix    INTEGER NOT NULL,
            -- D2's wMagicPrefix[3]/wMagicSuffix[3]. One column per slot because SQLite has no
            -- array type and the arity is fixed by the game: a comma-joined string was neither
            -- typed nor searchable, and a child table would buy indexability worth nothing at
            -- this size (a full top-level item scan is ~2ms). 0 = empty slot; the slot POSITION
            -- is meaningful, so an empty one is stored rather than skipped.
            magic_prefix_0  INTEGER NOT NULL DEFAULT 0,
            magic_prefix_1  INTEGER NOT NULL DEFAULT 0,
            magic_prefix_2  INTEGER NOT NULL DEFAULT 0,
            magic_suffix_0  INTEGER NOT NULL DEFAULT 0,
            magic_suffix_1  INTEGER NOT NULL DEFAULT 0,
            magic_suffix_2  INTEGER NOT NULL DEFAULT 0,
            ear_level     INTEGER NOT NULL,
            player_name   TEXT    NOT NULL DEFAULT '',
            gfx_index     INTEGER NOT NULL,
            -- The item's name line, and the only game-rendered string kept. The whole TOOLTIP used
            -- to live beside it; it is gone because the producer no longer sends one, and because
            -- the tooltip library derives it from the columns around here anyway. A stored copy was
            -- also 500-odd characters per item of the one thing in this table nothing can query.
            title         TEXT    NOT NULL DEFAULT '',
            location      INTEGER NOT NULL,
            x             INTEGER NOT NULL,
            y             INTEGER NOT NULL,
            width         INTEGER NOT NULL,
            height        INTEGER NOT NULL,

            -- Resolved from the game's data tables at INSERT, because that is the only point
            -- where the owning framework - and so which install's tables apply - is known: a
            -- search spanning every profile spans every framework, and there is no framework
            -- column here to disambiguate with.
            --
            -- NULL when the item's class id resolved to no row - a modded or unknown base - which
            -- is honest rather than convenient: a defaulted tier would claim every unresolved item
            -- is normal. Such a row simply fails these filters instead of being counted as one
            -- kind. Nothing re-ingests to fill them in; bots re-report their whole inventory each
            -- game, so rows refresh within minutes.
            -- No class restriction column: that is a property of the item TYPE, so it is already
            -- reachable by filtering on the class's type subtree.
            tier          INTEGER,  -- 0 normal, 1 exceptional, 2 elite
            type_0        INTEGER,  -- wType[0], the LEAF ItemTypes.txt row; broad categories are
            type_1        INTEGER,  -- wType[1]  expanded to descendants at query time instead
            req_level     INTEGER,  -- highest of the base and every affix the item rolled
            req_str       INTEGER,  -- base requirement with -requirements% and ethereal applied
            req_dex       INTEGER,
            -- Both ends of each damage line the tooltip would draw, resolved at ingest like the
            -- requirements above. One pair per KIND rather than one aggregate pair, because the
            -- game draws them as separate lines under separate rules: a two-handed weapon has no
            -- one-hand line, a throwing weapon has a throw line beside its own, and a reader asking
            -- about one is asking about THAT line. NULL where the item draws no such line, which is
            -- most of them.
            --
            -- Only the MAX columns are reachable through the contract today (ItemColumn orders by
            -- them; nothing filters on damage at all). The min columns are stored anyway, because
            -- the line is a RANGE and half of one answers no question a damage filter would ask —
            -- and because filling them later would mean waiting for every profile to re-report,
            -- where storing them now costs an integer per weapon.
            --
            -- Not the same as the item's damage STATS. 21/22 are the one-handed pair alone, and the
            -- drawn number is the game's arithmetic over them (INV_CalcWeaponDamageRange) rather
            -- than the stored value. A throwing potion's line lands in the throw pair: the game
            -- labels it with the same string, and it is the only line such an item draws.
            damage_1h_min    INTEGER,
            damage_1h_max    INTEGER,
            damage_2h_min    INTEGER,
            damage_2h_max    INTEGER,
            damage_throw_min INTEGER,
            damage_throw_max INTEGER
        ) STRICT;

        -- One leaf stat array off the item's statlist chain. state_no and flags are the game's
        -- own provenance fields, stored verbatim and uninterpreted: 165-170 are the set tiers,
        -- 171 a runeword, and flags say whether the array currently contributes.
        CREATE TABLE statlist (
            id        INTEGER PRIMARY KEY,
            item_id   INTEGER NOT NULL REFERENCES item(id) ON DELETE CASCADE,
            ordinal   INTEGER NOT NULL,  -- position in the item's list, so order survives
            state_no  INTEGER NOT NULL,
            flags     INTEGER NOT NULL
        ) STRICT;

        -- One stat. value is RAW - pre nValShift, pre op resolution - exactly as captured.
        --
        -- WITHOUT ROWID, keyed by its natural (statlist_id, ordinal): this is the largest table
        -- by far, and the key makes the primary key itself the statlist lookup index rather than
        -- needing a second one. Measured at 337k stats: 21% off the whole file, no write cost,
        -- because inserts arrive in key order (statlist_id monotonic, ordinal ascending) and are
        -- therefore pure appends.
        CREATE TABLE stat (
            statlist_id  INTEGER NOT NULL REFERENCES statlist(id) ON DELETE CASCADE,
            ordinal      INTEGER NOT NULL,
            stat_id      INTEGER NOT NULL,
            value        INTEGER NOT NULL,
            layer        INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (statlist_id, ordinal)
        ) STRICT, WITHOUT ROWID;

        -- What the item's stats ADD UP TO, as the game resolves them: its base array, its own
        -- affix / unique / setitems / runeword nodes, what its socket fillers grant, and op 13
        -- applied. One row per (stat, layer). Written from D2ItemToolkit's MergedStats.
        --
        -- ALONGSIDE the raw statlist rows, never instead of them. The two answer different
        -- questions and a search names which it wants: raw is "has a SOURCE of >= N", which is what
        -- provenance questions need ("which of my items has a gem granting magic find"); merged is
        -- "has >= N", which is the number the tooltip prints and what a reader compares against.
        -- Making raw mean the sum instead would have changed every query already expressible, and
        -- silently: "all resistances <= 20" matches a 15+15 item today and would stop.
        --
        -- What is NOT here, and why the totals can differ from what the game currently grants:
        --
        --   * Socket fillers on a WORN SET PIECE. ITEM_RecalcAllEquippedItems detaches an equipped
        --     set item's stat list and rebuilds it with set state only, so the game can grant a
        --     worn Tal Rasha's Crest with an Um in it All Resistances +15 rather than 30. We index
        --     30 on purpose: an item must not drop out of a search because something equipped it,
        --     and the discarded value is not stable enough to index in any case - it returns on a
        --     re-socket or re-equip and goes on the next recalc, and one session's captures of the
        --     same equipped helm hold both numbers.
        --   * Earned SET TIER bonuses (state 165-170). Those exist only because the wearer has N
        --     other pieces, so indexing them would make a belt's defence fall from 98 to 38 the
        --     moment it is muled. Same instability, same answer.
        --   * PACKED encodings - charges, the by-time triples. A packed word is not a quantity, so
        --     it can be neither summed nor compared against a bound. The toolkit names the ones it
        --     left out, and those searches go to the raw rows, where they belong anyway.
        CREATE TABLE merged_stat (
            item_id     INTEGER NOT NULL REFERENCES item(id) ON DELETE CASCADE,
            stat_id     INTEGER NOT NULL,
            layer       INTEGER NOT NULL,
            value       INTEGER NOT NULL,  -- the total, socket fillers folded in
            -- The same total WITHOUT the fillers, so "30 all resistances of its own" and "30 with
            -- a rune in it" are different searches. NULL when the stat exists only because of a
            -- filler, which needs no special case at query time: a NULL satisfies no comparison,
            -- so such a row simply fails a bound asked of the item's own total.
            value_host  INTEGER,
            PRIMARY KEY (item_id, stat_id, layer)
        ) STRICT, WITHOUT ROWID;

        CREATE INDEX item_by_container ON item(container_id);
        CREATE INDEX item_by_root      ON item(root_id);
        CREATE INDEX item_by_profile   ON item(profile);
        -- Not optional: parent_id is a self-referencing FK with ON DELETE CASCADE, and SQLite
        -- enforces that by probing the child table once per deleted row. Unindexed it degrades
        -- to a full scan of `item` per row, on the hottest write path there is (every container
        -- replacement and every game change), under the store's single lock — measured at 1369ms
        -- vs 4ms over 40k rows, and it worsens quadratically.
        CREATE INDEX item_by_parent    ON item(parent_id);
        -- ordinal carried so the index also SATISFIES the read path's `ORDER BY item_id, ordinal`
        -- (a page of items is read with one query over every list it owns), which on item_id alone
        -- costs a temp B-tree sort over the whole page. `stat` gets the same ordering for free
        -- from its WITHOUT ROWID primary key.
        CREATE INDEX statlist_by_item  ON statlist(item_id, ordinal);
        -- stat's lookup-by-statlist index is its PRIMARY KEY; it needs no separate one.

        -- What the stat-first search drives from, and the single most load-bearing index here.
        --
        -- statlist_id sits BEFORE value deliberately, though only value is ever compared. The CTE
        -- seeks on stat_id and then must join back to statlist, so without statlist_id in the
        -- index every matching row costs a random lookup into the table. Carrying it makes those
        -- lookups sequential and the index covering: measured 7-13x end-to-end across every
        -- search shape (162ms -> 24ms for a plain stat bound over 337k stats).
        --
        -- value earns no better position. SQLite can only range-scan it after an EQUALITY on
        -- everything to its left, and the common case — an unlayered stat with a min/max — emits
        -- no layer clause at all, so value is a residual filter there whatever its position.
        CREATE INDEX stat_by_stat      ON stat(stat_id, layer, statlist_id, value);

        -- The merged counterpart, ordered on the same reasoning: seek on (stat_id, layer), carry
        -- item_id so the join back to `item` is covered, and leave the values as residual filters.
        --
        -- BOTH values are carried because a condition compares one or the other, never both: the
        -- socket scope picks value_host — the item's own total, ignoring what is socketed into it
        -- — and value otherwise. Carrying only one leaves the other's searches uncovered, paying a
        -- table lookup per matching row for a column the index could have held.
        CREATE INDEX merged_by_stat    ON merged_stat(stat_id, layer, item_id, value, value_host);

        -- "Find my SoJ" is a (quality, file_index) pair, since the game overloads file_index per
        -- quality. PARTIAL on purpose: unqualified `quality = 7` is better served by a scan, and
        -- an unconditional index here made those searches measurably worse by tempting the
        -- planner. file_index is -1 until an item is identified, so the predicate also skips
        -- exactly the rows that can never match.
        --
        -- The partial predicate only pays off because the search repeats it verbatim: SQLite uses
        -- a partial index only where the query's own terms IMPLY the index's, and `file_index = ?`
        -- does not imply `>= 0`. See SearchQueryBuilder.AppendIdentity.
        CREATE INDEX item_by_specific   ON item(quality, file_index) WHERE file_index >= 0;
        """;

    /// <summary>
    /// Creates the schema in a new file, and reports whether an existing one is usable. A file at
    /// any other version is discarded rather than read: the caller recreates it, which is safe
    /// precisely because this store is derived state.
    /// </summary>
    /// <returns>False when the file is at another version and must be recreated.</returns>
    public static bool TryUpgrade(SqliteConnection connection)
    {
        Execute(connection, "PRAGMA foreign_keys = ON");
        // WAL is for commit latency, NOT reader/writer concurrency — one connection behind one
        // lock has none to protect. NORMAL sync is the right trade for derived state: a torn tail
        // after a power cut costs one snapshot, and the bot re-reports everything next game.
        Execute(connection, "PRAGMA journal_mode = WAL");
        Execute(connection, "PRAGMA synchronous = NORMAL");
        // The default 2MB page cache is smaller than a populated captures.db, which showed up as
        // real time on the WRITE path (a container replacement 8.1ms -> 6.8ms at 40 profiles).
        Execute(connection, "PRAGMA cache_size = -32000");
        // Nothing else opens this file, so contention is not expected — but a second manager
        // instance or a DB browser pointed at it would otherwise fail instantly rather than wait.
        Execute(connection, "PRAGMA busy_timeout = 5000");
        // Without a limit SQLite REUSES the WAL after a checkpoint rather than shrinking it, so
        // the file keeps whatever high-water mark one busy moment produced for the rest of the
        // run. This caps that; the checkpoint threshold still governs how much is in flight.
        Execute(connection, "PRAGMA journal_size_limit = 8388608");

        var current = QueryUserVersion(connection);
        if (current == Version) return true;
        if (current != 0) return false;

        using var transaction = connection.BeginTransaction();
        Execute(connection, Ddl, transaction);
        Execute(connection, $"PRAGMA user_version = {Version}", transaction);
        transaction.Commit();
        return true;
    }

    private static int QueryUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }
}
