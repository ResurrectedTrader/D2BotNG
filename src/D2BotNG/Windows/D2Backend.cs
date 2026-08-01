using System.Diagnostics;
using System.Text;
using D2BotNG.Core.Protos;
using D2BotNG.Data;

namespace D2BotNG.Windows;

/// <summary>
/// Launch backend for Diablo II (the 32-bit LoD client): delete cache, create the
/// process suspended, apply memory patches, overwrite the DACL, inject the
/// framework's DLL(s), resume, then wait for the game window. The 32-bit game is
/// injected cross-bitness from the x64 manager via <see cref="RemoteModule"/>.
/// </summary>
public class D2Backend : IGameBackend
{
    private readonly ILogger<D2Backend> _logger;
    private readonly ProcessManager _processManager;
    private readonly Patcher _patcher;
    private readonly PatchRepository _patchRepository;
    private readonly DaclOverwriter _daclOverwriter;

    public D2Backend(
        ILogger<D2Backend> logger,
        ProcessManager processManager,
        Patcher patcher,
        PatchRepository patchRepository,
        DaclOverwriter daclOverwriter)
    {
        _logger = logger;
        _processManager = processManager;
        _patcher = patcher;
        _patchRepository = patchRepository;
        _daclOverwriter = daclOverwriter;
    }

    public GameType GameType => GameType.D2;

    public async Task<Process> LaunchAsync(GameLaunchConfig config, CancellationToken cancellationToken = default)
    {
        var gameDir = Path.GetDirectoryName(config.GamePath)!;

        // Step 1: Delete cache files
        DeleteCacheFiles(gameDir);

        // Step 2: Build command line
        var args = BuildCommandLine(config);
        _logger.LogDebug("Launching game: {Path} {Args}", config.GamePath, args);

        // Step 3: Create process suspended
        var process = _processManager.CreateSuspended(config.GamePath, args, gameDir, config.Environment);

        if (process == null)
        {
            throw new InvalidOperationException("Failed to create suspended process");
        }

        var processId = process.Id;

        try
        {
            // Step 4: Re-acquire the Process by id for a fresh handle and enable exit events.
            process.Dispose();
            process = Process.GetProcessById(processId);
            process.EnableRaisingEvents = true;

            // Step 5: Apply patches
            if (!await ApplyPatchesAsync(process, gameDir, config.Visible, config.GameVersion))
            {
                throw new ApplicationException("Failed to apply patches");
            }

            // Step 6: Overwrite DACL to be able to inject.
            if (!_daclOverwriter.OverwriteDacl(process))
            {
                throw new ApplicationException("Failed to overwrite DACL");
            }

            // Step 7: Inject DLL(s), in order
            foreach (var dllPath in config.DllPaths)
            {
                if (!await _processManager.InjectDllAsync(process, dllPath))
                {
                    throw new ApplicationException($"Failed to inject {dllPath} into {processId}");
                }
            }

            // Step 8: Resume process
            _processManager.ResumeProcess(process);

            // Step 9: Wait for main window
            await WaitForMainWindowAsync(process, TimeSpan.FromSeconds(30), cancellationToken);

            // Step 10: Set window title
            var gameWindow = process.GameWindow;
            if (!string.IsNullOrEmpty(config.ProfileName) && gameWindow != 0)
            {
                _processManager.SetWindowTitle(gameWindow, config.ProfileName);
            }

            // Step 11: Set window position if configured
            if (config.WindowLocation != null && gameWindow != 0)
            {
                _processManager.MoveWindow(gameWindow, config.WindowLocation.X, config.WindowLocation.Y);
            }

            // Step 12: Handle visibility
            if (!config.Visible && gameWindow != 0)
            {
                _processManager.HideWindow(gameWindow);
            }

            return process;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during game launch, attempting to kill process {Pid}", processId);
            // Kill the process before disposing
            try { process.Kill(); } catch { /* ignore */ }
            process.Dispose();
            throw;
        }
    }

    private void DeleteCacheFiles(string gameDirectory)
    {
        try
        {
            var i = 0;
            foreach (var file in Directory.GetFiles(gameDirectory, "*.dat*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                    i++;
                }
                catch
                {
                    // Ignore individual file deletion failures
                }
            }
            if (i > 0)
                _logger.LogDebug("Deleted {Count} cache files from {Directory}", i, gameDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete cache files");
        }
    }

    private async Task<bool> ApplyPatchesAsync(Process process, string gameDir, bool visible, string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            _logger.LogWarning("No D2 version configured on the framework, skipping patches");
            return true;
        }

        var patches = await _patchRepository.GetPatchesForVersionAsync(version);

        _logger.LogDebug("Will apply {Length} patches", patches.Count);

        foreach (var patch in patches)
        {
            // Do not apply hidewin patches if the window is supposed to be visible.
            if (visible && patch.Name.StartsWith("hidewin"))
            {
                _logger.LogDebug("Skipping patch {Patch} as window configured to be visible", patch.Name);
                continue;
            }

            if (patch.Name.StartsWith("rdblock"))
            {
                _logger.LogDebug("Skipping patch {Patch} (no idea why, that is what D2Bot does)", patch.Name);
                continue;
            }

            var moduleName = PatchRepository.GetModuleName(patch.Module);
            var modulePath = Path.Combine(gameDir, moduleName);

            if (!await _patcher.ApplyPatchAsync(process, modulePath, patch))
            {
                _logger.LogWarning("Failed to apply patch {Patch}", patch.Name);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Build command line matching original D2Bot format, plus the switches this manager adds:
    /// [CD keys] [-proxy] -profile "{name}" -handle "{handle}" -cachefix -multi -title "{name}"
    /// [-noanalytics] {user_params}
    /// </summary>
    private static string BuildCommandLine(GameLaunchConfig config)
    {
        var sb = new StringBuilder();

        // 1. CD key parameters (if any)
        if (!string.IsNullOrEmpty(config.ClassicKey) && !string.IsNullOrEmpty(config.ExpansionKey))
        {
            sb.Append($"-d2c \"{config.ClassicKey}\" -d2x \"{config.ExpansionKey}\" ");
        }

        // 1b. SOCKS5 proxy (if configured) - consumed by the D2BS connect hook
        if (!string.IsNullOrEmpty(config.ProxyAddress))
        {
            sb.Append($"-proxy \"{config.ProxyAddress}\" ");
        }

        // 2. Profile name (unless user has -L flag for custom loader)
        var userParams = config.Parameters ?? "";
        if (!userParams.Contains("-L"))
        {
            sb.Append($"-profile \"{config.ProfileName}\" ");
        }

        // 3. System parameters
        if (!string.IsNullOrEmpty(config.Handle))
        {
            sb.Append($"-handle \"{config.Handle}\" ");
        }

        sb.Append("-cachefix -multi ");
        sb.Append($"-title \"{config.ProfileName}\" ");

        if (config.DisableAnalytics)
        {
            sb.Append("-noanalytics ");
        }

        // 5. User parameters (passed through as-is)
        if (!string.IsNullOrEmpty(userParams))
        {
            sb.Append(userParams);
        }

        return sb.ToString().TrimEnd();
    }

    private static async Task WaitForMainWindowAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
                throw new InvalidOperationException($"Game process exited with code {process.ExitCode}");

            process.Refresh();
            if (process.GameWindow != 0)
                return;

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException("Timed out waiting for game window");
    }
}
