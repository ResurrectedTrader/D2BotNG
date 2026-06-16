using System.Text.Json.Serialization;
using D2BotNG.Legacy.Models;
using JetBrains.Annotations;

namespace D2BotNG.Services;

/// <summary>
/// Wire DTO for the "characterState" WM_COPYDATA message (see docs/plans character-state contract).
/// Sections are partial: only changed sections are present on the wire, except on a keyframe
/// (which carries the full state and a fresh gameId). Members are populated via JSON deserialization.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class CharacterStateDto
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; }
    [JsonPropertyName("gameId")] public string? GameId { get; init; }
    [JsonPropertyName("keyframe")] public bool Keyframe { get; init; }
    [JsonPropertyName("updatedAt")] public long UpdatedAt { get; init; } // Unix epoch ms (game-side assembly time)
    [JsonPropertyName("identity")] public IdentityDto? Identity { get; init; }
    [JsonPropertyName("stats")] public List<StatDto>? Stats { get; init; }
    [JsonPropertyName("containers")] public Dictionary<string, ContainerDto>? Containers { get; init; }
    [JsonPropertyName("progression")] public ProgressionDto? Progression { get; init; }

    // Active weapon set (0 = primary/I, 1 = secondary/II). Top-level because the
    // WeaponSwitch char flag is only valid in the lobby; this is live in-game.
    [JsonPropertyName("hand")] public int? Hand { get; init; }

    // charFlags and skills are top-level (moved out of progression): charFlags drives
    // hardcore/expansion; skills are the invested skill points. progression is now the
    // current difficulty's quests/waypoints only (the manager keys it by difficulty).
    [JsonPropertyName("charFlags")] public uint? CharFlags { get; init; }
    [JsonPropertyName("skills")] public List<SkillDto>? Skills { get; init; }

    // Per-game kill counts (the engine resets them each game); the manager accumulates
    // lifetime totals from the deltas, keyed by the current difficulty.
    [JsonPropertyName("kills")] public KillsDto? Kills { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class IdentityDto
{
    [JsonPropertyName("account")] public string? Account { get; init; }
    [JsonPropertyName("realm")] public string? Realm { get; init; }
    [JsonPropertyName("charName")] public string? CharName { get; init; }
    [JsonPropertyName("charClass")] public int CharClass { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("area")] public int Area { get; init; }
    [JsonPropertyName("difficulty")] public int Difficulty { get; init; }

    // hardcore/expansion are derived from Progression.charFlags; only ladder is sent here.
    [JsonPropertyName("ladder")] public bool Ladder { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class StatDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("value")] public long Value { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class ContainerDto
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("width")] public int Width { get; init; }
    [JsonPropertyName("height")] public int Height { get; init; }
    [JsonPropertyName("items")] public List<LegacyItem>? Items { get; init; }
    [JsonPropertyName("pages")] public List<StashPageDto>? Pages { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class StashPageDto
{
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("width")] public int Width { get; init; }
    [JsonPropertyName("height")] public int Height { get; init; }
    [JsonPropertyName("items")] public List<LegacyItem>? Items { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class ProgressionDto
{
    [JsonPropertyName("quests")] public List<int>? Quests { get; init; }       // completed quest ids (current difficulty)
    [JsonPropertyName("waypoints")] public List<int>? Waypoints { get; init; } // active waypoint ids (current difficulty)
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class SkillDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("hard")] public int Hard { get; init; }
    [JsonPropertyName("soft")] public int Soft { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class KillsDto
{
    [JsonPropertyName("byClass")] public List<KillCountDto>? ByClass { get; init; }
    [JsonPropertyName("bySuperUnique")] public List<KillCountDto>? BySuperUnique { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class KillCountDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("spec")] public int Spec { get; init; }  // SpecType rarity (byClass only; 0 for super-uniques)
    [JsonPropertyName("count")] public long Count { get; init; }
}
