using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Serialization;
using D2BotNG.Core.Protos;
using D2BotNG.Engine.Handoff;
using Google.Protobuf.WellKnownTypes;
using JetBrains.Annotations;

namespace D2BotNG.Services;

/// <summary>
/// Manages checking for and downloading updates from GitHub releases.
/// Status updates are broadcast via EventBroadcaster.
/// </summary>
public class UpdateManager
{
    private readonly ILogger<UpdateManager> _logger;
    private readonly EventBroadcaster _eventBroadcaster;
    // Resolved lazily to break a DI cycle: HandoffManager pulls in
    // IEnumerable<IHandoffParticipant>, which (through DiscordService)
    // pulls UpdateManager back in. Constructing HandoffManager here would
    // loop. The resolution at use-time is fine — by then both singletons
    // are cached.
    private readonly IServiceProvider _serviceProvider;
    private readonly HttpClient _httpClient;
    /// <summary>
    /// Running app version ("0.0.0" for a local build CI never stamped). A build-time fact
    /// rather than update state, so it is reported on ServerInfo and read from here.
    /// </summary>
    public static string AppVersion { get; } = ResolveAppVersion();

    private static string ResolveAppVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }

    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly string _buildVariant;

    private readonly UpdateStatus _currentStatus; // Mutated in place.
    private readonly Lock _statusLock = new();
    // Serializes CheckForUpdateAsync. The 6h background tick and a manual
    // gRPC CheckForUpdate could otherwise interleave, both observe
    // UpdateAvailable==false at the start, both reach the notify step, and
    // double-fire UpdateBecameAvailable.
    private readonly SemaphoreSlim _checkLock = new(1, 1);

    /// <summary>
    /// Fires when an update transitions to being available — either the
    /// first time it's detected or whenever a newer version supersedes a
    /// previously-detected one. Payload is the latest version string.
    /// </summary>
    public event Func<string, Task>? UpdateBecameAvailable;

    public UpdateManager(
        ILogger<UpdateManager> logger,
        EventBroadcaster eventBroadcaster,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _eventBroadcaster = eventBroadcaster;
        _serviceProvider = serviceProvider;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "D2BotNG");

        _repoOwner = "ResurrectedTrader";
        _repoName = "D2BotNG";

        _buildVariant = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildVariant")?.Value ?? "standalone";

        _currentStatus = new UpdateStatus
        {
            LatestVersion = AppVersion,
            UpdateAvailable = false,
            State = UpdateState.Unknown
        };
    }

    public UpdateStatus GetStatus()
    {
        lock (_statusLock)
        {
            return _currentStatus.Clone();
        }
    }

    private void UpdateStatusAndBroadcast(Action<UpdateStatus> updateAction)
    {
        UpdateStatus newStatus;

        lock (_statusLock)
        {
            updateAction(_currentStatus);
            newStatus = _currentStatus.Clone();
        }

        _eventBroadcaster.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            UpdateStatus = newStatus
        });
    }

    public async Task CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (AppVersion == "0.0.0")
        {
            _logger.LogDebug("Skipping update check for non-release build (version 0.0.0)");
            return;
        }

        await _checkLock.WaitAsync(cancellationToken);
        try
        {
            await CheckForUpdateInnerAsync(cancellationToken);
        }
        finally
        {
            _checkLock.Release();
        }
    }

    private async Task CheckForUpdateInnerAsync(CancellationToken cancellationToken)
    {
        bool wasAvailable;
        string previousLatest;
        lock (_statusLock)
        {
            wasAvailable = _currentStatus.UpdateAvailable;
            previousLatest = _currentStatus.LatestVersion;
        }

        UpdateStatusAndBroadcast(s =>
        {
            s.State = UpdateState.Checking;
            s.ErrorMessage = "";
        });

        try
        {
            var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
            _logger.LogDebug("Checking for updates at {Url}", url);

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new Exception($"GitHub API returned {response.StatusCode}: {errorContent}");
            }

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: cancellationToken);

            if (release == null)
            {
                throw new Exception("Failed to parse GitHub release response");
            }

            var latestVersion = release.TagName.TrimStart('v');
            var updateAvailable = IsNewerVersion(latestVersion, AppVersion);

            // Find the asset matching our build variant (e.g., D2BotNG-standalone.exe)
            var expectedName = $"D2BotNG-{_buildVariant}.exe";
            var asset = release.Assets.FirstOrDefault(a =>
                a.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase));

            UpdateStatusAndBroadcast(s =>
            {
                s.LatestVersion = latestVersion;
                s.UpdateAvailable = updateAvailable;
                s.ReleaseNotes = release.Body ?? "";
                s.DownloadUrl = asset?.BrowserDownloadUrl ?? "";
                s.DownloadSize = asset?.Size ?? 0;
                s.State = updateAvailable ? UpdateState.UpdateAvailable : UpdateState.UpToDate;
            });

            if (!updateAvailable)
            {
                _logger.LogDebug("Update check complete. Current: {Current}, Latest: {Latest}, UpdateAvailable: {Available}",
                    AppVersion, latestVersion, updateAvailable);
            }
            else
            {
                _logger.LogInformation("New version {LatestVersion} available!", latestVersion);
                if (!wasAvailable || !string.Equals(previousLatest, latestVersion, StringComparison.Ordinal))
                {
                    await NotifyUpdateBecameAvailableAsync(latestVersion);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to check for updates");
            UpdateStatusAndBroadcast(s =>
            {
                s.State = UpdateState.Error;
                s.ErrorMessage = ex.Message;
            });
        }
    }

    public const string OldExeSuffix = ".old";

    /// <summary>
    /// Deletes the predecessor binary left behind by <see cref="StartUpdateAsync"/>.
    /// The previous version is renamed to <c>D2BotNG.exe.old</c> so that the new exe can
    /// take over the original path. In the takeover case the predecessor is still holding
    /// the file lock at the moment Main runs, so this fires a background task that waits
    /// for the predecessor PID to exit, then deletes — falling back to a short retry loop
    /// in case the OS hasn't released the lock the instant the process is gone. Failures
    /// are swallowed; the next startup will retry.
    /// </summary>
    public static void CleanupOldExeAfterUpdate(int? predecessorPid = null)
    {
        if (Environment.ProcessPath is not { } exePath) return;
        var oldPath = exePath + OldExeSuffix;
        if (!File.Exists(oldPath)) return;

        _ = WaitAndDeleteAsync(oldPath, predecessorPid);
    }

    private static async Task WaitAndDeleteAsync(string oldPath, int? predecessorPid)
    {
        if (predecessorPid is { } pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await p.WaitForExitAsync(cts.Token);
            }
            catch
            {
                // Predecessor already gone, never existed, or timed out — fall through.
            }
        }

        // Even after the process exits, the kernel can take a moment to release the
        // file lock. Short retry loop instead of bailing on the first failure.
        for (var i = 0; i < 10; i++)
        {
            try
            {
                File.Delete(oldPath);
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }
    }

    public async Task StartUpdateAsync(CancellationToken cancellationToken = default)
    {
        var status = GetStatus();

        if (!status.UpdateAvailable || string.IsNullOrEmpty(status.DownloadUrl))
        {
            UpdateStatusAndBroadcast(s =>
            {
                s.State = UpdateState.Error;
                s.ErrorMessage = "No update available or download URL not set";
            });
            return;
        }

        var currentExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve current exe path");
        var stagingPath = currentExe + ".new";
        var oldPath = currentExe + OldExeSuffix;

        UpdateStatusAndBroadcast(s =>
        {
            s.State = UpdateState.Downloading;
            s.DownloadProgress = 0;
            s.ErrorMessage = "";
        });

        try
        {
            _logger.LogInformation("Downloading update from {Url} to {Path}", status.DownloadUrl, stagingPath);

            // Download to a sidecar file in the install directory, with progress reporting.
            using (var response = await _httpClient.GetAsync(status.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? status.DownloadSize;
                var downloadedBytes = 0L;

                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(stagingPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progress = (int)((downloadedBytes * 100) / totalBytes);
                        UpdateStatusAndBroadcast(s => s.DownloadProgress = progress);
                    }
                }

                // Force to physical disk before the swap below: a crash in between could
                // otherwise leave a correctly-sized but zero-filled exe in place.
                fileStream.Flush(flushToDisk: true);
            }

            _logger.LogInformation("Download complete: {Path}", stagingPath);

            UpdateStatusAndBroadcast(s => s.State = UpdateState.ReadyToInstall);
            UpdateStatusAndBroadcast(s => s.State = UpdateState.Installing);

            // Windows lets us *rename* a running exe even though we can't overwrite
            // it. Move the running exe out of the way, then drop the freshly-
            // downloaded one into the original path so handoff can spawn it.
            if (File.Exists(oldPath)) File.Delete(oldPath);
            File.Move(currentExe, oldPath);
            File.Move(stagingPath, currentExe);

            _logger.LogInformation("Triggering handoff to newly-installed exe at {Path}", currentExe);
            var handoffManager = _serviceProvider.GetRequiredService<HandoffManager>();
            await handoffManager.TriggerHandoffAsync(currentExe, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download/apply update");
            UpdateStatusAndBroadcast(s =>
            {
                s.State = UpdateState.Error;
                s.ErrorMessage = ex.Message;
            });
        }
    }

    private async Task NotifyUpdateBecameAvailableAsync(string latestVersion)
    {
        var handler = UpdateBecameAvailable;
        if (handler == null) return;

        // Subscribers run sequentially; their exceptions are isolated so a
        // throwing listener doesn't prevent the rest from being notified. A
        // genuinely slow handler still blocks the next one — none of the
        // current subscribers do significant async work, so this is fine.
        foreach (var sub in handler.GetInvocationList().OfType<Func<string, Task>>())
        {
            try
            {
                await sub(latestVersion);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UpdateBecameAvailable subscriber threw");
            }
        }
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        if (!Version.TryParse(latest, out var latestVersion) ||
            !Version.TryParse(current, out var currentVersion))
        {
            return false;
        }

        return latestVersion > currentVersion;
    }

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = "";

        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = [];
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
