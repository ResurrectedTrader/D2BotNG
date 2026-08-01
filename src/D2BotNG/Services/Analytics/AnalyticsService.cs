using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Engine;

namespace D2BotNG.Services.Analytics;

/// <summary>
/// Anonymous usage reporting to Aptabase. Sends an install-shape snapshot shortly after
/// startup and a small activity heartbeat while the manager runs.
///
/// Only counts, booleans and environment facts are sent — never a profile, account, proxy,
/// key or path. The install is identified by <see cref="InstallId"/>, a salted digest of
/// machine facts that d2bsng derives identically, so a machine's manager and its injected
/// DLLs land on the same install without either sending anything reversible.
///
/// Disabled unless an app key was baked in at build time, and switched off any time from
/// Settings. The opt-out is re-read per send, so toggling it stops or starts the manager's
/// own reporting without a restart; launched games take it as a command-line switch, so a
/// change reaches each of those the next time it starts.
/// </summary>
public class AnalyticsService : BackgroundService
{
    // Let startup settle before the first send so it doesn't pile onto migrations,
    // framework bootstrap and the initial mule scan.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
    // How often the loop wakes: the floor on retry spacing, and how quickly re-enabling
    // analytics mid-run is noticed.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    // Ceiling on the session_start backoff. It retries until it lands rather than giving up
    // after a few tries: the manager is commonly launched at login, where the first minutes
    // are exactly when a VPN or wifi association hasn't come up yet, and an install that
    // misses this event is one we know nothing about for the rest of the run.
    private static readonly TimeSpan MaxSessionBackoff = TimeSpan.FromHours(1);
    // The manager runs for days, so a startup-only event would say nothing about whether it
    // is actually botting. The heartbeat is what distinguishes "installed" from "in use".
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromHours(12);

    private const string EventPath = "/api/v0/event";

    private readonly ILogger<AnalyticsService> _logger;
    private readonly ProfileRepository _profileRepository;
    private readonly FrameworkRepository _frameworkRepository;
    private readonly ProxyRepository _proxyRepository;
    private readonly KeyListRepository _keyListRepository;
    private readonly ScheduleRepository _scheduleRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly ProfileEngine _profileEngine;

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _appVersion;
    private readonly string _buildVariant;
    private readonly DateTime _startedUtc = DateTime.UtcNow;

    private string _installId = "";
    private string _sessionId = "";

    public AnalyticsService(
        ILogger<AnalyticsService> logger,
        ProfileRepository profileRepository,
        FrameworkRepository frameworkRepository,
        ProxyRepository proxyRepository,
        KeyListRepository keyListRepository,
        ScheduleRepository scheduleRepository,
        SettingsRepository settingsRepository,
        ProfileEngine profileEngine)
    {
        _logger = logger;
        _profileRepository = profileRepository;
        _frameworkRepository = frameworkRepository;
        _proxyRepository = proxyRepository;
        _keyListRepository = keyListRepository;
        _scheduleRepository = scheduleRepository;
        _settingsRepository = settingsRepository;
        _profileEngine = profileEngine;

        _appVersion = UpdateManager.AppVersion;
        _buildVariant = Metadata(Assembly.GetExecutingAssembly(), "BuildVariant") ?? "standalone";
    }

    /// <summary>The user-facing opt-out, re-read per send so it applies immediately.</summary>
    private bool OptedOut => _settingsRepository.Current.AnalyticsDisabled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Baked in at build time only; an empty key means the build shipped without one, so
        // analytics is a no-op. There is no runtime key override.
        var appKey = AnalyticsBuild.AppKey;
        if (!AnalyticsBuild.IsConfigured)
        {
            _logger.LogDebug("Analytics disabled - no app key compiled in");
            return;
        }

