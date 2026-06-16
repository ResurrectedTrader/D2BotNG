using D2BotNG.Core.Protos;
using D2BotNG.Data;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace D2BotNG.Services;

/// <summary>
/// Holds the latest live character state per profile (merged from "characterState"
/// WM_COPYDATA sections), broadcasts updates to UI clients, and persists on a debounce
/// so characters remain viewable while profiles are stopped.
/// </summary>
public class CharacterStateService : IHostedService, IDisposable
{
    private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(5);

    // Cap a single area-time tick. Gaps larger than this (profile paused, machine asleep,
    // or just a long stretch between updates) are treated as "away" and not counted, so a
    // stale clock can't dump minutes/hours into whatever area the character was last in.
    private const long MaxAreaTickMs = 5 * 60 * 1000;

    // D2 character-flag bits (.d2s status byte) used to derive hardcore/expansion from charFlags.
    // NB: 0x04 = hardcore, 0x08 = "has died" (not hardcore), 0x20 = expansion.
    private const uint CharFlagHardcore = 0x04;
    private const uint CharFlagExpansion = 0x20;

    private readonly ILogger<CharacterStateService> _logger;
    private readonly EventBroadcaster _eventBroadcaster;
    private readonly CharacterRepository _repository;

    private readonly object _lock = new();
    private readonly Dictionary<string, Character> _characters = new();
    private bool _dirty;

    private readonly CancellationTokenSource _cts = new();
    private Task? _persistTask;

    public CharacterStateService(
        ILogger<CharacterStateService> logger,
        EventBroadcaster eventBroadcaster,
        CharacterRepository repository)
    {
        _logger = logger;
        _eventBroadcaster = eventBroadcaster;
        _repository = repository;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var persisted = await _repository.GetAllAsync();
            lock (_lock)
            {
                foreach (var c in persisted)
                {
                    // Clone: GetAllAsync hands back the repository's cached instances, so
                    // mutating them here would corrupt the repo's in-memory copy.
                    var character = c.Clone();
                    character.Online = false;
                    // Drop the persisted area-entry time so "time in area" can't count
                    // from a previous session until the char actually reports in-game.
                    character.AreaEnteredAt = null;
                    _characters[character.Profile] = character;
                }
            }

            _logger.LogInformation("Loaded {Count} persisted character(s)", persisted.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load persisted characters");
        }

        _persistTask = PersistLoopAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        if (_persistTask != null)
        {
            try { await _persistTask; }
            catch (OperationCanceledException) { }
        }

        await PersistIfDirtyAsync();
    }

