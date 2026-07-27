using D2BotNG.Core.Protos;

namespace D2BotNG.Windows;

/// <summary>
/// Configuration for launching a game
/// </summary>
public class GameLaunchConfig
{
    /// <summary>Which game this profile launches (from its framework); selects the backend.</summary>
    public required GameType GameType { get; init; }

    public required string GamePath { get; init; }

    /// <summary>DLLs to inject into the game, in order (resolved absolute paths).</summary>
    public required IReadOnlyList<string> DllPaths { get; init; }
    public required string ProfileName { get; init; }
    public string? Handle { get; init; }

    /// <summary>
    /// User-specified command line parameters (e.g., "-w -sleepy -ftj")
    /// These are passed through as-is, system params are appended automatically.
    /// </summary>
    public string? Parameters { get; init; }

    /// <summary>
    /// CD key in classic/expansion format
    /// </summary>
    public string? ClassicKey { get; init; }
    public string? ExpansionKey { get; init; }

    /// <summary>
    /// Window position, or null for default
    /// </summary>
    public WindowLocation? WindowLocation { get; init; }

    /// <summary>
    /// Whether to show the game window (default true)
    /// </summary>
    public bool Visible { get; init; } = true;

    /// <summary>
    /// SOCKS5 proxy passed through to the game as -proxy (e.g. socks5://user:pass@host:port).
    /// Null or empty means no -proxy argument is added.
    /// </summary>
    public string? ProxyAddress { get; init; }

    /// <summary>
    /// D2 version used to select which memory patches to apply (from the profile's framework).
    /// Null or empty skips patching.
    /// </summary>
    public string? GameVersion { get; init; }

    /// <summary>
    /// Extra environment variables for the game process (the framework's, overlaid by the
    /// profile's), merged over the manager's environment. Empty = inherit it unchanged.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}
