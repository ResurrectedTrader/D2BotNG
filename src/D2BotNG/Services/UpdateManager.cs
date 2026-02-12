using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Serialization;
using D2BotNG.Core.Protos;
using D2BotNG.Engine;
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
    private readonly ProfileEngine _profileEngine;
    private readonly EventBroadcaster _eventBroadcaster;
    private readonly HttpClient _httpClient;
    private readonly string _currentVersion;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly string _buildVariant;

    private readonly UpdateStatus _currentStatus; // Mutated in place.
    private readonly Lock _statusLock = new();

    public UpdateManager(
        ILogger<UpdateManager> logger,
        ProfileEngine profileEngine,
        EventBroadcaster eventBroadcaster)
    {
        _logger = logger;
        _profileEngine = profileEngine;
        _eventBroadcaster = eventBroadcaster;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "D2BotNG");

        // Get current version from assembly
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        _currentVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";

        _repoOwner = "ResurrectedTrader";
        _repoName = "D2BotNG";

        _buildVariant = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildVariant")?.Value ?? "standalone";

        _currentStatus = new UpdateStatus
        {
            CurrentVersion = _currentVersion,
            LatestVersion = _currentVersion,
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
        if (_currentVersion == "0.0.0")
        {
            _logger.LogDebug("Skipping update check for non-release build (version 0.0.0)");
            return;
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
            var updateAvailable = IsNewerVersion(latestVersion, _currentVersion);

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
                    _currentVersion, latestVersion, updateAvailable);
            }
            else
            {
                _logger.LogInformation("New version {LatestVersion} available!", latestVersion);
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

        UpdateStatusAndBroadcast(s =>
        {
            s.State = UpdateState.Downloading;
            s.DownloadProgress = 0;
            s.ErrorMessage = "";
        });

        try
        {
            // Create temp directory for update
            var tempDir = Path.Combine(Path.GetTempPath(), "D2BotNG.Update");
            Directory.CreateDirectory(tempDir);

            var fileName = Path.GetFileName(new Uri(status.DownloadUrl).LocalPath);
            var downloadPath = Path.Combine(tempDir, fileName);

            _logger.LogInformation("Downloading update from {Url} to {Path}", status.DownloadUrl, downloadPath);

            // Download with progress reporting
            using var response = await _httpClient.GetAsync(status.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? status.DownloadSize;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

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

            _logger.LogInformation("Download complete: {Path}", downloadPath);

            // Create update script that will:
            // 1. Wait for current process to exit
            // 2. Replace executable
            // 3. Start new version
            var currentExe = Environment.ProcessPath ?? AppContext.BaseDirectory;
            var scriptPath = Path.Combine(tempDir, "update.bat");

            var scriptContent = $@"@echo off
echo Waiting for D2BotNG to exit...
timeout /t 2 /nobreak >nul
:waitloop
tasklist /fi ""pid eq {Environment.ProcessId}"" | find ""{Environment.ProcessId}"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto waitloop
)
echo Applying update...
";

            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                scriptContent += $@"powershell -Command ""Expand-Archive -Path '{downloadPath}' -DestinationPath '{Path.GetDirectoryName(currentExe)}' -Force""
";
            }
            else
            {
                scriptContent += $@"copy /y ""{downloadPath}"" ""{currentExe}""
";
            }

            scriptContent += $@"echo Starting D2BotNG...
start """" ""{currentExe}""
echo Cleaning up...
rd /s /q ""{tempDir}"" 2>nul
";

            await File.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken);

            UpdateStatusAndBroadcast(s => s.State = UpdateState.ReadyToInstall);

            _logger.LogInformation("Update ready to install. Script created at {Path}", scriptPath);

            // Start the update script and exit
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            UpdateStatusAndBroadcast(s => s.State = UpdateState.Installing);

            // Stop profiles to prevent them being orphaned when the process dies.
            _logger.LogInformation("Stopping all profiles before update");
            await _profileEngine.StopAllAsync();

            Process.Start(startInfo);

            // Exit application after a short delay to allow event to be sent
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                Environment.Exit(0);
            });
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