    /// <summary>
    /// Merge a partial (or keyframe) snapshot into the profile's live character,
    /// then broadcast the merged result.
    /// </summary>
    public void Ingest(string profile, CharacterStateDto dto)
    {
        Character snapshot;
        lock (_lock)
        {
            if (!_characters.TryGetValue(profile, out var character))
            {
                character = new Character { Profile = profile };
                _characters[profile] = character;
            }

            var gameChanged = !string.IsNullOrEmpty(dto.GameId) && dto.GameId != character.GameId;
            if (!string.IsNullOrEmpty(dto.GameId))
                character.GameId = dto.GameId;

            // A new game carries full state — drop the previous game's containers. A keyframe
            // for the SAME game (e.g. a keyframe split across messages) is additive, so we
            // clear on the game transition only, not on the keyframe flag.
            if (gameChanged)
                character.Containers.Clear();

            var previousArea = character.Area;
            var previousDifficulty = character.Difficulty;
            var wasOnline = character.Online;
            var previousUpdatedAt = character.UpdatedAt;
            if (dto.Identity != null)
                ApplyIdentity(character, dto.Identity);

            if (dto.Stats != null)
            {
                character.Stats.Clear();
                foreach (var s in dto.Stats)
                    character.Stats.Add(new CharacterStat { Id = s.Id, Value = s.Value });
            }

            if (dto.Containers != null)
            {
                foreach (var (key, containerDto) in dto.Containers)
                    ApplyContainer(character, key, containerDto);
            }

            if (dto.CharFlags.HasValue)
                character.CharFlags = dto.CharFlags.Value;

            if (dto.Skills != null)
            {
                character.Skills.Clear();
                foreach (var s in dto.Skills)
                    character.Skills.Add(new SkillLevel
                    {
                        SkillId = s.Id,
                        HardPoints = s.Hard,
                        SoftPoints = s.Soft
                    });
            }

            // Progression is keyed by difficulty. An update carries only the current
            // difficulty's quests/waypoints; keep the other difficulties' entries so we
            // accumulate progression across all difficulties over time.
            if (dto.Progression != null)
            {
                var progression = new Progression();
                if (dto.Progression.Quests != null)
                    progression.Quests.AddRange(dto.Progression.Quests);
                if (dto.Progression.Waypoints != null)
                    progression.Waypoints.AddRange(dto.Progression.Waypoints);
                character.Progression ??= new DifficultyProgression();
                switch (character.Difficulty)
                {
                    case 0:
                        character.Progression.Normal = progression;
                        break;
                    case 1:
                        character.Progression.Nightmare = progression;
                        break;
                    case 2:
                        character.Progression.Hell = progression;
                        break;
                }
            }

            // Kills arrive as deltas (kills since the last update); add them to the
            // persisted lifetime totals, keyed by the current difficulty.
            if (dto.Kills != null)
                AccumulateKills(character, dto.Kills);

            // Derive hardcore/expansion from the (root) character flags.
            character.Mode ??= new EntityMode();
            character.Mode.Hardcore = (character.CharFlags & CharFlagHardcore) != 0;
            character.Mode.Expansion = (character.CharFlags & CharFlagExpansion) != 0;

            if (dto.Hand.HasValue)
                character.Hand = dto.Hand.Value;

            var updatedAt = dto.UpdatedAt > 0
                ? Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeMilliseconds(dto.UpdatedAt))
                : Timestamp.FromDateTime(DateTime.UtcNow);
            character.UpdatedAt = updatedAt;
            // Accumulate time spent in the area the character occupied over the interval since
            // the last in-game update. Skip the first update of a session (wasOnline guards the
            // stale persisted clock), game transitions (lobby/loading time), and oversized gaps.
            if (wasOnline && !gameChanged && previousUpdatedAt != null)
            {
                var deltaMs = (long)(updatedAt.ToDateTimeOffset() - previousUpdatedAt.ToDateTimeOffset())
                    .TotalMilliseconds;
                if (deltaMs > 0 && deltaMs <= MaxAreaTickMs)
                    AccumulateAreaTime(character, previousDifficulty, previousArea, deltaMs);
            }

            // Stamp when the player enters an area (for "time in area"). Also re-stamp on
            // a new game so a stale persisted entry time isn't reused; combined with the
            // load-time clear, the timestamp is only ever set from a real in-game entry.
            if (gameChanged || character.Area != previousArea)
                character.AreaEnteredAt = updatedAt;
            character.Online = true;
            _dirty = true;
            snapshot = character.Clone();
        }

