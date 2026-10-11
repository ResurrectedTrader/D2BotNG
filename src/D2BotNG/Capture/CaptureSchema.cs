using System.Data;
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
/// The database is mostly derived state, not a system of record. It holds what running bots
/// reported, and a bot re-reports its whole character on the next game it enters, so a corrupt
/// or deleted file costs the history of stopped profiles and nothing else. That is why the
/// recovery path for a schema too new to read, or a file that cannot be read at all, is to delete
/// and recreate rather than to fail startup. The exception is what a bot does NOT re-report: the
/// lifetime kill and area-time totals only ever accumulate here, and they are why an OLDER file
/// is migrated forward rather than discarded.
/// </summary>
internal static class CaptureSchema
{
    /// <summary>
    /// Bumped whenever <see cref="Ddl" /> changes shape, with a step added to
    /// <see cref="Migrations" /> that takes a file from the previous version to this one.
    ///
    /// Older files are upgraded in place, one step at a time, inside one transaction, after a
    /// one-time copy of the original is set aside (see <see cref="BackUp" />). A step that fails
    /// rolls the whole upgrade back — never a half-converted database — and the caller then
    /// recreates the file, which is why the copy exists: a failed step would otherwise discard
    /// the accumulated totals the upgrade was written to keep. NEWER files are recreated
    /// outright: this build cannot know their shape, and captures are re-reported within minutes.
    ///
    /// History:
    ///   1  initial shape
    ///   2  stash pages carry their kind (personal / shared), type and gold; (kind, page)
    ///      replaces page alone in the container key, since PlugY and D2R number pages within
    ///      a kind and personal 0 and shared 0 are different pages
    ///   3  a capture is keyed by (profile, character name) rather than by profile: `character`
    ///      gains a surrogate id and that unique pair, and every table that hung off the profile
    ///      string hangs off the id instead. One profile playing several characters — a mule
    ///      profile — used to overwrite each with the next; now each keeps its own capture
    ///   4  `character` records which game build's data its ids index (`backend`) and its game
    ///      type; `item` records an advanced stash slot's stack size. All three are columns added
    ///      in place: nothing that existed changes meaning, so no table is rebuilt
    ///   5  `character` records the player's last-used skill, which D2R's requirement rules read,
    ///      and the game it is of (flavor, version, variant), derived and stored for search
    ///   6  a stash page records whether it is remove-only (D2R's shared tabs from a previous
    ///      season) and the season it belongs to; both columns added in place
    /// </summary>
    public const int Version = 6;

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

    /// <summary>
    /// The container table, as a function of its name because a migration has to build the new
    /// shape BESIDE the old one (SQLite cannot alter a table-level UNIQUE in place) and it must
    /// be the same shape a fresh file gets — stating it once is what guarantees that.
    /// </summary>
    private static string ContainerTable(string table) =>
        $"""
        -- A grid (inventory/stash page/cube/belt) or slot set (equipped). Replaced wholesale
        -- whenever the engine re-sends it, which it does only when its contents changed.
        --
        -- stash_kind, stash_type and gold mean something only on a stash page; the others carry
        -- the zero defaults. The kind is part of the key because a page's index is 0-based within
        -- its kind: the first personal and the first shared page are both page 0.
        CREATE TABLE {table} (
            id            INTEGER PRIMARY KEY,
            character_id  INTEGER NOT NULL REFERENCES character(id) ON DELETE CASCADE,
            owner         INTEGER NOT NULL,
            name          TEXT    NOT NULL,  -- equipped | inventory | cube | belt | stash
            label         TEXT    NOT NULL DEFAULT '',
            stash_kind    INTEGER NOT NULL DEFAULT 0,  -- StashTabKind: 0 personal, 1 shared
            stash_type    INTEGER NOT NULL DEFAULT 0,  -- StashTabType: 0 normal, 1 advanced, 2 chronicle
            page          INTEGER NOT NULL DEFAULT 0,
            gold          INTEGER NOT NULL DEFAULT 0,
            width         INTEGER NOT NULL DEFAULT 0,
            height        INTEGER NOT NULL DEFAULT 0,
            -- Version 6, last because ALTER TABLE ADD COLUMN appends them there. A stash page
            -- items can be taken off but not put on (D2R's shared tabs from a previous season),
            -- and the season a shared page belongs to; 0 / 0 everywhere else, LoD included.
            remove_only   INTEGER NOT NULL DEFAULT 0,
            season        INTEGER NOT NULL DEFAULT 0,
            UNIQUE (character_id, owner, name, stash_kind, page)
        ) STRICT;
        """;

