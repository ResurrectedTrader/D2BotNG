using D2BotNG.Windows;
using static D2BotNG.Windows.NativeMethods;
using static D2BotNG.Windows.NativeTypes;

namespace D2BotNG.Engine;

/// <summary>
/// Background service that watches for and auto-dismisses game error dialogs
/// </summary>
public class ErrorDialogWatcher : BackgroundService
{
    private readonly ILogger<ErrorDialogWatcher> _logger;
    private readonly ProcessManager _processManager;

    // Exact error dialog title from D2Bot reference
    private const string ErrorDialogTitle = "Diablo II Error";

    public ErrorDialogWatcher(
        ILogger<ErrorDialogWatcher> logger,
        ProcessManager processManager)
    {
        _logger = logger;
        _processManager = processManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Error dialog watcher started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CheckForErrorDialogs();
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for error dialogs");
            }
        }

        _logger.LogInformation("Error dialog watcher stopped");
    }

    private void CheckForErrorDialogs()
    {
        foreach (var hwnd in _processManager.FindWindowsByTitle(ErrorDialogTitle))
        {
            DismissDialog(hwnd);
        }
    }

    private void DismissDialog(nint hwnd)
    {
        _logger.LogInformation("Dismissing error dialog: {Hwnd}", hwnd);
        SendMessageTimeoutW(hwnd, WM_SYSCOMMAND, SC_CLOSE, 0,
            SMTO_ABORTIFHUNG | SMTO_NORMAL, 1000, out _);
    }
}
