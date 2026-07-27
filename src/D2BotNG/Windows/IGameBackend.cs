using System.Diagnostics;
using D2BotNG.Core.Protos;

namespace D2BotNG.Windows;

/// <summary>
/// Owns the launch pipeline for one game type: building the command line, creating and
/// injecting the process, and returning it live. <see cref="GameLauncher"/> selects the
/// implementation by <see cref="GameType"/> from the profile's framework.
/// </summary>
public interface IGameBackend
{
    /// <summary>The game type this implementation launches.</summary>
    GameType GameType { get; }

    Task<Process> LaunchAsync(GameLaunchConfig config, CancellationToken cancellationToken = default);
}