    /// <summary>
    /// The container table as versions 3 to 5 wrote it, kept for the 2→3 step: built from the
    /// current shape, that step would already hold version 6's columns and the 5→6 step's ADD
    /// COLUMN would then fail on them.
    /// </summary>
    private static string ContainerV3Table(string table) =>
        $"""
        CREATE TABLE {table} (
            id            INTEGER PRIMARY KEY,
            character_id  INTEGER NOT NULL REFERENCES character(id) ON DELETE CASCADE,
            owner         INTEGER NOT NULL,
            name          TEXT    NOT NULL,
            label         TEXT    NOT NULL DEFAULT '',
            stash_kind    INTEGER NOT NULL DEFAULT 0,
            stash_type    INTEGER NOT NULL DEFAULT 0,
            page          INTEGER NOT NULL DEFAULT 0,
            gold          INTEGER NOT NULL DEFAULT 0,
            width         INTEGER NOT NULL DEFAULT 0,
            height        INTEGER NOT NULL DEFAULT 0,
            UNIQUE (character_id, owner, name, stash_kind, page)
        ) STRICT;
        """;

    /// <summary>
    /// One upgrade step. <paramref name="CheckTables" /> names the child tables whose foreign
    /// keys the step could have broken — the ones it rebuilt or re-pointed — and ONLY those are
    /// verified afterwards. A database-wide check would also fail on an orphan row the step never
    /// went near (a file once opened in a DB browser with foreign keys off, say), and the caller's
    /// answer to a failed upgrade is to recreate the file: a stray row from years ago must not
    /// cost the lifetime totals today.
    /// </summary>
    private sealed record MigrationStep(string Sql, params string[] CheckTables);

