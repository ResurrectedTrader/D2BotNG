using System.Diagnostics;
using D2BotNG.Core.Protos;

namespace D2BotNG.Windows;

/// <summary>
/// Dispatches a launch to the backend for the profile framework's game type
/// (<see cref="GameLaunchConfig.GameType"/>). Each backend owns its own pipeline;
/// see <see cref="IGameBackend"/>.
/// </summary>
public class GameLauncher
{
    private readonly IReadOnlyDictionary<GameType, IGameBackend> _backends;

    public GameLauncher(IEnumerable<IGameBackend> backends)
    {
        _backends = backends.ToDictionary(b => b.GameType);
    }

    public Task<Process> LaunchAsync(GameLaunchConfig config, CancellationToken cancellationToken = default)
    {
        if (!_backends.TryGetValue(config.GameType, out var backend))
        {
            throw new NotSupportedException(
                $"Launching '{config.GameType}' is not supported in this build. Choose another game type on the framework.");
        }

        return backend.LaunchAsync(config, cancellationToken);
    }
}
