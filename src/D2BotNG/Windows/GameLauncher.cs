using System.Diagnostics;
using System.Text;
using D2BotNG.Data;

namespace D2BotNG.Windows;

public class GameLauncher
{
    private readonly ILogger<GameLauncher> _logger;
    private readonly ProcessManager _processManager;
    private readonly Patcher _patcher;
    private readonly PatchRepository _patchRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly DaclOverwriter _daclOverwriter;

    public GameLauncher(
        ILogger<GameLauncher> logger,
        ProcessManager processManager,
        Patcher patcher,
        PatchRepository patchRepository,
        SettingsRepository settingsRepository,
        DaclOverwriter daclOverwriter)
    {
        _logger = logger;
        _processManager = processManager;
        _patcher = patcher;
        _patchRepository = patchRepository;
        _settingsRepository = settingsRepository;
        _daclOverwriter = daclOverwriter;
    }

    public async Task<Process> LaunchAsync(GameLaunchConfig config, CancellationToken cancellationToken = default)
    {
        var gameDir = Path.GetDirectoryName(config.GamePath)!;

        // Step 1: Delete cache files
        DeleteCacheFiles(gameDir);

        // Step 2: Build command line
        var args = BuildCommandLine(config);
        _logger.LogDebug("Launching game: {Path} {Args}", config.GamePath, args);

        // Step 3: Create process suspended
        var process = _processManager.CreateSuspended(config.GamePath, args, gameDir);

        if (process == null)
        {
            throw new InvalidOperationException("Failed to create suspended process");
        }

        var processId = process.Id;

        try
        {
            // Step 4: Get fresh Process object after DACL overwrite (we now have access)
            process.Dispose();
            process = Process.GetProcessById(processId);

            // Step 5: Apply patches
            await ApplyPatchesAsync(process);

            // Step 6: Resume process
            _processManager.ResumeProcess(process);

            // Step 7: Wait for process to initialize (ready for input)
            if (!process.WaitForInputIdle(30000)) // 30 second timeout
            {
                throw new TimeoutException("Timed out waiting for window to be ready for input");
            }

            // Step 8: Overwrite DACL to be able to inject.
            if (!_daclOverwriter.OverwriteDacl(process))
            {
                throw new ApplicationException("Failed to overwrite DACL");
            }

            // Step 9: Wait for main window
            await WaitForMainWindowAsync(process, TimeSpan.FromSeconds(30), cancellationToken);

            // Step 10: Inject D2BS.dll
            if (!string.IsNullOrEmpty(config.D2BSPath))
            {
                if (!_processManager.InjectDll(process, config.D2BSPath))
                {
                    throw new ApplicationException($"Failed to inject {config.D2BSPath} into {processId}");
                }
            }

            // Step 11: Set window title
            if (!string.IsNullOrEmpty(config.ProfileName) && process.MainWindowHandle != 0)
            {
                _processManager.SetWindowTitle(process.MainWindowHandle, config.ProfileName);
            }

            // Step 12: Set window position if configured
            if (config.WindowLocation != null && process.MainWindowHandle != 0)
            {
                _processManager.MoveWindow(process.MainWindowHandle, config.WindowLocation.X, config.WindowLocation.Y);
            }

            // Step 13: Handle visibility
            if (!config.Visible && process.MainWindowHandle != 0)
            {
                _processManager.HideWindow(process.MainWindowHandle);
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
            foreach (var file in Directory.GetFiles(gameDirectory, "*.dat*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Ignore individual file deletion failures
                }
            }
            _logger.LogDebug("Deleted cache files from {Directory}", gameDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete cache files");
        }
    }

    private async Task ApplyPatchesAsync(Process process)
    {
        var settings = await _settingsRepository.GetAsync();
        var version = settings.Game?.GameVersion;

        if (string.IsNullOrEmpty(version))
        {
            _logger.LogDebug("No D2 version configured, skipping patches");
            return;
        }

        var patches = _patchRepository.GetPatchesForVersion(version);

        foreach (var patch in patches)
        {
            var moduleName = PatchRepository.GetModuleName(patch.Module);
            var bytes = patch.Data.ToByteArray();
            _patcher.ApplyPatch(process, moduleName, patch.Offset, bytes);
        }
    }

    /// <summary>
    /// Build command line matching original D2Bot format:
    /// [CD Keys] -profile "{name}" -handle "{handle}" -cachefix -multi -title "{name}" {user_params}
    /// </summary>
    private static string BuildCommandLine(GameLaunchConfig config)
    {
        var sb = new StringBuilder();

        // 1. CD key parameters (if any)
        if (!string.IsNullOrEmpty(config.ClassicKey) && !string.IsNullOrEmpty(config.ExpansionKey))
        {
            sb.Append($"-d2c \"{config.ClassicKey}\" -d2x \"{config.ExpansionKey}\" ");
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
            if (process.MainWindowHandle != 0)
                return;

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException("Timed out waiting for game window");
    }
}
