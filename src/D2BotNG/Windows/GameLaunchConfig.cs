using D2BotNG.Core.Protos;

namespace D2BotNG.Windows;

/// <summary>
/// Configuration for launching a game
/// </summary>
public class GameLaunchConfig
{
    public required string GamePath { get; init; }
    public required string D2BSPath { get; init; }
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
}