        var host = ResolveHost(appKey);
        if (host == null)
        {
            _logger.LogWarning(
                "Analytics disabled - can't derive ingest host from the app key (set D2BOTNG_ANALYTICS_HOST)");
            return;
        }

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);

            _installId = InstallId.Derive();
            _sessionId = Guid.NewGuid().ToString("N");

            var sessionReported = false;
            var sessionBackoff = PollInterval;
            var nextSessionAttempt = DateTime.UtcNow;
            var lastHeartbeat = DateTime.UtcNow;

            // One polled loop rather than "send, then heartbeat forever": the opt-out is
            // re-read every tick, so enabling analytics mid-run starts reporting without a
            // restart, instead of losing the install snapshot for the whole run because the
            // setting happened to be off at startup.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!OptedOut)
                    {
                        if (!sessionReported && DateTime.UtcNow >= nextSessionAttempt)
                        {
                            sessionReported = await PostEventAsync(
                                host, appKey, "session_start", await BuildStartupPropsAsync(), stoppingToken);

                            if (!sessionReported)
                            {
                                nextSessionAttempt = DateTime.UtcNow + sessionBackoff;
                                sessionBackoff = sessionBackoff >= MaxSessionBackoff
                                    ? MaxSessionBackoff
                                    : sessionBackoff * 2;
                            }
                        }

                        if (DateTime.UtcNow - lastHeartbeat >= HeartbeatInterval)
                        {
                            lastHeartbeat = DateTime.UtcNow;
                            await PostEventAsync(
                                host, appKey, "heartbeat", await BuildHeartbeatPropsAsync(), stoppingToken);
                        }
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Telemetry must never take the manager down. The prop builders read five
                    // repositories, and BackgroundService's default StopHost would exit the
                    // app -- killing supervision of running games -- over an unreadable file.
                    _logger.LogDebug(ex, "Analytics: skipping this tick");
                }

                await Task.Delay(PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>
    /// What this install looks like: how much is configured, which optional features are on,
    /// and what it is running on.
    /// </summary>
    private async Task<JsonObject> BuildStartupPropsAsync()
    {
        var profiles = await _profileRepository.GetAllAsync();
        var frameworks = await _frameworkRepository.GetAllAsync();
        var proxies = await _proxyRepository.GetAllAsync();
        var keyLists = await _keyListRepository.GetAllAsync();
        var schedules = await _scheduleRepository.GetAllAsync();
        var settings = _settingsRepository.Current;

        var props = new JsonObject
        {
            ["profileCount"] = profiles.Count,
            ["frameworkCount"] = frameworks.Count,
            ["proxyCount"] = proxies.Count,
            ["keyListCount"] = keyLists.Count,
            ["keyCount"] = keyLists.Sum(k => k.Keys.Count),
            ["scheduleCount"] = schedules.Count,

            // How far each optional feature is actually adopted, which the counts above and
            // the boolean tags below can't answer: "12 profiles configured, 1 on a proxy" and
            // "12 profiles, all on proxies" are the same install by every other measure.
            // Counts rather than ratios -- profileCount is the denominator, in this same
            // event, and counts still mean something when summed across installs.
            ["profilesWithProxy"] = profiles.Count(p => !string.IsNullOrEmpty(p.Proxy)),
            ["profilesWithEnv"] = profiles.Count(p => p.Environment.Count > 0),
            ["profilesWithWebhooks"] = profiles.Count(p => p.DiscordWebhooks.Count > 0),
            ["profilesWithKeyList"] = profiles.Count(p => !string.IsNullOrEmpty(p.KeyList)),
            ["profilesScheduled"] = profiles.Count(p => !string.IsNullOrEmpty(p.Schedule)),

            ["frameworksWithEnv"] = frameworks.Count(f => f.Environment.Count > 0),
            ["frameworksWithCustomHealth"] = frameworks.Count(HasCustomHealthThresholds),
            ["frameworksWithCleanup"] =
                frameworks.Count(f => f.ScreenshotRetentionDays > 0 || f.CrashLogRetentionDays > 0),
            ["frameworksWithExtraDlls"] = frameworks.Count(f => f.DllPaths.Count > 1),

            // Lifetime counters, so an install that has botted for months is distinguishable
            // from one that was configured and abandoned.
            ["totalRuns"] = profiles.Sum(p => p.Runs),
            ["totalCrashes"] = profiles.Sum(p => p.Crashes),
            ["totalDeaths"] = profiles.Sum(p => p.Deaths),

            ["buildVariant"] = _buildVariant,
            ["dotnetVersion"] = Environment.Version.ToString(),
            ["cpuCores"] = Environment.ProcessorCount,
            ["isWine"] = IsWine(),

            // Which game patches to keep supporting.
            ["gameVersions"] = Join(frameworks
                .Select(f => f.GameVersionOrDefault())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)),
        };

        // Install-level switches only: anything with a per-entity dimension is a count above,
        // so nothing here restates one. One sorted comma-joined string because Aptabase props
        // are scalar, and this groups and "contains"-filters far better than a dozen booleans.
        var features = new List<string>();
        if (Environment.GetCommandLineArgs().Contains("--headless")) features.Add("headless");
        if (settings.Discord?.Enabled == true) features.Add("discord");
        if (settings.LegacyApi?.Enabled == true) features.Add("legacyApi");
        if (settings.Discord?.Webhooks.Count > 0) features.Add("globalWebhooks");
        if (frameworks.Count > 1) features.Add("multiFramework");

        features.Sort(StringComparer.Ordinal);
        props["features"] = Join(features);
        return props;
    }

    /// <summary>Whether any of the four optional per-framework watchdog thresholds was overridden.</summary>
    private static bool HasCustomHealthThresholds(Framework framework) =>
        framework.HasHeartbeatTimeoutSeconds
        || framework.HasMaxMissedHeartbeats
        || framework.HasMaxCrashRetries
        || framework.HasUnresponsiveTimeoutSeconds;

    /// <summary>How much of the install is actually running, and for how long.</summary>
    private async Task<JsonObject> BuildHeartbeatPropsAsync()
    {
        var profiles = await _profileRepository.GetAllAsync();
        var running = profiles.Count(p =>
            _profileEngine.GetInstance(p.Name)?.State is RunState.Running or RunState.Starting);

        return new JsonObject
        {
            ["profileCount"] = profiles.Count,
            ["profilesRunning"] = running,
            ["uptimeHours"] = Math.Round((DateTime.UtcNow - _startedUtc).TotalHours, 1),
        };
    }

    private async Task<bool> PostEventAsync(
        string host, string appKey, string eventName, JsonObject props, CancellationToken cancellationToken)
    {
        props["installId"] = _installId;

        var payload = new JsonObject
        {
            // Invariant, or the current culture's calendar formats this: a machine set to
            // th-TH would stamp the Buddhist year and the event would be rejected or filed
            // centuries out, visible only as an install that never reports.
            ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            ["sessionId"] = _sessionId,
            ["eventName"] = eventName,
            ["systemProps"] = new JsonObject
            {
                ["isDebug"] = IsDebugBuild(),
                ["osName"] = "Windows",
                ["osVersion"] = Environment.OSVersion.Version.ToString(3),
                ["locale"] = CultureInfo.CurrentUICulture.Name,
                ["appVersion"] = _appVersion,
                ["appBuildNumber"] = "",
                ["sdkVersion"] = $"d2botng@{_appVersion}",
            },
            ["props"] = props,
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, host + EventPath);
            request.Headers.Add("App-Key", appKey);
            request.Headers.Add("User-Agent", $"d2botng-analytics/{_appVersion}");
            request.Content = new StringContent(
                payload.ToJsonString(JsonSerializerOptions.Default), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Analytics: {Event} HTTP {Status}", eventName, (int)response.StatusCode);
                return false;
            }

            _logger.LogDebug("Analytics: {Event} reported", eventName);
            return true;
        }
        // Gated on the token, not the exception type: HttpClient reports its own Timeout as a
        // TaskCanceledException, which *is* an OperationCanceledException. Filtering by type
        // would let every network timeout escape and end the service for the whole run.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Analytics: {Event} request failed", eventName);
            return false;
        }
    }

    /// <summary>
    /// Aptabase app keys are "A-&lt;REGION&gt;-&lt;digits&gt;". A D2BOTNG_ANALYTICS_HOST override wins,
    /// which self-hosted ("A-SH-") and dev ("A-DEV-") keys need. Null when it can't be routed.
    /// </summary>
    private static string? ResolveHost(string appKey)
    {
        var hostOverride = Environment.GetEnvironmentVariable("D2BOTNG_ANALYTICS_HOST");
        if (!string.IsNullOrEmpty(hostOverride))
        {
            return hostOverride.TrimEnd('/');
        }

        var parts = appKey.Split('-');
        return parts.Length < 3
            ? null
            : parts[1] switch
            {
                "US" => "https://us.aptabase.com",
                "EU" => "https://eu.aptabase.com",
                _ => null,
            };
    }

    private static string? Metadata(Assembly assembly, string key) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

    /// <summary>Bucketed apart from released traffic rather than dropped.</summary>
    private bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        // 0.0.0 is the csproj default, i.e. a build that CI never stamped a version onto.
        return _appVersion == "0.0.0";
#endif
    }

    /// <summary>Wine exports wine_get_version from ntdll; absent on native Windows.</summary>
    private static bool IsWine()
    {
        var ntdll = GetModuleHandleW("ntdll.dll");
        return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
    }

    private static string Join(IEnumerable<string> items) => string.Join(',', items);

    public override void Dispose()
    {
        // Signal the token before dropping the client, so an in-flight send is usually
        // cancelled rather than faulted with ObjectDisposedException. Only usually: the base
        // cancels without awaiting the loop. Normal shutdown goes through StopAsync, which
        // does await it, so this ordering only narrows a window it can't close.
        base.Dispose();
        _httpClient.Dispose();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);
}
