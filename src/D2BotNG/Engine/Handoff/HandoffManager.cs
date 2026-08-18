using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using D2BotNG.Data;
using D2BotNG.Windows;

namespace D2BotNG.Engine.Handoff;

/// <summary>
/// Orchestrates in-place process handoff: spawns a successor instance of D2BotNG,
/// transfers the job object (so child games survive the restart), and ferries
/// non-persisted in-memory state via a JSON manifest. Stateful services contribute
/// their payloads by implementing <see cref="IHandoffParticipant"/>.
/// </summary>
public class HandoffManager
{
    public const string TakeoverFlag = "--takeover";

    private readonly ILogger<HandoffManager> _logger;
    private readonly ProfileEngine _profileEngine;
    private readonly ProcessManager _processManager;
    private readonly IEnumerable<IHandoffParticipant> _participants;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly DataWriteGate _writeGate;

    public HandoffManager(
        ILogger<HandoffManager> logger,
        ProfileEngine profileEngine,
        ProcessManager processManager,
        IEnumerable<IHandoffParticipant> participants,
        IHostApplicationLifetime appLifetime,
        DataWriteGate writeGate)
    {
        _logger = logger;
        _profileEngine = profileEngine;
        _processManager = processManager;
        _participants = participants;
        _appLifetime = appLifetime;
        _writeGate = writeGate;
    }

    /// <summary>
    /// Spawns a successor with the manifest path, waits for it to signal that it has
    /// adopted the job handle, then quiesces local engine state and asks the host to
    /// stop so the successor can bind the server port. If the successor crashes or
    /// times out before signalling, this method returns without shutting down — games
    /// stay managed.
    /// </summary>
    /// <param name="successorExePath">
    /// Optional path to the exe that should be spawned as the successor. Defaults
    /// to the current process's own exe — used by the update flow to point at a
    /// freshly-installed binary at the original on-disk path while the running
    /// process keeps the old one renamed to <c>.old</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task TriggerHandoffAsync(string? successorExePath = null, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var adoptedEventName = $"D2BotNG.Handoff.Adopted.{sessionId}";

        using var adoptedEvent = new EventWaitHandle(false, EventResetMode.ManualReset, adoptedEventName);

        string? manifestPath = null;
        var successorStarted = false;
        try
        {
            var manifest = await BuildManifestAsync(adoptedEventName);
            manifestPath = Path.Combine(Path.GetTempPath(), $"D2BotNG.Handoff.{sessionId}.json");
            await File.WriteAllTextAsync(manifestPath, SerializeManifest(manifest), cancellationToken);
            _logger.LogInformation("Wrote handoff manifest to {Path} ({Profiles} profiles, {Payloads} participant payloads)",
                manifestPath, manifest.Profiles.Count, manifest.Payloads.Count);

            var exe = successorExePath
                ?? Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot resolve own exe path");
            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                // UseShellExecute=false so the successor inherits our token and integrity level.
                // ShellExecute can drop the child to medium integrity even when we're elevated,
                // which makes UIPI silently block WM_COPYDATA from the new manager to the
                // high-integrity game windows.
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            };

            // Preserve the original command-line args so the successor stays in the same
            // mode (--headless, --dev-ui, etc.). Skip any existing --takeover arg in case
            // this is a chained handoff.
            var originalArgs = Environment.GetCommandLineArgs();
            for (var i = 1; i < originalArgs.Length; i++)
            {
                if (originalArgs[i] == TakeoverFlag) { i++; continue; } // skip flag + value
                startInfo.ArgumentList.Add(originalArgs[i]);
            }
            startInfo.ArgumentList.Add(TakeoverFlag);
            startInfo.ArgumentList.Add(manifestPath);

            // Hand the data directory over BEFORE the successor exists. It signals Adopted
            // at the top of Main and then migrates — we stay alive and message-driven through
            // all of that, and a single stale save here rewrites whole files from a list this
            // build's schema parsed, silently dropping fields the successor just added.
            _writeGate.Close();