        Broadcast(snapshot);
    }

    /// <summary>
    /// Clear all accumulated kill counts for a character, then broadcast and persist.
    /// </summary>
    public void ResetKills(string profile)
    {
        Character snapshot;
        lock (_lock)
        {
            if (!_characters.TryGetValue(profile, out var character))
                return;

            character.Kills = null;
            _dirty = true;
            snapshot = character.Clone();
        }

        Broadcast(snapshot);
    }

    /// <summary>
    /// Clear all accumulated time-in-area for a character, then broadcast and persist.
    /// </summary>
    public void ResetAreaTime(string profile)
    {
        Character snapshot;
        lock (_lock)
        {
            if (!_characters.TryGetValue(profile, out var character))
                return;

            character.AreaTime = null;
            _dirty = true;
            snapshot = character.Clone();
        }

        Broadcast(snapshot);
    }

    /// <summary>
    /// Snapshot of all known characters, for sending to a client on connect.
    /// </summary>
    public CharactersSnapshot GetSnapshot()
    {
        var snapshot = new CharactersSnapshot();
        lock (_lock)
        {
            foreach (var c in _characters.Values)
                snapshot.Characters.Add(c.Clone());
        }

        return snapshot;
    }

    private void Broadcast(Character character)
    {
        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            CharacterState = character
        });
    }

    private static void ApplyIdentity(Character character, IdentityDto identity)
    {
        if (identity.Account != null) character.Account = identity.Account;
        if (identity.Realm != null) character.Realm = identity.Realm;
        if (identity.CharName != null) character.CharName = identity.CharName;
        character.CharClass = identity.CharClass;
        character.Level = identity.Level;
        character.Area = identity.Area;
        character.Difficulty = identity.Difficulty;
        // hardcore/expansion are derived from charFlags (see Ingest); identity only carries ladder.
        character.Mode ??= new EntityMode();
        character.Mode.Ladder = identity.Ladder;
    }

    private static void ApplyContainer(Character character, string key, ContainerDto dto)
    {
        // Replace any existing container(s) for this key.
        for (var i = character.Containers.Count - 1; i >= 0; i--)
        {
            if (character.Containers[i].Id == key)
                character.Containers.RemoveAt(i);
        }

        if (dto.Pages != null)
        {
            // Multi-page container (stash): one Container per page.
            foreach (var page in dto.Pages)
            {
                var container = new Container
                {
                    Id = key,
                    Name = page.Name ?? "",
                    Width = page.Width,
                    Height = page.Height,
                    Page = page.Index
                };
                if (page.Items != null)
                {
                    foreach (var item in page.Items)
                    {
                        var modern = item.ToModern();
                        modern.Header = ""; // viewer omits the redundant per-item owner header
                        container.Items.Add(modern);
                    }
                }

                character.Containers.Add(container);
            }

            return;
        }

        var single = new Container
        {
            Id = key,
            Name = dto.Name ?? "",
            Width = dto.Width,
            Height = dto.Height
        };
        if (dto.Items != null)
        {
            foreach (var item in dto.Items)
            {
                var modern = item.ToModern();
                modern.Header = ""; // viewer omits the redundant per-item owner header
                if (key == "belt")
                {
                    // Belt reports a linear slot index in x (y unused). Slot 0 is the
                    // bottom-left in game, but the grid renders row 0 at the top, so
                    // decompose and flip vertically: x = i % w, y = (h-1) - i/w.
                    var w = single.Width > 0 ? single.Width : 4;
                    var h = single.Height > 0 ? single.Height : 4;
                    var index = modern.X;
                    modern.X = index % w;
                    modern.Y = h - 1 - index / w;
                }

                single.Items.Add(modern);
            }
        }

        character.Containers.Add(single);
    }

    /// <summary>
    /// Add a kill-delta update (kills since the last update) into the character's lifetime
    /// totals for the current difficulty: regular monsters by (class id, SpecType rarity),
    /// super-uniques by SuperUniques.txt index.
    /// </summary>
    private static void AccumulateKills(Character character, KillsDto kills)
    {
        character.Kills ??= new MonsterKills();
        var totals = KillsForDifficulty(character.Kills, character.Difficulty);

        if (kills.ByClass != null)
        {
            foreach (var e in kills.ByClass)
            {
                if (e.Count <= 0) continue;
                if (!totals.ByClass.TryGetValue(e.Id, out var cls))
                {
                    cls = new ClassKills();
                    totals.ByClass[e.Id] = cls;
                }

                cls.BySpec[e.Spec] =
                    (cls.BySpec.TryGetValue(e.Spec, out var cur) ? cur : 0L) + e.Count;
            }
        }

        if (kills.BySuperUnique != null)
        {
            foreach (var e in kills.BySuperUnique)
            {
                if (e.Count <= 0) continue;
                totals.BySuperUnique[e.Id] =
                    (totals.BySuperUnique.TryGetValue(e.Id, out var cur) ? cur : 0L) + e.Count;
            }
        }
    }

    private static DifficultyKills KillsForDifficulty(MonsterKills kills, int difficulty) => difficulty switch
    {
        1 => kills.Nightmare ??= new DifficultyKills(),
        2 => kills.Hell ??= new DifficultyKills(),
        _ => kills.Normal ??= new DifficultyKills(),
    };

    /// <summary>
    /// Add a time delta (ms) to the area the character spent it in, for the given difficulty.
    /// </summary>
    private static void AccumulateAreaTime(Character character, int difficulty, int area, long deltaMs)
    {
        if (area <= 0) return; // 0 = no/menu area; don't attribute time to it
        character.AreaTime ??= new AreaTime();
        var map = AreaTimeForDifficulty(character.AreaTime, difficulty);
        map[area] = (map.TryGetValue(area, out var cur) ? cur : 0L) + deltaMs;
    }

    private static MapField<int, long> AreaTimeForDifficulty(AreaTime areaTime, int difficulty) => difficulty switch
    {
        1 => areaTime.Nightmare,
        2 => areaTime.Hell,
        _ => areaTime.Normal,
    };

    private async Task PersistLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PersistInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await PersistIfDirtyAsync();
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task PersistIfDirtyAsync()
    {
        List<Character> snapshot;
        lock (_lock)
        {
            if (!_dirty) return;
            _dirty = false;
            snapshot = _characters.Values.Select(c => c.Clone()).ToList();
        }

        try
        {
            await _repository.ReplaceAllAsync(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist characters");
            lock (_lock) { _dirty = true; }
        }
    }

    public void Dispose()
    {
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