    /// <summary>
    /// One step per version, keyed by the version it upgrades FROM. Each runs inside the upgrade
    /// transaction with foreign keys OFF — necessary for any step that rebuilds a table, since
    /// dropping a referenced table with them on would cascade its rows away.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, MigrationStep> Migrations = new Dictionary<int, MigrationStep>
    {
        // 1 -> 2: rebuild `container` with the stash columns and the wider key. The generic
        // ALTER TABLE ... ADD COLUMN would have done for the columns, but not for the UNIQUE
        // constraint, and a key that cannot hold a shared page 0 next to a personal page 0 is
        // the thing this version exists to fix. Rows keep their ids, so `item.container_id`
        // stays valid; the REFERENCES in `item` name the table by its final name, which the
        // rename restores — which is exactly what the check on `item` confirms.
        [1] = new(ContainerV2Table("container_v2") + """

            INSERT INTO container_v2 (id, profile, owner, name, label, page, width, height)
            SELECT id, profile, owner, name, label, page, width, height FROM container;
            DROP TABLE container;
            ALTER TABLE container_v2 RENAME TO container;
            """, "container", "item"),

        // 2 -> 3: a character gets a surrogate id and the (profile, name) key, and every child
        // table re-keys from the profile string to that id. The old character table's rowid
        // becomes the id, so the children's rows map through a join on the profile they carry —
        // which is exact, because at version 2 a profile IS one character.
        //
        // `item` is the store's largest table and is rewritten IN PLACE (add the column, fill it,
        // drop the old one, put the index back) rather than copied; everything else is small and
        // rebuilt beside itself. The old `character` goes last, since every copy joins it.
        [2] = new(
            $"""
            {CharacterV3Table("character_v3")}
            INSERT INTO character_v3 (id, profile, char_name, account, realm, char_class, level, char_flags,
                                      flags_ex, ladder, difficulty, area, hand, game_id, updated_at, area_entered_at)
            SELECT rowid, profile, char_name, account, realm, char_class, level, char_flags,
                   flags_ex, ladder, difficulty, area, hand, game_id, updated_at, area_entered_at
              FROM character;

            {MercTable("merc_v3")}
            INSERT INTO merc_v3 (character_id, name, class_id, flags_ex)
            SELECT c.rowid, t.name, t.class_id, t.flags_ex
              FROM merc t JOIN character c ON c.profile = t.profile;

            {WearerStatTable("wearer_stat_v3")}
            INSERT INTO wearer_stat_v3 (character_id, owner, stat_id, value)
            SELECT c.rowid, t.owner, t.stat_id, t.value
              FROM wearer_stat t JOIN character c ON c.profile = t.profile;

            {WearerSkillTable("wearer_skill_v3")}
            INSERT INTO wearer_skill_v3 (character_id, owner, skill_id, hard_points, level)
            SELECT c.rowid, t.owner, t.skill_id, t.hard_points, t.level
              FROM wearer_skill t JOIN character c ON c.profile = t.profile;

            {ProgressionTable("progression_v3")}
            INSERT INTO progression_v3 (character_id, difficulty, kind, entry_id)
            SELECT c.rowid, t.difficulty, t.kind, t.entry_id
              FROM progression t JOIN character c ON c.profile = t.profile;

            {KillTable("kill_v3")}
            INSERT INTO kill_v3 (character_id, difficulty, super_unique, entry_id, spec, count)
            SELECT c.rowid, t.difficulty, t.super_unique, t.entry_id, t.spec, t.count
              FROM kill t JOIN character c ON c.profile = t.profile;

            {AreaTimeTable("area_time_v3")}
            INSERT INTO area_time_v3 (character_id, difficulty, area, milliseconds)
            SELECT c.rowid, t.difficulty, t.area, t.milliseconds
              FROM area_time t JOIN character c ON c.profile = t.profile;

            {ContainerV3Table("container_v3")}
            INSERT INTO container_v3 (id, character_id, owner, name, label, stash_kind, stash_type, page, gold,
                                      width, height)
            SELECT t.id, c.rowid, t.owner, t.name, t.label, t.stash_kind, t.stash_type, t.page, t.gold,
                   t.width, t.height
              FROM container t JOIN character c ON c.profile = t.profile;

            DROP INDEX item_by_profile;
            ALTER TABLE item ADD COLUMN character_id INTEGER NOT NULL DEFAULT 0;
            UPDATE item SET character_id = (SELECT c.rowid FROM character c WHERE c.profile = item.profile);
            ALTER TABLE item DROP COLUMN profile;
            CREATE INDEX item_by_character ON item(character_id);

            DROP TABLE merc;         ALTER TABLE merc_v3 RENAME TO merc;
            DROP TABLE wearer_stat;  ALTER TABLE wearer_stat_v3 RENAME TO wearer_stat;
            DROP TABLE wearer_skill; ALTER TABLE wearer_skill_v3 RENAME TO wearer_skill;
            DROP TABLE progression;  ALTER TABLE progression_v3 RENAME TO progression;
            DROP TABLE kill;         ALTER TABLE kill_v3 RENAME TO kill;
            DROP TABLE area_time;    ALTER TABLE area_time_v3 RENAME TO area_time;
            DROP TABLE container;    ALTER TABLE container_v3 RENAME TO container;
            DROP TABLE character;    ALTER TABLE character_v3 RENAME TO character;
            """,
            "merc", "wearer_stat", "wearer_skill", "progression", "kill", "area_time", "container", "item",
            "statlist"),

        // 3 -> 4: three columns, each appended in place, so a fresh file states them in the same
        // trailing positions (see CharacterTable and `item`). Existing rows take the meaning the
        // older producer actually had: no backend named (it ran on 1.14d only), game type unknown
        // rather than Classic, and no stack — a version-3 manager never stored an advanced page's
        // slot counts, and the next stash re-send fills them. Nothing re-points a key, so there
        // is nothing for the foreign key check to verify.
        [3] = new(
            """
            ALTER TABLE character ADD COLUMN backend TEXT NOT NULL DEFAULT '';
            ALTER TABLE character ADD COLUMN game_type INTEGER;
            ALTER TABLE item ADD COLUMN stack_count INTEGER;
            """),

        // 4 -> 5: the player's last-used skill and the game the capture is of, appended in place.
        // last_used_skill is NULL on existing rows: no earlier producer reported it, and 0 would
        // claim Attack. The game columns take the defaults (LOD, Expansion, 1.14d) only until
        // CaptureStore.Open recomputes every row from what was reported, which it does on every
        // open — so the rule lives in GameEdition alone and is not restated here in SQL.
        [4] = new(
            """
            ALTER TABLE character ADD COLUMN last_used_skill INTEGER;
            ALTER TABLE character ADD COLUMN game_flavor INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE character ADD COLUMN game_version INTEGER NOT NULL DEFAULT 2;
            ALTER TABLE character ADD COLUMN game_variant INTEGER NOT NULL DEFAULT 0;
            """),

        // 5 -> 6: a stash page's remove-only flag and season, appended in place. Existing rows
        // take 0 / 0, which is what every earlier producer meant: none reported a remove-only
        // page (they listed old-season tabs as ordinary shared pages), and the next stash re-send
        // corrects any that were.
        [5] = new(
            """
            ALTER TABLE container ADD COLUMN remove_only INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE container ADD COLUMN season INTEGER NOT NULL DEFAULT 0;
            """),
    };

