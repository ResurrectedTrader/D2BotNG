using System.Text;
using D2BotNG.Core.Protos;
using D2BotNG.Data;

namespace D2BotNG.Services;

/// <summary>
/// Writes profile configurations to d2bs.ini file.
/// </summary>
public class IniWriter
{
    private readonly ILogger<IniWriter> _logger;
    private readonly Paths _paths;

    public IniWriter(
        ILogger<IniWriter> logger,
        Paths paths)
    {
        _logger = logger;
        _paths = paths;
    }

    /// <summary>
    /// Write all profiles to d2bs.ini.
    /// </summary>
    public async Task WriteAsync(List<Profile> profiles)
    {
        var iniPath = Path.Combine(_paths.D2BSDirectory, "d2bs.ini");
        if (!File.Exists(iniPath))
        {
            _logger.LogError("{iniPath} does not exist", iniPath);
            return;
        }

        const string marker = "; gateway=";
        var content = await File.ReadAllTextAsync(iniPath);
        var markerIndex = content.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            _logger.LogError("Could not find marker '{marker}' in {iniPath}", marker, iniPath);
            return;
        }

        content = content[..(markerIndex + marker.Length)] + Environment.NewLine + Environment.NewLine;

        var sb = new StringBuilder(content.Length + profiles.Count * 256);
        sb.Append(content);

        foreach (var profile in profiles)
        {
            WriteProfileSection(sb, profile);
        }

        try
        {
            await File.WriteAllTextAsync(iniPath, sb.ToString(), Encoding.Unicode);
            _logger.LogDebug("Wrote d2bs.ini with {Count} profiles", profiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write d2bs.ini");
        }
    }

    private static void WriteProfileSection(StringBuilder sb, Profile profile)
    {
        var difficulty = MapDifficulty(profile.Difficulty);
        var scriptPath = "kolbot"; // Default bot library folder name
        var entryScript = Path.GetFileName(profile.EntryScript);

        sb.AppendLine($"[{profile.Name}]");
        sb.AppendLine($"Mode={GameModeToString(profile.Mode)}");
        sb.AppendLine($"Username={profile.Account}");
        sb.AppendLine($"Password={profile.Password}");
        sb.AppendLine($"gateway={RealmToString(profile.Realm)}");
        sb.AppendLine($"character={profile.Character}");
        sb.AppendLine($"ScriptPath={scriptPath}");
        sb.AppendLine("DefaultGameScript=default.dbj");
        sb.AppendLine($"DefaultStarterScript={entryScript}");
        sb.AppendLine($"spdifficulty={difficulty}");
        sb.AppendLine();
    }

    private static string MapDifficulty(Difficulty difficulty)
    {
        return difficulty switch
        {
            Difficulty.Normal => "0",
            Difficulty.Nightmare => "1",
            Difficulty.Hell => "2",
            Difficulty.Highest => "3",
            _ => "0"
        };
    }

    private static string RealmToString(Realm realm) => realm switch
    {
        Realm.UsWest => "West",
        Realm.UsEast => "East",
        Realm.Europe => "Europe",
        Realm.Asia => "Asia",
        _ => ""
    };

    private static string GameModeToString(GameMode mode) => mode switch
    {
        GameMode.SinglePlayer => "Single Player",
        GameMode.BattleNet => "Battle.net",
        GameMode.OpenBattleNet => "Open Battle.net",
        GameMode.TcpHost => "Host TCP/IP Game",
        GameMode.TcpJoin => "Join TCP/IP Game",
        _ => ""
    };
}
