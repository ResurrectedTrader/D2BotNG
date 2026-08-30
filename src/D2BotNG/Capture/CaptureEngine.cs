using System.Text.Json;
using D2BotNG.Core.Protos;          // Settings and the event envelope
using D2BotNG.Core.Protos.Captures;
using D2BotNG.Data;
using D2BotNG.Services;
using D2BotNG.Utilities;
using Google.Protobuf.WellKnownTypes;

namespace D2BotNG.Capture;

/// <summary>
/// Ingests wire schema v2 character snapshots into the capture store — the lifecycle half of the
/// stack whose storage half is <see cref="CaptureStore" />.
///
/// Thin by design: parse and delegate. The payload parses straight into <see cref="Snapshot" />,
/// and everything about how a partial snapshot accumulates lives in the store, because that
/// accumulation IS the database.
/// </summary>
public sealed class CaptureEngine : IHostedService
{
    /// <summary>The wire schema this stack handles.</summary>
    public const int SchemaVersion = 2;

    private readonly ILogger<CaptureEngine> _logger;
    private readonly CaptureStore _store;
    private readonly SettingsRepository _settings;
    private readonly EventBroadcaster _events;

    public CaptureEngine(ILogger<CaptureEngine> logger, CaptureStore store, SettingsRepository settings,
        EventBroadcaster events)
    {
        _logger = logger;
        _store = store;
        _settings = settings;
        _events = events;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _store.Open();
        // The database lives under the data directory, so a base-path change moves it. Reopening
        // is idempotent and cheap when the path is unchanged.
        _settings.SettingsChanged += OnSettingsChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _settings.SettingsChanged -= OnSettingsChanged;
        // Symmetric with Open() above, and load-bearing: StopAsync does not dispose the DI
        // container, so without this the connection is abandoned at process exit and the WAL
        // survives with the database itself still empty.
        _store.Close();
        return Task.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, Settings settings) => _store.Open();

    /// <summary>
    /// Reads just the schemaVersion out of a raw payload, so the router can decide which stack
    /// owns it before committing to a shape. Returns 0 for anything it cannot read — absent,
    /// non-numeric, or not JSON at all — and no schema claims 0, so such a payload routes to the
    /// other stack rather than being dropped here.
    /// </summary>
    public static int PeekSchemaVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            // Both ValueKind tests guard an InvalidOperationException, which is NOT a JsonException
            // and so would escape the catch below and lose the message: TryGetProperty throws on a
            // root that is not an object (an array, string or number is valid JSON), and
            // TryGetInt32 on a value that is not a number.
            return root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("schemaVersion", out var value)
                   && value.ValueKind == JsonValueKind.Number
                   && value.TryGetInt32(out var version)
                ? version
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>Parses and stores one snapshot the router has already claimed for this stack.</summary>
    public void Ingest(string profile, string json)
    {
        CharacterSummary? summary;
        try
        {
            summary = _store.Apply(profile, ProtobufJsonConfig.Parser.Parse<Snapshot>(json));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store character capture for {Profile}", profile);
            return;
        }

        // A null summary means the store is disabled, so nothing was written and there is nothing
        // to announce. Broadcasting anyway bumps the client's revision for that profile once a
        // second, and each bump schedules a refetch of a character that cannot load.
        if (summary == null) return;

        // Announced only after Apply returns, so a client that refetches on this signal cannot
        // read a half-applied capture — and a snapshot that threw announces nothing at all.
        //
        // Apply hands the summary back rather than the store being asked for it again: it was
        // read under the same lock as the commit, so it describes the snapshot this event
        // announces and not whatever landed between the two calls.
        //
        // The event carries the summary but NOT the capture: an inventory's worth of stat lists is
        // exactly what this stack fetches on demand rather than pushes, and one manager can be
        // running hundreds of profiles all reporting at once. The client refetches the character
        // only if it happens to be showing it.
        _events.Broadcast(new Event
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            CaptureChanged = new CaptureChanged
            {
                Profile = profile,
                Summary = summary,
            },
        });
    }
}
