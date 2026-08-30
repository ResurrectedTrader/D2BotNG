using D2BotNG.Core.Protos.Captures;
using D2BotNG.Data;
using ItemDamageKind = D2ItemToolkit.ItemDamageKind;
using MergedStatsOptions = D2ItemToolkit.MergedStatsOptions;
using TooltipEngine = D2ItemToolkit.TooltipEngine;
using Microsoft.Data.Sqlite;

namespace D2BotNG.Capture;

/// <summary>
/// SQLite-backed store of captured character state (data/ng/captures.db).
///
/// Reads and writes the same protobuf messages the engine's JSON parses into, so an item has
/// exactly one representation from the WM_COPYDATA payload through storage to the UI.
///
/// Deliberately has no in-memory mirror of the state: snapshots are section-partial, so
/// "current state" is by definition the accumulation of everything applied so far, and the
/// database is that accumulation. A cache would only be a second copy to keep in step.
///
/// One connection guarded by one lock. Writes arrive at roughly one per second per running
/// profile and are small (only the sections that changed); reads come from gRPC calls. Neither
/// justifies a pool, and a single connection makes the read-modify-write in
/// <see cref="Apply" /> trivially safe.
/// </summary>
public sealed partial class CaptureStore : IDisposable
{
    /// <summary>
    /// Cap on a single area-time tick. Larger gaps (profile paused, machine asleep, or just a
    /// long stretch between updates) are treated as "away" and not counted, so a stale clock
    /// cannot dump hours into whatever area the character was last standing in.
    /// </summary>
    private const long MaxAreaTickMs = 5 * 60 * 1000;

    /// <summary>ItemStatCost.txt row for character level — the engine sends no level field.</summary>
    private const int StatLevel = 12;

    // Roughly two seconds in total, which covers a predecessor letting go of the file during a
    // handoff without making a genuinely locked file cost a visible pause at startup.
    private const int DeleteAttempts = 10;
    private const int DeleteRetryMs = 200;

    // Default belt grid, used only when the engine sends no dimensions with the container.
    private const int DefaultBeltWidth = 4;
    private const int DefaultBeltHeight = 4;

    private readonly ILogger<CaptureStore> _logger;
    private readonly Paths _paths;
    private readonly TooltipEngine _tooltip;
    private readonly Lock _lock = new();

    private SqliteConnection? _connection;
    private string? _path;

    // Profiles that have reported since this store was opened. Purely to gate the time-in-area
    // accrual: without it the gap between two SESSIONS would be credited to whatever area the
    // character was last standing in. In memory rather than a column because it is erased on
    // open anyway, so persisting it could never carry information across a restart.
    private readonly HashSet<string> _reportedThisSession = [];

    // Statements whose text never varies, prepared once and rebound per use. Every CreateCommand
    // re-runs sqlite3_prepare_v2, and a 300-item keyframe issues on the order of twenty thousand
    // of these — the item insert alone binds 38 parameters — so preparation would otherwise
    // dominate an ingest in which the values are the only thing that actually changes.
    //
    // Only compile-time constant SQL belongs here: a search's text is unique per request and
    // Prune's placeholder count follows the reported key count, so caching either would grow the
    // dictionary without bound. The handles belong to the connection, so the two live and die
    // together — see Discard.
    private readonly Dictionary<string, SqliteCommand> _prepared = [];

    // The class-id-keyed half of an item's base facts. TooltipEngine.Embedded is an immutable
    // process-lifetime singleton, so the answer cannot change under us; without this every
    // re-report of an inventory re-resolves the same few hundred base items, four row lookups
    // and two string-keyed ones each.
    //
    // Null is a memoised "the tables describe no such base", so an unresolvable class id is
    // probed once rather than on every re-report of the item carrying it.
    private readonly Dictionary<int, BaseFacts?> _baseFacts = [];

    public CaptureStore(ILogger<CaptureStore> logger, Paths paths, TooltipEngine tooltip)
    {
        _logger = logger;
        _paths = paths;
        _tooltip = tooltip;
    }

    /// <summary>
    /// Opens (creating if needed) the database for the current base path. Safe to call again
    /// after a base-path change: the old connection is closed and the new location opened.
    /// </summary>
    public void Open()
    {
        lock (_lock)
        {
            // Nothing may escape: Open runs from StartAsync and from the SettingsChanged handler,
            // where a throw would abort startup or fail a settings save after the file was
            // already written. Captures are derived state; losing them must not cost anything else.
            try
            {
                var path = Path.Combine(_paths.DataDirectory, "captures.db");
                if (_connection != null && _path == path) return;

                Discard();
                _path = path;
                _reportedThisSession.Clear();

                try
                {
                    _connection = Connect(path);
                }
                catch (Exception ex)
                {
                    // A file we cannot read is derived state we can rebuild, so trade it for a
                    // working store rather than leaving captures broken until someone intervenes.
                    _logger.LogWarning(ex, "Could not open {Path}; recreating it", path);
                    TryRecreate(path);
                }
            }
            catch (Exception ex)
            {
                Discard();
                _logger.LogError(ex, "Captures are disabled: the store could not be opened");
            }
        }
    }

    /// <summary>
    /// Drops the connection and everything that hangs off it. The prepared statements hold handles
    /// on the connection, so they go first and they go every time it does — including the failure
    /// paths, where the next open would otherwise find commands bound to a dead connection.
    /// </summary>
    private void Discard()
    {
        ClearPrepared();
        _connection?.Dispose();
        _connection = null;
    }

    /// <summary>
    /// Finalises the cached statements alone. Separate from <see cref="Discard" /> because
    /// <see cref="Close" /> checkpoints before it drops the connection, and a TRUNCATE checkpoint
    /// is refused while any statement on that connection is still live.
    /// </summary>
    private void ClearPrepared()
    {
        foreach (var command in _prepared.Values) command.Dispose();
        _prepared.Clear();
    }

