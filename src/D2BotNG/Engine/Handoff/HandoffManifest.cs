using System.Text.Json;
using System.Text.Json.Serialization;
using D2BotNG.Core.Protos;

namespace D2BotNG.Engine.Handoff;

/// <summary>
/// Snapshot of state that must be carried across an in-place process restart.
/// The job handle is duplicated separately via Win32; profile instances are
/// special-cased because they require attaching to live PIDs; everything else
/// is contributed by <see cref="IHandoffParticipant"/> implementations and stored
/// in <see cref="Payloads"/> keyed by participant.
/// </summary>
public class HandoffManifest
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("oldPid")] public int OldPid { get; set; }

    [JsonPropertyName("jobHandle")] public long JobHandle { get; set; }

    /// <summary>Successor signals this event after duplicating the job handle, before building DI.</summary>
    [JsonPropertyName("adoptedEventName")] public string AdoptedEventName { get; set; } = "";

    [JsonPropertyName("profiles")] public List<HandoffProfile> Profiles { get; set; } = new();

    [JsonPropertyName("payloads")] public Dictionary<string, JsonElement> Payloads { get; set; } = new();
}

public class HandoffProfile
{
    [JsonPropertyName("profileName")] public string ProfileName { get; set; } = "";

    [JsonPropertyName("pid")] public int Pid { get; set; }

    [JsonPropertyName("state")] public RunState State { get; set; }

    [JsonPropertyName("status")] public string Status { get; set; } = "";

    [JsonPropertyName("keyName")] public string? KeyName { get; set; }

    [JsonPropertyName("proxyName")] public string? ProxyName { get; set; }

    // Renamed from "crashCount" when the retry budget was scoped to launch failures. A manifest
    // written by an older predecessor simply won't bind this, leaving 0 — a fresh budget, which
    // is the permissive direction.
    [JsonPropertyName("launchFailureCount")] public int LaunchFailureCount { get; set; }

    [JsonPropertyName("startedAt")] public DateTime? StartedAt { get; set; }

    [JsonPropertyName("handle")] public long Handle { get; set; }
}