    /// <summary>
    /// The container table as version 2 wrote it, kept for the 1→2 step: a step must build the
    /// shape of ITS target version, not whatever the current one is, or a file two versions
    /// behind would skip the middle.
    /// </summary>
    private static string ContainerV2Table(string table) =>
        $"""
        CREATE TABLE {table} (
            id          INTEGER PRIMARY KEY,
            profile     TEXT    NOT NULL REFERENCES character(profile) ON DELETE CASCADE,
            owner       INTEGER NOT NULL,
            name        TEXT    NOT NULL,
            label       TEXT    NOT NULL DEFAULT '',
            stash_kind  INTEGER NOT NULL DEFAULT 0,
            stash_type  INTEGER NOT NULL DEFAULT 0,
            page        INTEGER NOT NULL DEFAULT 0,
            gold        INTEGER NOT NULL DEFAULT 0,
            width       INTEGER NOT NULL DEFAULT 0,
            height      INTEGER NOT NULL DEFAULT 0,
            UNIQUE (profile, owner, name, stash_kind, page)
        ) STRICT;
        """;

    // Every table that hangs off a character is a function of its name, for the same reason as
    // ContainerTable: a migration has to build the new shape beside the old one, and it must be
    // the shape a fresh file gets. The table is the one place the shape is written.

    /// <summary>
    /// One row per captured character: the player wearer's identity and live position. Keyed by
    /// the profile it was reported through AND its name (see CharacterKey in the proto), with a
    /// surrogate id for the children to hang off — a composite text key would have to be
    /// carried into every one of them.
    /// </summary>
    private static string CharacterTable(string table) =>
        $"""
        CREATE TABLE {table} (
            id               INTEGER PRIMARY KEY,
            profile          TEXT    NOT NULL,
            char_name        TEXT    NOT NULL DEFAULT '',  -- '' only until the first named report
            account          TEXT    NOT NULL DEFAULT '',
            realm            TEXT    NOT NULL DEFAULT '',
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
            area_entered_at  INTEGER,                     -- epoch ms; NULL until a real in-game entry
            -- Version 4's additions, last because ALTER TABLE ADD COLUMN appends them there.
            -- Which game build's data the ids index ("1.14d" / "d2r"); '' until a producer that
            -- names it reports, and the producers that don't ran on 1.14d only.
            backend          TEXT    NOT NULL DEFAULT '',
            -- 0 Classic, 1 Expansion, 2 RotW. NULL until reported: 0 is a real answer, and an
            -- older producer sends none.
            game_type        INTEGER,
            -- Version 5, appended the same way. D2R: the player's last-used skill id, which one
            -- requirement rule reads. NULL until reported, since 0 is a real skill (Attack).
            last_used_skill  INTEGER,
            -- Version 5 as well: the game the capture is of (GameEdition), stored so search can
            -- filter on it. DERIVED from backend, game_type and char_flags — rewritten with them on
            -- every snapshot and recomputed for every row on open, never written independently.
            game_flavor      INTEGER NOT NULL DEFAULT 0,  -- GameFlavor: 0 LOD, 1 D2R
            game_version     INTEGER NOT NULL DEFAULT 2,  -- 1 Classic, 2 Expansion, 3 RotW
            game_variant     INTEGER NOT NULL DEFAULT 0,  -- GameVariant: the tables its ids index
            UNIQUE (profile, char_name)
        ) STRICT;
        """;