    /// <summary>
    /// The connection string, in one place so every open uses the same one.
    ///
    /// Pooling is OFF deliberately. Microsoft.Data.Sqlite pools by default, which keeps the file
    /// handle alive past <c>Dispose</c> — and every recreate path below then fails on Windows
    /// with "the process cannot access the file", disabling captures permanently instead of
    /// rebuilding. One long-lived connection behind one lock has nothing to gain from a pool.
    /// </summary>
    private static string ConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = false,
    }.ToString();

    /// <summary>
    /// Opens a connection and brings the schema up to date, rebuilding the file when it was
    /// written by a newer build (whose shape this one cannot read).
    /// </summary>
    private SqliteConnection Connect(string path)
    {
        var connection = new SqliteConnection(ConnectionString(path));
        try
        {
            connection.Open();
            if (CaptureSchema.TryUpgrade(connection)) return connection;
        }
        catch
        {
            // A corrupt file throws from the first PRAGMA rather than from Open, so without this
            // the handle would leak and block the recreate that the caller is about to attempt.
            connection.Dispose();
            throw;
        }

        connection.Dispose();
        // Either direction: a file from an older build is discarded as readily as one from a newer
        // build, because captures are derived state and re-reported within minutes.
        _logger.LogWarning("{Path} is at another schema version; recreating it", path);
        Delete(path);

        var fresh = new SqliteConnection(ConnectionString(path));
        try
        {
            fresh.Open();
            CaptureSchema.TryUpgrade(fresh);
            return fresh;
        }
        catch
        {
            // Same reason as above: the caller's response to a throw here is to delete the file and
            // start over, which a handle we left open would block.
            fresh.Dispose();
            throw;
        }
    }

    private void TryRecreate(string path)
    {
        try
        {
            Delete(path);
            _connection = Connect(path);
        }
        catch (Exception ex)
        {
            // Leave _connection null: every entry point no-ops, so captures are lost but the
            // rest of the manager is unaffected.
            Discard();
            _logger.LogError(ex, "Captures are disabled: {Path} could not be created", path);
        }
    }

    /// <summary>
    /// Removes the database and its sidecars, waiting briefly for whoever still has them open.
    ///
    /// The retry is what makes a schema bump survive an in-place update. The successor starts its
    /// hosted services while the PREDECESSOR is still quiescing, and the capture store is not part
    /// of the handoff — so for a second or two both processes have the file open, and Windows
    /// refuses a delete to a handle opened without FILE_SHARE_DELETE, which SQLite's VFS does not
    /// pass. Without this, the one moment the recreate path exists for is the one moment it fails,
    /// and captures stay disabled for the whole run of the new process.
    ///
    /// All three in one loop rather than one loop each, so the wait is bounded once rather than
    /// three times; deleting a file that is already gone is a no-op, so the earlier ones are free
    /// on a later attempt.
    /// </summary>
    private static void Delete(string path)
    {
        string[] files = [path, path + "-wal", path + "-shm"];
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                foreach (var file in files) File.Delete(file);
                return;
            }
            catch (Exception ex)
                when ((ex is IOException or UnauthorizedAccessException) && attempt < DeleteAttempts)
            {
                Thread.Sleep(DeleteRetryMs);
            }
        }
    }

    /// <summary>
    /// Applies one snapshot. Every section is optional — absent means "unchanged", so only what
    /// arrived is written — and the whole thing lands in a single transaction, so a reader never
    /// observes a character half-way through a game change.
    ///
    /// Hands back the resulting summary, read after the commit and still under the lock. The
    /// caller announces the change with it, which is both one lock acquisition instead of two and
    /// a stronger guarantee than a follow-up read could give: no other snapshot can land between
    /// the commit and the summary that is supposed to describe it.
    /// </summary>
    /// <param name="profile">Owning profile name, the key every row hangs off.</param>
    /// <param name="snapshot">The parsed payload; absent sections are left untouched.</param>
    /// <returns>The character's summary after this snapshot, or null when the store is disabled.</returns>
    public CharacterSummary? Apply(string profile, Snapshot snapshot)
    {
        lock (_lock)
        {
            if (_connection == null) return null;

            using var transaction = _connection.BeginTransaction();
            var previous = ReadCursor(profile, transaction) ?? InsertCharacter(profile, transaction);

            // Read here, recorded only after the commit below: a snapshot that throws rolls the
            // transaction back, so marking the profile as seen now would leave the flag claiming
            // an update landed when none did — and the next successful one would then accrue the
            // gap since the PREVIOUS session as time in area, which is exactly what this gates.
            var reportedBefore = _reportedThisSession.Contains(profile);

            var gameChanged = !string.IsNullOrEmpty(snapshot.GameId) && snapshot.GameId != previous.GameId;
            if (gameChanged)
            {
                // Item gids are only meaningful within one game, so nothing from the previous
                // one may survive into this one. Cascades through item/statlist/stat.
                //
                // The rebuild depends on this snapshot carrying the containers, which only a
                // keyframe does — and a game change does NOT guarantee one. The producer keeps
                // running across a manager restart (HandoffManager keeps games alive), advances
                // its game id over games it could not send, and resumes with unchanged
                // fingerprints: a plain update bearing a new game id. Gear is then empty until
                // the next game create, minutes for a bot. Still the right trade — serving items
                // whose gids belong to a dead game would be worse — but if that window ever
                // matters, the fix is a "resend everything" message, not keeping the rows.
                Execute("DELETE FROM container WHERE profile = $p", transaction, ("$p", profile));
            }

            var state = previous;
            if (snapshot.Identity != null) state = ApplyIdentity(profile, snapshot.Identity, state, transaction);
            if (snapshot.Player != null) state = ApplyPlayer(profile, snapshot.Player, state, transaction);

            // The mercenary, decided by the keyframe rather than by the payload's `merc: null`.
            // The producer emits merc on EVERY keyframe — the object, or null when there is none —
            // and proto3 JSON collapses that null to an unset field, indistinguishable from an
            // absent key (see MercPresenceTests). So: unset on a keyframe means there is no merc;
            // unset otherwise means unchanged. A merc that goes for good mid-game (a hardcore
            // death) therefore lingers until the next game, which for a bot is minutes — and in
            // exchange a merc that is simply unresolvable for one sample is not thrown away.
            if (snapshot.Merc != null) ApplyMerc(profile, snapshot.Merc, transaction);
            else if (snapshot.Keyframe) DismissMerc(profile, transaction);

            // Progression and kills are filed UNDER a difficulty, so they must run after identity
            // — and are dropped outright when no identity has ever named one. Guessing Normal
            // would be worse than losing them: a kill delta filed under the wrong difficulty is
            // added to a lifetime total and never self-corrects. This only bites on a character's
            // very first snapshot into a fresh database (a base-path change or a recreate while
            // bots are mid-game), and the next keyframe carries identity along with everything else.
            if (state.Difficulty is { } difficulty)
            {
                if (snapshot.Progression != null)
                    ApplyProgression(profile, difficulty, snapshot.Progression, transaction);

                if (snapshot.Kills != null) ApplyKills(profile, difficulty, snapshot.Kills, transaction);
            }
            else if (snapshot.Progression != null || snapshot.Kills != null)
            {
                _logger.LogDebug(
                    "Dropping progression/kills for {Profile}: no difficulty reported yet", profile);
            }

            var updatedAt = snapshot.UpdatedAt > 0
                ? snapshot.UpdatedAt
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Accrue the interval since the last in-game update into the area it was spent in.
            // Skipped on the first update of a session (the stored clock is stale), across a
            // game change (that gap is lobby and loading time), and for oversized gaps.
            // Also keyed by difficulty, so it waits for one too.
            if (reportedBefore && !gameChanged && previous.UpdatedAt > 0
                && previous.Difficulty is { } previousDifficulty)
            {
                var deltaMs = updatedAt - previous.UpdatedAt;
                if (deltaMs > 0 && deltaMs <= MaxAreaTickMs && previous.Area > 0)
                    AccrueAreaTime(profile, previousDifficulty, previous.Area, deltaMs, transaction);
            }

            // Re-stamp on a new game as well as a real area change, so a stale entry time from a
            // previous session is never carried forward.
            var areaEnteredAt = gameChanged || state.Area != previous.Area ? updatedAt : previous.AreaEnteredAt;

            Execute(
                """
                UPDATE character
                   SET game_id = $game, updated_at = $updated, area_entered_at = $entered
                 WHERE profile = $p
                """,
                transaction,
                ("$p", profile),
                ("$game", string.IsNullOrEmpty(snapshot.GameId) ? previous.GameId : snapshot.GameId),
                ("$updated", updatedAt),
                ("$entered", areaEnteredAt));

            transaction.Commit();
            _reportedThisSession.Add(profile);
            return ReadSummaries(profile).FirstOrDefault();
        }
    }

    public CharacterSummary? ResetKills(string profile) => DeleteFor("kill", profile);

    public CharacterSummary? ResetAreaTime(string profile) => DeleteFor("area_time", profile);

    /// <summary>
    /// Clears one accumulated table for a profile, and hands back the summary the same way
    /// <see cref="Apply" /> does — read under the same lock, so the caller can announce the change
    /// to every connected client rather than only the one that asked for it. Without that a second
    /// window keeps showing the totals it had, and for a STOPPED profile nothing ever arrives to
    /// correct it.
    /// </summary>
    private CharacterSummary? DeleteFor(string table, string profile)
    {
        lock (_lock)
        {
            if (_connection == null) return null;
            Execute($"DELETE FROM {table} WHERE profile = $p", null, ("$p", profile));
            return ReadSummaries(profile).FirstOrDefault();
        }
    }

    // -----------------------------------------------------------------------
    // Sections
    // -----------------------------------------------------------------------

    /// <summary>
    /// The mutable character facts the rest of <see cref="Apply" /> needs to reason about: what
    /// the row said before this snapshot, and what it says after each section is applied.
    /// </summary>
    private sealed record Cursor(
        string GameId, int Area, int? Difficulty, long UpdatedAt, long? AreaEnteredAt);

    private Cursor? ReadCursor(string profile, SqliteTransaction transaction)
    {
        using var command = Command(
            "SELECT game_id, area, difficulty, updated_at, area_entered_at FROM character WHERE profile = $p",
            transaction, ("$p", profile));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        return new Cursor(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4));
    }

    private Cursor InsertCharacter(string profile, SqliteTransaction transaction)
    {
        Execute("INSERT INTO character (profile) VALUES ($p)", transaction, ("$p", profile));
        return new Cursor("", 0, null, 0, null);
    }

    private Cursor ApplyIdentity(string profile, Identity identity, Cursor state,
        SqliteTransaction transaction)
    {
        Execute(
            """
            UPDATE character
               SET account = $account, realm = $realm, difficulty = $difficulty,
                   char_flags = $flags, ladder = $ladder
             WHERE profile = $p
            """,
            transaction,
            ("$p", profile),
            ("$account", identity.Account),
            ("$realm", identity.Realm),
            ("$difficulty", identity.Difficulty),
            ("$flags", (long)identity.CharFlags),
            ("$ladder", identity.Ladder ? 1 : 0));

        return state with { Difficulty = identity.Difficulty };
    }

    /// <summary>
    /// The player wearer, flattened onto the character row. Every part is independently
    /// fingerprinted by the engine, so the unit document, the merged stats and the containers can
    /// each be absent while the others are present. <see cref="ApplyMerc" /> is the same shape
    /// against the merc's own row.
    /// </summary>
    private Cursor ApplyPlayer(string profile, Unit wearer, Cursor state,
        SqliteTransaction transaction)
    {
        // The unit document rides one fingerprint on the engine side, so `name` being PRESENT
        // means the whole block is. Its absence must leave the stored values alone: class id 0 is
        // a real class (Amazon), so a defaulted field is indistinguishable from a reported one and
        // would silently rewrite the character. Testing presence rather than the value matters —
        // the producer can send an empty name while the client has not resolved one, and reading
        // that as "no document" would throw away a real area, hand, class and skill update.
        const int owner = CaptureSchema.OwnerPlayer;
        if (wearer.HasName)
        {
            Execute(
                """
                UPDATE character
                   SET char_name = $name, char_class = $class, flags_ex = $flagsEx,
                       area = $area, hand = $hand
                 WHERE profile = $p
                """,
                transaction,
                ("$p", profile),
                ("$name", wearer.Name),
                ("$class", wearer.ClassId),
                ("$flagsEx", (long)wearer.FlagsEx),
                ("$area", wearer.Area),
                ("$hand", wearer.Hand));

            ReplaceSkills(profile, owner, wearer.Skills, transaction);
            state = state with { Area = wearer.Area };
        }

        if (wearer.Stats.Count > 0)
        {
            ReplaceStats(profile, owner, wearer.Stats, transaction);

            var level = wearer.Stats.FirstOrDefault(s => s.Id == StatLevel);
            if (level != null)
            {
                Execute("UPDATE character SET level = $level WHERE profile = $p", transaction,
                    ("$p", profile), ("$level", level.Value));
            }
        }

        ReplaceContainers(profile, owner, wearer.Containers, transaction);
        return state;
    }

    /// <summary>The mercenary wearer. Presence rules exactly as <see cref="ApplyPlayer" />.</summary>
    private void ApplyMerc(string profile, Unit merc, SqliteTransaction transaction)
    {
        if (merc.HasName)
        {
            Execute(
                """
                INSERT INTO merc (profile, name, class_id, flags_ex) VALUES ($p, $name, $class, $flagsEx)
                ON CONFLICT(profile) DO UPDATE SET name = $name, class_id = $class, flags_ex = $flagsEx
                """,
                transaction,
                ("$p", profile),
                ("$name", merc.Name),
                ("$class", merc.ClassId),
                ("$flagsEx", (long)merc.FlagsEx));

            ReplaceSkills(profile, CaptureSchema.OwnerMerc, merc.Skills, transaction);
        }

        if (merc.Stats.Count > 0) ReplaceStats(profile, CaptureSchema.OwnerMerc, merc.Stats, transaction);
        ReplaceContainers(profile, CaptureSchema.OwnerMerc, merc.Containers, transaction);
    }

    private void DismissMerc(string profile, SqliteTransaction transaction)
    {
        Execute("DELETE FROM merc WHERE profile = $p", transaction, ("$p", profile));
        foreach (var table in new[] { "container", "wearer_stat", "wearer_skill" })
        {
            Execute($"DELETE FROM {table} WHERE profile = $p AND owner = $o", transaction,
                ("$p", profile), ("$o", CaptureSchema.OwnerMerc));
        }
    }

    /// <summary>
    /// A wearer's merged stats, UPSERTED rather than replaced.
    ///
    /// These are the highest-frequency write in the store — the producer re-sends them whenever
    /// experience or gold moves — and delete-then-insert is expensive out of proportion to what
    /// changes. The WAL's unit is the PAGE, so rewriting one stat costs the same as rewriting
    /// all 22; what delete-then-insert added on top was churn the values never needed: every
    /// index entry removed and re-added under an identical key, and a fresh rowid per row
    /// leaving the old one on the freelist. An upsert keeps both, so only the table page moves.
    ///
    /// The WHERE on the conflict clause takes it further: a stat whose value is unchanged writes
    /// nothing at all, so a snapshot where only experience moved dirties what experience touches
    /// and no more.
    /// </summary>
    private void ReplaceStats(string profile, int owner, IReadOnlyList<Stat> stats,
        SqliteTransaction transaction)
    {
        foreach (var stat in stats)
        {
            ExecutePrepared(
                """
                INSERT INTO wearer_stat (profile, owner, stat_id, value) VALUES ($p, $o, $id, $v)
                ON CONFLICT(profile, owner, stat_id) DO UPDATE SET value = $v WHERE value IS NOT $v
                """,
                transaction, ("$p", profile), ("$o", owner), ("$id", stat.Id), ("$v", stat.Value));
        }

        // The producer's stat list is a fixed curated set, so in practice this removes nothing —
        // but an upsert cannot retract, and without it a shrinking set would leave rows behind.
        Prune("wearer_stat", "stat_id", profile, owner, stats.Select(s => s.Id), transaction);
    }

    /// <summary>
    /// Skills, upserted for the same reasons as <see cref="ReplaceStats" /> — but here the prune
    /// is load-bearing rather than defensive. A skill list is not fixed: gear grants skills, so
    /// unequipping the item that granted one has to remove it, and an upsert alone never can.
    /// </summary>
    private void ReplaceSkills(string profile, int owner, IReadOnlyList<Skill> skills,
        SqliteTransaction transaction)
    {
        foreach (var skill in skills)
        {
            ExecutePrepared(
                """
                INSERT INTO wearer_skill (profile, owner, skill_id, hard_points, level)
                VALUES ($p, $o, $id, $hard, $level)
                ON CONFLICT(profile, owner, skill_id) DO UPDATE
                    SET hard_points = $hard, level = $level
                    WHERE hard_points IS NOT $hard OR level IS NOT $level
                """,
                transaction, ("$p", profile), ("$o", owner), ("$id", skill.SkillId),
                ("$hard", skill.HardPoints), ("$level", skill.Level));
        }

        Prune("wearer_skill", "skill_id", profile, owner, skills.Select(s => s.SkillId), transaction);
    }

    /// <summary>
    /// Drops rows for keys the wearer no longer reports, which is the half of "replace" an upsert
    /// cannot express. Costs nothing when it matches nothing: a DELETE that removes no row dirties
    /// no page, so on the common path this is a read.
    /// </summary>
    private void Prune(string table, string keyColumn, string profile, int owner,
        IEnumerable<int> reported, SqliteTransaction transaction)
    {
        var keys = reported.ToList();
        var parameters = new List<(string, object?)> { ("$p", profile), ("$o", owner) };
        parameters.AddRange(keys.Select((key, i) => ($"$k{i}", (object?)key)));

        // NOT IN () is a syntax error rather than "everything", so an empty report clears instead.
        var filter = keys.Count == 0
            ? ""
            : $" AND {keyColumn} NOT IN ({string.Join(", ", keys.Select((_, i) => $"$k{i}"))})";

        Execute($"DELETE FROM {table} WHERE profile = $p AND owner = $o{filter}", transaction,
            parameters.ToArray());
    }

    /// <summary>
    /// Progression is replaced for the difficulty in force and only that one. The engine reports
    /// whichever difficulty the character is currently in, so rows for the others must survive —
    /// that is what lets progression accumulate across all three over a character's life.
    /// </summary>
    private void ApplyProgression(string profile, int difficulty, Progression progression,
        SqliteTransaction transaction)
    {
        Execute("DELETE FROM progression WHERE profile = $p AND difficulty = $d", transaction,
            ("$p", profile), ("$d", difficulty));

        void Insert(int kind, IEnumerable<int> ids)
        {
            foreach (var id in ids)
            {
                ExecutePrepared(
                    """
                    INSERT OR IGNORE INTO progression (profile, difficulty, kind, entry_id)
                    VALUES ($p, $d, $k, $id)
                    """,
                    transaction, ("$p", profile), ("$d", difficulty), ("$k", kind), ("$id", id));
            }
        }

        Insert(CaptureSchema.ProgressionQuest, progression.Quests);
        Insert(CaptureSchema.ProgressionWaypoint, progression.Waypoints);
    }

    /// <summary>
    /// Kills arrive as the delta since the engine's last send — it clears its own tally on send —
    /// so they are added to the stored lifetime totals rather than replacing them.
    /// </summary>
    private void ApplyKills(string profile, int difficulty, Kills kills, SqliteTransaction transaction)
    {
        void Accumulate(bool superUnique, IEnumerable<Kill> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.Count <= 0) continue;
                ExecutePrepared(
                    """
                    INSERT INTO kill (profile, difficulty, super_unique, entry_id, spec, count)
                    VALUES ($p, $d, $su, $id, $spec, $count)
                    ON CONFLICT(profile, difficulty, super_unique, entry_id, spec)
                    DO UPDATE SET count = count + $count
                    """,
                    transaction, ("$p", profile), ("$d", difficulty), ("$su", superUnique ? 1 : 0),
                    ("$id", entry.Id), ("$spec", superUnique ? 0 : entry.Spec), ("$count", entry.Count));
            }
        }

        Accumulate(false, kills.ByClass);
        Accumulate(true, kills.BySuperUnique);
    }

    private void AccrueAreaTime(string profile, int difficulty, int area, long deltaMs,
        SqliteTransaction transaction)
    {
        Execute(
            """
            INSERT INTO area_time (profile, difficulty, area, milliseconds) VALUES ($p, $d, $a, $ms)
            ON CONFLICT(profile, difficulty, area) DO UPDATE SET milliseconds = milliseconds + $ms
            """,
            transaction, ("$p", profile), ("$d", difficulty), ("$a", area), ("$ms", deltaMs));
    }

    // -----------------------------------------------------------------------
    // Containers and items
    // -----------------------------------------------------------------------

    private void ReplaceContainers(string profile, int owner, Containers? containers,
        SqliteTransaction transaction)
    {
        if (containers == null) return;

        Replace(CaptureSchema.ContainerEquipped, containers.Equipped, false);
        Replace(CaptureSchema.ContainerInventory, containers.Inventory, false);
        Replace(CaptureSchema.ContainerCube, containers.Cube, false);
        // The belt is the one container whose x is a slot index rather than a grid column.
        Replace(CaptureSchema.ContainerBelt, containers.Belt, true);

        // The stash is a list of pages, not a container — one row each, sharing the storage shape
        // with the others because a page IS a grid of items even though its holder is not.
        if (containers.Stash != null)
        {
            Clear(CaptureSchema.ContainerStash);
            foreach (var page in containers.Stash.Pages)
            {
                Insert(CaptureSchema.ContainerStash, page.Name, page.Index, page.Width, page.Height,
                    page.Items, false);
            }
        }

        void Replace(string name, Container? container, bool slotIndexed)
        {
            if (container == null) return;
            Clear(name);

            // A slot-indexed container's items are decomposed onto a grid on the way in, so the
            // grid they were decomposed WITH is what gets stored. Serving the reported 0 x 0
            // alongside coordinates derived from 4 x 4 would leave a renderer with cells it
            // cannot lay out and no way to discover the dimensions they came from.
            var width = slotIndexed && container.Width <= 0 ? DefaultBeltWidth : container.Width;
            var height = slotIndexed && container.Height <= 0 ? DefaultBeltHeight : container.Height;
            Insert(name, "", 0, width, height, container.Items, slotIndexed);
        }

        // A container arrives only when its contents changed, and it arrives whole, so everything
        // under that name goes and is rebuilt. Cascades through item/statlist/stat.
        void Clear(string name) =>
            ExecutePrepared("DELETE FROM container WHERE profile = $p AND owner = $o AND name = $n",
                transaction, ("$p", profile), ("$o", owner), ("$n", name));

        void Insert(string name, string label, int page, int width, int height,
            IEnumerable<Unit> items, bool slotIndexed)
        {
            // OR REPLACE for the same reason as ReplaceStats, and here the hazard is closer: the
            // stash inserts one row per page under a UNIQUE(profile, owner, name, page), so two
            // pages arriving with the same index would otherwise abort the whole snapshot —
            // identity, kills and area time with it — and keep doing so on every later send.
            // The superseded row cascades its items away, which is the right outcome: last wins.
            var containerId = InsertReturningId(
                """
                INSERT OR REPLACE INTO container (profile, owner, name, label, page, width, height)
                VALUES ($p, $o, $n, $l, $pg, $w, $h)
                RETURNING id
                """,
                transaction, ("$p", profile), ("$o", owner), ("$n", name), ("$l", label),
                ("$pg", page), ("$w", width), ("$h", height));

            InsertItems(new ItemTarget(containerId, profile, transaction), items, width, height,
                slotIndexed);

            // Every top-level item in this container is its own root, settled in one statement
            // rather than one per item. Fillers already know their root and set it inline.
            ExecutePrepared("UPDATE item SET root_id = id WHERE container_id = $c AND parent_id IS NULL",
                transaction, ("$c", containerId));
        }
    }

    /// <summary>
    /// Where an item is being written. Invariant through the whole socket recursion, so it travels
    /// as one value rather than as three parameters re-threaded through every frame.
    /// </summary>
    private readonly record struct ItemTarget(
        long ContainerId, string Profile, SqliteTransaction Transaction);

    /// <summary>
    /// A socket filler's place in its host; absent for a top-level item, which has no parent, no
    /// socket ordinal and no host. One value rather than four parameters because it is one fact:
    /// "has a parent", "has a socket index" and "has a host" are the same question, and separate
    /// nullables invite code that answers it a different way in each place it asks.
    /// </summary>
    private readonly record struct SocketPlacement(
        long ParentId, long RootId, int Index, CapturedUnit Host);

    /// <summary>The class-id-derived half of <see cref="ResolveBaseFacts" />, which never changes.</summary>
    private readonly record struct BaseFacts(int Tier, int? Type0, int? Type1);

    /// <summary>
    /// Inserts a container page's items. <c>slotIndexed</c> marks a container that reports a
    /// linear slot index in x (y unused) rather than a grid cell — the belt. Slot 0 is the
    /// bottom-left in game, but a grid renders row 0 at the top, so the index is decomposed and
    /// flipped vertically on the way in.
    ///
    /// A slot-indexed call requires real dimensions; the caller substitutes the belt defaults so
    /// that the grid used here is also the one written to the container row.
    /// </summary>
    private void InsertItems(ItemTarget target, IEnumerable<Unit> items, int width, int height,
        bool slotIndexed)
    {
        foreach (var item in items)
        {
            var (x, y) = (item.X, item.Y);
            if (slotIndexed)
            {
                // Clamped because the point of decomposing at all is a cell a renderer can place:
                // a slot outside the reported grid decomposes to a negative row, which is no more
                // usable than the raw index. Only reachable if the grid and the slot disagree.
                var slot = Math.Clamp(item.X, 0, width * height - 1);
                (x, y) = (slot % width, height - 1 - slot / width);
            }

            InsertItem(target, item, null, x, y);
        }
    }

    /// <summary>
    /// Inserts one captured unit and, recursively, its socket fillers. Nothing is interpreted:
    /// the row is the capture, so any derivation belongs on the rendering side.
    /// </summary>
    private void InsertItem(ItemTarget target, Unit item, SocketPlacement? placement, int x, int y)
    {
        // Wrapped once and threaded onwards, into the socket recursion as the filler's host as
        // well. CapturedUnit memoises its projections precisely because the library walks them
        // repeatedly, so a second wrapper over the same item throws that memo away and re-wraps
        // every stat on every list — three times over for a socketed item.
        var unit = new CapturedUnit(item);

        // Top-level items only. Every column these fill is reachable through exactly one predicate
        // — SearchQueryBuilder's item alias, fixed to `parent_id IS NULL` — and the read path does
        // not select them at all, so resolving them for a filler would pay a requirements pass and
        // the merged-stats pass behind Damage to write six columns nothing can read.
        var facts = placement is null ? ResolveBaseFacts(item, unit) : default;

        // root_id is bound directly for a filler, which already knows its host's. A top-level item
        // is its own root and cannot know its id yet, so it goes in as 0 and the caller settles
        // every one of them with a single UPDATE per container.
        var id = InsertReturningIdPrepared(
            """
            INSERT INTO item (
                container_id, parent_id, root_id, socket_index, profile, gid, unit_type, class_id, code,
                quality, item_flags, format, file_index, item_level, rare_prefix, rare_suffix, auto_affix,
                magic_prefix_0, magic_prefix_1, magic_prefix_2,
                magic_suffix_0, magic_suffix_1, magic_suffix_2,
                ear_level, player_name, gfx_index, title,
                location, x, y, width, height,
                tier, type_0, type_1, req_level, req_str, req_dex,
                damage_1h_min, damage_1h_max, damage_2h_min, damage_2h_max,
                damage_throw_min, damage_throw_max)
            VALUES (
                $container, $parent, $root, $socket, $profile, $gid, $unitType, $classId, $code,
                $quality, $itemFlags, $format, $fileIndex, $itemLevel, $rarePrefix, $rareSuffix, $autoAffix,
                $prefix0, $prefix1, $prefix2, $suffix0, $suffix1, $suffix2,
                $earLevel, $playerName, $gfxIndex, $title,
                $location, $x, $y, $w, $h,
                $tier, $type0, $type1, $reqLevel, $reqStr, $reqDex,
                $d1hMin, $d1hMax, $d2hMin, $d2hMax, $dThrowMin, $dThrowMax)
            RETURNING id
            """,
            target.Transaction,
            ("$container", target.ContainerId),
            ("$parent", placement?.ParentId),
            ("$root", placement?.RootId ?? 0),
            ("$socket", placement?.Index),
            ("$profile", target.Profile),
            ("$gid", (long)item.Gid),
            ("$unitType", item.UnitType),
            ("$classId", item.ClassId),
            ("$code", item.Code),
            ("$quality", item.Quality),
            ("$itemFlags", (long)item.ItemFlags),
            ("$format", item.Format),
            ("$fileIndex", item.FileIndex),
            ("$itemLevel", item.ItemLevel),
            ("$rarePrefix", item.RarePrefix),
            ("$rareSuffix", item.RareSuffix),
            ("$autoAffix", item.AutoAffix),
            ("$prefix0", Slot(item.MagicPrefix, 0)),
            ("$prefix1", Slot(item.MagicPrefix, 1)),
            ("$prefix2", Slot(item.MagicPrefix, 2)),
            ("$suffix0", Slot(item.MagicSuffix, 0)),
            ("$suffix1", Slot(item.MagicSuffix, 1)),
            ("$suffix2", Slot(item.MagicSuffix, 2)),
            ("$earLevel", item.EarLevel),
            ("$playerName", item.PlayerName),
            ("$gfxIndex", item.GfxIndex),
            ("$title", item.Title),
            ("$location", item.Location),
            ("$x", x),
            ("$y", y),
            ("$w", item.Width),
            ("$h", item.Height),
            ("$tier", facts.Tier),
            ("$type0", facts.Type0),
            ("$type1", facts.Type1),
            ("$reqLevel", facts.ReqLevel),
            ("$reqStr", facts.ReqStr),
            ("$reqDex", facts.ReqDex),
            ("$d1hMin", facts.Damage.OneHandMin),
            ("$d1hMax", facts.Damage.OneHandMax),
            ("$d2hMin", facts.Damage.TwoHandMin),
            ("$d2hMax", facts.Damage.TwoHandMax),
            ("$dThrowMin", facts.Damage.ThrowMin),
            ("$dThrowMax", facts.Damage.ThrowMax));

        InsertStatLists(id, item, target.Transaction);

        for (var i = 0; i < item.Sockets.Count; i++)
        {
            // Fillers are contiguous from socket 0, so the array position is the socket index.
            var filler = item.Sockets[i];
            InsertItem(target, filler, new SocketPlacement(id, placement?.RootId ?? id, i, unit),
                filler.X, filler.Y);
        }

        // The merged view, for TOP-LEVEL items only. A filler has no totals of its own worth
        // indexing — a rune carries no stats at all, and what it grants is a property of the pair,
        // which is already folded into the host's row here.
        if (placement is { } socket)
            InsertSynthesisedFillerStats(id, item, unit, socket.Host, target.Transaction);
        else
            InsertMergedStats(id, unit, target.Transaction);
    }

    /// <summary>
    /// The damage lines an item draws, one pair per kind. All null for anything that draws none,
    /// which is everything that is not a weapon.
    /// </summary>
    private readonly record struct DamageLines(
        int? OneHandMin, int? OneHandMax,
        int? TwoHandMin, int? TwoHandMax,
        int? ThrowMin, int? ThrowMax);

    /// <summary>
    /// What the game's tables say about this base item. Resolved HERE, at ingest, because a search
    /// spans every profile at once and so has no single item to resolve against.
    ///
    /// One branch rather than six, so the columns are all known or all NULL together: an item the
    /// tables cannot describe stores nothing rather than a plausible-looking tier of Normal and a
    /// requirement of zero.
    /// </summary>
    private (int? Tier, int? Type0, int? Type1, int? ReqLevel, int? ReqStr, int? ReqDex,
        DamageLines Damage) ResolveBaseFacts(Unit item, CapturedUnit unit)
    {
        // The memo answers the unresolvable case as well, so a modded base costs one dictionary
        // lookup on every re-report rather than a table probe it will fail forever.
        if (!_baseFacts.TryGetValue(item.ClassId, out var basics))
        {
            basics = _tooltip.Items.TryResolve(item.ClassId, out _, out _)
                ? new BaseFacts(
                    (int)_tooltip.Items.Tier(item.ClassId),
                    _tooltip.Types.Row(_tooltip.Items.PrimaryTypeCode(item.ClassId)),
                    _tooltip.Types.Row(_tooltip.Items.SecondaryTypeCode(item.ClassId)))
                : null;
            _baseFacts[item.ClassId] = basics;
        }

        if (basics is not { } resolved) return (null, null, null, null, null, null, default);

        // Deliberately outside the memo: requirements read this item's OWN ethereal flag and stat
        // 91, so two items sharing a base can differ. No viewer either — the item belongs to no
        // character, it may sit on a mule of any class — so the level is the general requirement
        // rather than any one character's.
        var requirements = _tooltip.Requirements(unit);

        // Every line the tooltip would draw, kept apart by kind — a reader ranking on one of them
        // is asking about that line, and they are not comparable: a two-handed weapon has no
        // one-hand line at all. No viewer, for the same reason as the requirements above, which
        // also means no Barbarian dual-wield split: a stored column has to mean the same thing
        // wherever the item ends up.
        var damage = new DamageLines();
        foreach (var line in _tooltip.Damage(unit).Lines)
        {
            damage = line.Kind switch
            {
                ItemDamageKind.OneHand => damage with { OneHandMin = line.Min, OneHandMax = line.Max },
                ItemDamageKind.TwoHand => damage with { TwoHandMin = line.Min, TwoHandMax = line.Max },
                // A throwing potion's numbers come from missiles.txt rather than from stats, but the
                // game labels the line with the same string as a throw line and it is the only one
                // such an item draws, so it is the same row to rank on.
                _ => damage with { ThrowMin = line.Min, ThrowMax = line.Max },
            };
        }

        return (resolved.Tier, resolved.Type0, resolved.Type1,
            requirements.Level, requirements.Strength, requirements.Dexterity, damage);
    }

    /// <summary>The item's captured stat chain, stored verbatim: one row per list, one per stat.</summary>
    private void InsertStatLists(long itemId, Unit item, SqliteTransaction transaction)
    {
        for (var i = 0; i < item.StatsLists.Count; i++)
        {
            var list = item.StatsLists[i];
            var statListId = InsertStatList(itemId, i, list.StateNo, list.Flags, transaction);
            for (var j = 0; j < list.Stats.Count; j++)
            {
                var stat = list.Stats[j];
                InsertStat(statListId, j, stat.Id, stat.Value, stat.Layer, transaction);
            }
        }
    }

    /// <summary>
    /// One stat list header. <c>stateNo</c> and <c>flags</c> are the game's own provenance fields,
    /// stored uninterpreted — a list this store synthesised has neither, so it passes 0 for both.
    /// </summary>
    private long InsertStatList(long itemId, int ordinal, int stateNo, uint flags,
        SqliteTransaction transaction) =>
        InsertReturningIdPrepared(
            """
            INSERT INTO statlist (item_id, ordinal, state_no, flags)
            VALUES ($item, $ord, $state, $flags) RETURNING id
            """,
            transaction, ("$item", itemId), ("$ord", ordinal), ("$state", stateNo),
            ("$flags", (long)flags));

    /// <summary>
    /// One stat row. OR REPLACE because (statlist_id, ordinal) is the table's key and is unique by
    /// construction — the ordinal is the caller's loop index — but a constraint failure here would
    /// cost the whole snapshot, identity and kills with it, and insurance is one word.
    /// </summary>
    private void InsertStat(long statListId, int ordinal, int statId, long value, int layer,
        SqliteTransaction transaction) =>
        ExecutePrepared(
            """
            INSERT OR REPLACE INTO stat (statlist_id, ordinal, stat_id, value, layer)
            VALUES ($l, $ord, $id, $v, $layer)
            """,
            transaction, ("$l", statListId), ("$ord", ordinal), ("$id", statId), ("$v", value),
            ("$layer", layer));

    /// <summary>
    /// What a socket filler grants its host, written against the FILLER.
    ///
    /// <para>
    /// A rune or gem arrives from the producer with an empty stat chain: its mods live in gems.txt,
    /// keyed by the host's `gemapplytype`, and the game resolves them at display time. Without this
    /// the raw surface's whole socket axis was a no-op for them — `WITH_FILLERS` added nothing and
    /// `FILLERS_ONLY` could never match anything at all.
    /// </para>
    /// <para>
    /// Only for a filler that carries NO lists of its own. A jewel does, and `socketFillerStats`
    /// returns exactly those affixes for one — so writing them here as well would store the same
    /// stats twice against the same row and double every jewel bound on the raw surface.
    /// </para>
    /// <para>
    /// Stored at state 0 with no flags, because it carries none of the game's provenance: the list
    /// never existed on the item, so there is no captured `dwFlags` to store and inventing one
    /// would claim a source the capture does not have. State 0 keeps it clear of the set tiers
    /// (165-170), so the tier exclusion leaves it alone. The flags are what a scope has to reckon
    /// with: 0x40 is STATLIST_MAGIC, the bit the game sets on a unit's GRANTED mods, and a base
    /// array is marked by 0x80000000 rather than by 0x40's absence — so a `flags_all = 0x40` scope
    /// does not reach this list, and a `flags_none = 0x40` scope does. That it was synthesised is
    /// answerable regardless: a filler is exactly a row with a `parent_id`, which is how
    /// `FILLERS_ONLY` already finds one.
    /// </para>
    /// </summary>
    private void InsertSynthesisedFillerStats(long id, Unit filler, CapturedUnit unit,
        CapturedUnit host, SqliteTransaction transaction)
    {
        if (filler.StatsLists.Count > 0) return;

        var granted = _tooltip.SocketFillerStats(unit, host);
        if (granted.Count == 0) return;

        var statListId = InsertStatList(id, 0, 0, 0, transaction);
        for (var i = 0; i < granted.Count; i++)
        {
            var stat = granted[i];
            InsertStat(statListId, i, stat.StatId, stat.Value, stat.Layer, transaction);
        }
    }

    /// <summary>
    /// Writes what the item's stats add up to, from D2ItemToolkit's <c>MergedStats</c>, alongside
    /// the raw statlist rows rather than instead of them. Which question each of the two answers,
    /// and what is deliberately left out of the merged one, is on the `merged_stat` table in
    /// <see cref="CaptureSchema" />; the options below are that policy expressed.
    ///
    /// `value_host` is the same total without the socket fillers, which is what makes "30 of its
    /// own" a different search from "30 with a rune in it".
    /// </summary>
    private void InsertMergedStats(long id, CapturedUnit unit, SqliteTransaction transaction)
    {
        var merged = _tooltip.MergedStats(
            unit, new MergedStatsOptions { IncludeSockets = true, IncludeSetBonuses = false });

        // The host pass is only worth making when a filler could make the two differ: with nothing
        // inside the item the two views are the same computation by construction, and that is the
        // large majority of what an inventory holds.
        //
        // Asked of the ADAPTER rather than of item.Sockets, because the adapter's list is what the
        // toolkit actually walks. The two agree for every payload a real engine sends, and reading
        // the wrong one would make the shortcut's premise depend on the producer.
        //
        // Keyed for the lookup below rather than zipped: the two views do not agree on which stats
        // exist, let alone their order — a stat a filler alone grants is absent from the host view
        // entirely, which is exactly the NULL the column wants.
        Dictionary<(int, int), long>? hostValues = null;
        if (unit.Items.Count > 0)
        {
            var host = _tooltip.MergedStats(
                unit, new MergedStatsOptions { IncludeSockets = false, IncludeSetBonuses = false });

            hostValues = new Dictionary<(int, int), long>();
            foreach (var stat in host.Stats) hostValues[(stat.StatId, stat.Layer)] = stat.Value;
        }

        foreach (var stat in merged.Stats)
        {
            long? valueHost = null;
            if (hostValues is null) valueHost = stat.Value;
            else if (hostValues.TryGetValue((stat.StatId, stat.Layer), out var own)) valueHost = own;

            // OR REPLACE for the same reason InsertStat uses it: (item, stat, layer) is unique by
            // construction here, and a constraint failure would cost the whole snapshot.
            ExecutePrepared(
                """
                INSERT OR REPLACE INTO merged_stat (item_id, stat_id, layer, value, value_host)
                VALUES ($item, $id, $layer, $v, $vh)
                """,
                transaction, ("$item", id), ("$id", stat.StatId), ("$layer", stat.Layer),
                ("$v", stat.Value), ("$vh", valueHost));
        }

    }

    // -----------------------------------------------------------------------
    // Plumbing
    // -----------------------------------------------------------------------

    /// <summary>
    /// One affix slot, or 0 when the producer sent a shorter array than the game's three. The
    /// slot position carries meaning (a consumer indexes by it), so this pads rather than packs.
    /// </summary>
    private static int Slot(IReadOnlyList<int> affixes, int index) =>
        index < affixes.Count ? affixes[index] : 0;

    /// <summary>
    /// Runs an INSERT whose last clause is <c>RETURNING id</c> and yields that id. One statement
    /// where a separate <c>SELECT last_insert_rowid()</c> would be two, on the path that runs
    /// once per item, per stat list and per container.
    /// </summary>
    private long InsertReturningId(string sql, SqliteTransaction? transaction,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, transaction, parameters);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private SqliteCommand Command(string sql, SqliteTransaction? transaction,
        params (string Name, object? Value)[] parameters)
    {
        var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        return command;
    }

    private void Execute(string sql, SqliteTransaction? transaction, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, transaction, parameters);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// A statement from <see cref="_prepared" />, built on first use and rebound on every one
    /// after. Never disposed by the caller — the cache owns it until <see cref="Discard" />.
    ///
    /// The transaction is assigned per use, which is safe because a prepared statement belongs to
    /// the CONNECTION rather than to any transaction on it — SQLite has no per-transaction
    /// statement state, and Microsoft.Data.Sqlite only refuses to execute a command whose
    /// transaction is not the connection's current one. Reassigning does not re-prepare; only
    /// changing CommandText does, and this never does.
    /// </summary>
    private SqliteCommand Prepared(string sql, SqliteTransaction? transaction,
        (string Name, object? Value)[] parameters)
    {
        if (!_prepared.TryGetValue(sql, out var command))
        {
            command = Command(sql, transaction, parameters);
            _prepared[sql] = command;
            return command;
        }

        command.Transaction = transaction;
        if (command.Parameters.Count != parameters.Length)
        {
            throw new InvalidOperationException(
                $"Prepared statement takes {command.Parameters.Count} parameters, got {parameters.Length}");
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var (name, value) = parameters[i];
            // Rebound BY POSITION: the collection resolves a name by scanning, so binding the item
            // insert's 38 by name is quadratic and hands most of the saving straight back. The name
            // is still checked, once each, because a caller that reordered its tuples would
            // otherwise write every value into the wrong column and never say so.
            var parameter = command.Parameters[i];
            if (parameter.ParameterName != name)
            {
                throw new InvalidOperationException(
                    $"Prepared statement expects {parameter.ParameterName} at position {i}, got {name}");
            }

            parameter.Value = value ?? DBNull.Value;
        }

        return command;
    }

    private void ExecutePrepared(string sql, SqliteTransaction? transaction,
        params (string Name, object? Value)[] parameters) =>
        Prepared(sql, transaction, parameters).ExecuteNonQuery();

    /// <summary>
    /// <see cref="InsertReturningId" /> against a cached statement, for the inserts that run once
    /// per item and once per stat list.
    /// </summary>
    private long InsertReturningIdPrepared(string sql, SqliteTransaction? transaction,
        params (string Name, object? Value)[] parameters) =>
        Convert.ToInt64(Prepared(sql, transaction, parameters).ExecuteScalar());

    /// <summary>
    /// Folds the write-ahead log back into the database and closes it.
    ///
    /// Worth doing explicitly on shutdown rather than leaving to the process exit. SQLite only
    /// auto-checkpoints once the WAL passes ~1000 pages, and a session shorter than that ends
    /// with every row still in the WAL and captures.db holding nothing but a header. Nothing is
    /// lost — the next open recovers it — but the file is not self-contained in the meantime,
    /// so copying captures.db alone (a backup, a bug report) silently yields an empty database.
    /// </summary>
    public void Close()
    {
        lock (_lock)
        {
            if (_connection == null) return;

            ClearPrepared();
            try
            {
                // TRUNCATE rather than PASSIVE: it also removes the WAL rather than leaving it
                // at its high-water mark for the next run to inherit.
                Execute("PRAGMA wal_checkpoint(TRUNCATE)", null);
            }
            catch (Exception ex)
            {
                // A failed checkpoint costs a slower next open and nothing else, since the WAL
                // is recovered either way. It must never hold up shutdown.
                _logger.LogDebug(ex, "Could not checkpoint captures.db while closing");
            }

            Discard();
        }
    }

    public void Dispose() => Close();
}
