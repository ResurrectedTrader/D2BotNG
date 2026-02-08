using System.Text.Json.Serialization;
using D2BotNG.Core.Protos;

namespace D2BotNG.Data.LegacyModels;

/// <summary>
/// Represents the profile format used by the legacy D2Bot framework.
/// Use ToModern() to convert to the protobuf Profile type.
/// </summary>
public class LegacyProfile
{
    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("Group")]
    public string Group { get; init; } = "";

    [JsonPropertyName("Account")]
    public string Account { get; init; } = "";

    [JsonPropertyName("Password")]
    public string Password { get; init; } = "";

    [JsonPropertyName("Character")]
    public string Character { get; init; } = "";

    [JsonPropertyName("GameName")]
    public string GameName { get; init; } = "";

    [JsonPropertyName("GamePass")]
    public string GamePass { get; init; } = "";

    [JsonPropertyName("D2Path")]
    public string D2Path { get; init; } = "";

    [JsonPropertyName("Realm")]
    public string Realm { get; init; } = "";

    [JsonPropertyName("Mode")]
    public string Mode { get; init; } = "";

    [JsonPropertyName("Difficulty")]
    public string Difficulty { get; init; } = "";

    [JsonPropertyName("Parameters")]
    public string Parameters { get; init; } = "";

    [JsonPropertyName("Entry")]
    public string Entry { get; init; } = "";

    [JsonPropertyName("Location")]
    public string Location { get; init; } = "";

    [JsonPropertyName("KeyList")]
    public string KeyList { get; init; } = "";

    [JsonPropertyName("Schedule")]
    public string Schedule { get; init; } = "";

    [JsonPropertyName("Runs")]
    public int Runs { get; init; }

    [JsonPropertyName("Chickens")]
    public int Chickens { get; init; }

    [JsonPropertyName("Deaths")]
    public int Deaths { get; init; }

    [JsonPropertyName("Crashes")]
    public int Crashes { get; init; }

    [JsonPropertyName("Restarts")]
    public int Restarts { get; init; }

    [JsonPropertyName("RunsPerKey")]
    public int RunsPerKey { get; init; }

    [JsonPropertyName("KeyRuns")]
    public int KeyRuns { get; init; }

    [JsonPropertyName("InfoTag")]
    public string InfoTag { get; init; } = "";

    [JsonPropertyName("Visible")]
    public bool Visible { get; init; }

    [JsonPropertyName("SwitchKeys")]
    public bool SwitchKeys { get; init; }

    [JsonPropertyName("ScheduleEnable")]
    public bool ScheduleEnable { get; init; }

    [JsonPropertyName("Type")]
    public int Type { get; init; }

    public Profile ToModern()
    {
        return new Profile
        {
            Name = Name,
            Group = Group,
            Account = Account,
            Password = Password,
            Character = Character,
            GameName = GameName,
            GamePass = GamePass,
            D2Path = D2Path,
            Realm = ParseRealm(Realm),
            Mode = ParseGameMode(Mode),
            Difficulty = ParseDifficulty(Difficulty),
            Parameters = Parameters,
            EntryScript = Entry,
            WindowLocation = ParseWindowLocation(Location),
            KeyList = KeyList,
            Schedule = Schedule,
            Runs = (uint)Math.Max(0, Runs),
            Chickens = (uint)Math.Max(0, Chickens),
            Deaths = (uint)Math.Max(0, Deaths),
            Crashes = (uint)Math.Max(0, Crashes),
            Restarts = (uint)Math.Max(0, Restarts),
            KeyRuns = (uint)Math.Max(0, KeyRuns),
            RunsPerKey = (uint)Math.Max(0, RunsPerKey),
            InfoTag = InfoTag,
            Visible = Visible,
            SwitchKeysOnRestart = SwitchKeys,
            ScheduleEnabled = ScheduleEnable
        };
    }

    private static Realm ParseRealm(string realm) => realm.ToLowerInvariant() switch
    {
        "west" or "uswest" => Core.Protos.Realm.UsWest,
        "east" or "useast" => Core.Protos.Realm.UsEast,
        "europe" => Core.Protos.Realm.Europe,
        "asia" => Core.Protos.Realm.Asia,
        _ => Core.Protos.Realm.Unspecified
    };

    private static GameMode ParseGameMode(string mode) => mode.ToLowerInvariant() switch
    {
        "single player" => GameMode.SinglePlayer,
        "battle.net" => GameMode.BattleNet,
        "open battle.net" => GameMode.OpenBattleNet,
        "host tcp/ip game" => GameMode.TcpHost,
        "join tcp/ip game" => GameMode.TcpJoin,
        _ => GameMode.Unspecified
    };

    private static Difficulty ParseDifficulty(string difficulty) => difficulty.ToLowerInvariant() switch
    {
        "normal" => Core.Protos.Difficulty.Normal,
        "nightmare" => Core.Protos.Difficulty.Nightmare,
        "hell" => Core.Protos.Difficulty.Hell,
        "highest" => Core.Protos.Difficulty.Highest,
        _ => Core.Protos.Difficulty.Unspecified
    };

    private static WindowLocation? ParseWindowLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var parts = location.Split(',');
        if (parts.Length >= 2 && int.TryParse(parts[0].Trim(), out var x) && int.TryParse(parts[1].Trim(), out var y))
            return new WindowLocation { X = x, Y = y };
        return null;
    }
}