    /// <summary>
    /// The character table as version 3 wrote it, kept for the 2→3 step for the same reason as
    /// <see cref="ContainerV2Table" />: built from the current shape, that step would already
    /// hold version 4's columns and the 3→4 step's ADD COLUMN would then fail on them.
    /// </summary>
    private static string CharacterV3Table(string table) =>
        $"""
        CREATE TABLE {table} (
            id               INTEGER PRIMARY KEY,
            profile          TEXT    NOT NULL,
            char_name        TEXT    NOT NULL DEFAULT '',
            account          TEXT    NOT NULL DEFAULT '',
            realm            TEXT    NOT NULL DEFAULT '',
            char_class       INTEGER NOT NULL DEFAULT 0,
            level            INTEGER NOT NULL DEFAULT 0,
            char_flags       INTEGER NOT NULL DEFAULT 0,
            flags_ex         INTEGER NOT NULL DEFAULT 0,
            ladder           INTEGER NOT NULL DEFAULT 0,
            difficulty       INTEGER,
            area             INTEGER NOT NULL DEFAULT 0,
            hand             INTEGER NOT NULL DEFAULT 0,
            game_id          TEXT    NOT NULL DEFAULT '',
            updated_at       INTEGER NOT NULL DEFAULT 0,
            area_entered_at  INTEGER,
            UNIQUE (profile, char_name)
        ) STRICT;
        """;

    /// <summary>
    /// The active mercenary. Deleted when a keyframe arrives without one — see the dismissal rule
    /// on CaptureStore.Apply, which is not something the engine can report directly.
    /// </summary>
    private static string MercTable(string table) =>
        $"""
        CREATE TABLE {table} (
            character_id  INTEGER PRIMARY KEY REFERENCES character(id) ON DELETE CASCADE,
            name          TEXT    NOT NULL DEFAULT '',
            class_id      INTEGER NOT NULL DEFAULT 0,
            flags_ex      INTEGER NOT NULL DEFAULT 0
        ) STRICT;
        """;

    /// <summary>
    /// Merged wearer stats: GetStat off FullStats, so they already carry gear contributions.
    /// Unlike item stats these are NOT raw — the engine sign-extends per itemstatcost.
    /// </summary>
    private static string WearerStatTable(string table) =>
        $"""
        CREATE TABLE {table} (
            character_id  INTEGER NOT NULL REFERENCES character(id) ON DELETE CASCADE,
            owner         INTEGER NOT NULL,
            stat_id       INTEGER NOT NULL,
            value         INTEGER NOT NULL,
            PRIMARY KEY (character_id, owner, stat_id)
        ) STRICT;
        """;

    private static string WearerSkillTable(string table) =>
        $"""
        CREATE TABLE {table} (
            character_id  INTEGER NOT NULL REFERENCES character(id) ON DELETE CASCADE,
            owner         INTEGER NOT NULL,
            skill_id      INTEGER NOT NULL,
            hard_points   INTEGER NOT NULL,
            level         INTEGER NOT NULL,  -- bonused; the gear contribution is level - hard_points
            PRIMARY KEY (character_id, owner, skill_id)
        ) STRICT;
        """;

    /// <summary>
    /// Completed quests / active waypoints, per difficulty. The engine reports only the
    /// difficulty it is currently in, so rows for the others persist from when they were last
    /// played — which is the point of keying by difficulty rather than replacing.
    /// </summary>
    private static string ProgressionTable(string table) =>
        $"""
        CREATE TABLE {table} (
            character_id  INTEGER NOT NULL REFERENCES character(id) ON DELETE CASCADE,
            difficulty    INTEGER NOT NULL,
            kind          INTEGER NOT NULL,  -- 0 quest, 1 waypoint
            entry_id      INTEGER NOT NULL,
            PRIMARY KEY (character_id, difficulty, kind, entry_id)
        ) STRICT;
        """;