            _logger.LogInformation("Spawning successor: {Exe} {Args}", exe, string.Join(' ', startInfo.ArgumentList));
            var successor = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start successor process");
            successorStarted = true;

            _logger.LogInformation("Successor PID {Pid}. Waiting for Adopted signal...", successor.Id);
            var adopted = await Task.Run(() => adoptedEvent.WaitOne(TimeSpan.FromSeconds(30)), cancellationToken);
            if (!adopted)
            {
                if (successor.HasExited)
                {
                    _logger.LogError("Handoff aborted: successor exited (code {Code}) without signalling Adopted", successor.ExitCode);

                    // It is gone, so the data directory is unambiguously ours again. Whatever
                    // changed while the gate was closed is still in memory and lands on the
                    // next save, which rewrites the whole file.
                    _writeGate.Reopen();
                }
                else
                {
                    // Still alive and possibly mid-migration. Taking the directory back now
                    // would be exactly the two-writers race the gate exists to prevent, so we
                    // stay read-only rather than risk it.
                    _logger.LogError(
                        "Handoff aborted: successor did not signal Adopted within 30s but is still "
                        + "running. This instance will not persist any further changes until it is "
                        + "restarted — two instances writing one data directory would corrupt it");
                }

                return;
            }

            _logger.LogInformation("Successor adopted the job handle. Quiescing engine and shutting down so successor can bind the server port.");
            _profileEngine.QuiesceForHandoff();
            _appLifetime.StopApplication();
        }
        catch
        {
            // Cleanup manifest on any failure path so it doesn't accumulate in %TEMP%.
            CleanupManifest(manifestPath);
            // Only take the data directory back if no successor ever got as far as starting;
            // once one exists we cannot tell whether it has begun migrating, and sharing the
            // directory with it is the failure this gate prevents.
            if (!successorStarted) _writeGate.Reopen();
            throw;
        }
    }

    private async Task<HandoffManifest> BuildManifestAsync(string adoptedEventName)
    {
        var payloads = new Dictionary<string, JsonElement>();
        foreach (var participant in _participants)
        {
            try
            {
                var snapshot = await participant.SnapshotAsync();
                if (snapshot == null) continue;
                payloads[participant.HandoffKey] = JsonSerializer.SerializeToElement(snapshot, ManifestJsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Snapshot failed for handoff participant {HandoffKey}", participant.HandoffKey);
            }
        }

        return new HandoffManifest
        {
            SchemaVersion = HandoffManifest.CurrentSchemaVersion,
            OldPid = Environment.ProcessId,
            JobHandle = _processManager.GetJobHandle().ToInt64(),
            AdoptedEventName = adoptedEventName,
            Profiles = _profileEngine.SnapshotInstances(),
            Payloads = payloads
        };
    }

    public async Task RestoreAsync(HandoffManifest manifest)
    {
        foreach (var participant in _participants)
        {
            if (!manifest.Payloads.TryGetValue(participant.HandoffKey, out var payload)) continue;
            try
            {
                await participant.RestoreAsync(payload, ManifestJsonOptions);
                _logger.LogDebug("Restored handoff participant {HandoffKey}", participant.HandoffKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore failed for handoff participant {HandoffKey}", participant.HandoffKey);
            }
        }

        _logger.LogInformation("Restored {Count} participant payloads from manifest", manifest.Payloads.Count);
    }

    /// <summary>
    /// Deletes the manifest file once restoration completes. Best-effort: failures are logged, not thrown.
    /// </summary>
    public void CleanupManifest(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            File.Delete(path);
            _logger.LogDebug("Deleted handoff manifest at {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete handoff manifest at {Path}", path);
        }
    }

    private static string SerializeManifest(HandoffManifest manifest) =>
        JsonSerializer.Serialize(manifest, ManifestJsonOptions);

    public static HandoffManifest DeserializeManifest(string json) =>
        JsonSerializer.Deserialize<HandoffManifest>(json, ManifestJsonOptions)
            ?? throw new InvalidOperationException("Manifest deserialized to null");

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