    /// <summary>
    /// Lifetime kill counts, accumulated from the per-send deltas the engine reports. Regular
    /// monsters are keyed by (class id, SpecType rarity) and super-uniques by SuperUniques.txt
    /// index; super_unique keeps the two disjoint, so a super-unique is never also counted under
    /// its class.
    /// </summary>
    private static string KillTable(string table) =>
        $"""
        CREATE TABLE {table} (
            character_id  INTEGER NOT NULL REFERENCES character(id) ON DELETE CASCADE,
            difficulty    INTEGER NOT NULL,
            super_unique  INTEGER NOT NULL,
            entry_id      INTEGER NOT NULL,
            spec          INTEGER NOT NULL,
            count         INTEGER NOT NULL,
            PRIMARY KEY (character_id, difficulty, super_unique, entry_id, spec)
        ) STRICT;
        """;

    /// <summary>Lifetime milliseconds spent per area, accumulated from the gap between in-game updates.</summary>
    private static string AreaTimeTable(string table) =>
        $"""
        CREATE TABLE {table} (
            character_id  INTEGER NOT NULL REFERENCES character(id) ON DELETE CASCADE,
            difficulty    INTEGER NOT NULL,
            area          INTEGER NOT NULL,
            milliseconds  INTEGER NOT NULL,
            PRIMARY KEY (character_id, difficulty, area)
        ) STRICT;
        """;

    /// <summary>
    /// The item table's own indexes. Stated apart from the table because a migration that rebuilds
    /// or rewrites `item` loses them with it and has to put them back.
    /// </summary>
    private const string ItemIndexes = """
        CREATE INDEX item_by_container ON item(container_id);
        CREATE INDEX item_by_root      ON item(root_id);
        CREATE INDEX item_by_character ON item(character_id);
        -- Not optional: parent_id is a self-referencing FK with ON DELETE CASCADE, and SQLite
        -- enforces that by probing the child table once per deleted row. Unindexed it degrades
        -- to a full scan of `item` per row, on the hottest write path there is (every container
        -- replacement and every game change), under the store's single lock — measured at 1369ms
        -- vs 4ms over 40k rows, and it worsens quadratically.
        CREATE INDEX item_by_parent    ON item(parent_id);
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

    private static readonly string Ddl = $"""
        {CharacterTable("character")}

        {MercTable("merc")}

        {WearerStatTable("wearer_stat")}

        {WearerSkillTable("wearer_skill")}

        {ProgressionTable("progression")}

        {KillTable("kill")}

        {AreaTimeTable("area_time")}

        {ContainerTable("container")}

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
            damage_throw_max INTEGER,

            -- Denormalised from the container, so a search can filter by character before it
            -- joins anything. Without a REFERENCES clause, and after every original column,
            -- because that is the one shape ALTER TABLE ADD COLUMN can produce — the 2→3 migration
            -- adds it in place rather than rewriting the store's largest table — and a fresh file
            -- must match a migrated one. A character's deletion still reaches these rows: through
            -- container, which cascades.
            character_id     INTEGER NOT NULL DEFAULT 0,

            -- Version 4, appended in place after character_id for the same reason. The slot's
            -- stack size on a D2R advanced stash page; NULL on every other item, which is not a
            -- slot at all rather than an empty one.
            stack_count      INTEGER
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

        {ItemIndexes}
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
        """;

    /// <summary>
    /// Creates the schema in a new file, upgrades an older one in place, and reports whether the
    /// result is usable. A file from a NEWER build is the one case that is discarded rather than
    /// read: the caller recreates it, which is safe because captures are re-reported.
    /// </summary>
    /// <returns>False when the file is at a newer version and must be recreated.</returns>
    /// <exception cref="Exception">
    /// A migration step failed, or left a foreign key dangling. The transaction is rolled back
    /// first, so the file is exactly what it was; the caller treats this like any unreadable file
    /// — which is why an older file is backed up before anything is transformed.
    /// </exception>
    public static bool TryUpgrade(SqliteConnection connection, ILogger? logger = null)
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
        if (current > Version) return false;

        if (current == 0)
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, Ddl, transaction);
            Execute(connection, $"PRAGMA user_version = {Version}", transaction);
            transaction.Commit();
            return true;
        }

        BackUp(connection, current, logger);
        Migrate(connection);
        return true;
    }

    /// <summary>
    /// Copies the file to <c>&lt;name&gt;.v&lt;n&gt;.bak</c> before the first transformation — the same
    /// policy as <c>FileRepository</c>: written once, never overwritten (the migration re-runs
    /// after a failure), never read back, never auto-deleted. It exists because a failed upgrade
    /// sends the caller down the recreate path, which discards exactly the accumulated totals the
    /// upgrade was written to keep.
    ///
    /// <c>VACUUM INTO</c> rather than a file copy: the database is open in WAL mode, so a copy of
    /// the main file alone would miss whatever is still in the log, while VACUUM INTO writes one
    /// consistent snapshot. It refuses to overwrite, which is the write-once rule for free.
    ///
    /// A backup that cannot be written does NOT stop the upgrade. Going ahead without one is what
    /// every earlier version did; refusing would lose the file for certain to guard against
    /// losing it possibly.
    /// </summary>
    private static void BackUp(SqliteConnection connection, int from, ILogger? logger)
    {
        var backup = Path.ChangeExtension(connection.DataSource, $".v{from}.bak");
        if (File.Exists(backup)) return;

        try
        {
            Execute(connection, $"VACUUM INTO '{backup.Replace("'", "''")}'");
            logger?.LogInformation("Backed up captures at schema version {Version} to {Path} before upgrading",
                from, backup);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not back up captures to {Path} before upgrading; continuing without",
                backup);
            // A copy that failed part-way (disk full) is still a file, and the existence check
            // above would take it for a finished one on every later start. Remove it so the next
            // attempt writes a whole copy or nothing.
            try
            {
                File.Delete(backup);
            }
            catch (Exception deleteEx)
            {
                logger?.LogWarning(deleteEx, "Could not remove the partial backup {Path}", backup);
            }
        }
    }

    /// <summary>
    /// Runs every step from the file's version up to <see cref="Version" /> in one transaction,
    /// so the file is either fully upgraded or untouched.
    ///
    /// Foreign keys are switched off around it, not inside it: the pragma is a no-op within a
    /// transaction. Off is required rather than merely convenient — a step that drops and
    /// rebuilds a referenced table would otherwise cascade-delete every row that referenced it,
    /// which for `container` is every item in the database. The check at the end is what makes
    /// that safe to have done: a step that left a reference dangling fails the upgrade here,
    /// before the commit, rather than surfacing as a constraint error on some later write.
    /// </summary>
    private static void Migrate(SqliteConnection connection)
    {
        Execute(connection, "PRAGMA foreign_keys = OFF");
        try
        {
            // BEGIN IMMEDIATE: the write lock is taken here, not at the first write, so the
            // version is re-read under it. Two processes on one directory would otherwise both
            // read 1, and the second to run would re-apply the step to an already-upgraded table
            // — which for 1→2 succeeds, quietly zeroing the columns the first had filled.
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            var from = QueryUserVersion(connection, transaction);
            if (from >= Version) return;

            var checkTables = new SortedSet<string>(StringComparer.Ordinal);
            for (var version = from; version < Version; version++)
            {
                if (!Migrations.TryGetValue(version, out var step))
                    throw new InvalidOperationException($"No captures migration from schema version {version}");

                Execute(connection, step.Sql, transaction);
                checkTables.UnionWith(step.CheckTables);
            }

            foreach (var table in checkTables)
            {
                using var check = connection.CreateCommand();
                // The table name is one of this class's own literals, never input.
                check.CommandText = $"PRAGMA foreign_key_check({table})";
                check.Transaction = transaction;
                using var reader = check.ExecuteReader();
                if (reader.Read())
                {
                    // Column 1 is the child's rowid, NULL for a WITHOUT ROWID table.
                    var row = reader.IsDBNull(1) ? "?" : reader.GetInt64(1).ToString();
                    throw new InvalidOperationException(
                        $"Captures migration left a dangling reference in {table} (row {row})");
                }
            }

            Execute(connection, $"PRAGMA user_version = {Version}", transaction);
            transaction.Commit();
        }
        finally
        {
            Execute(connection, "PRAGMA foreign_keys = ON");
        }
    }

    private static int QueryUserVersion(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        command.Transaction = transaction;
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
