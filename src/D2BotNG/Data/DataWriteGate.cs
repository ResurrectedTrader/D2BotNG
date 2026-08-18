namespace D2BotNG.Data;

/// <summary>
/// A process-wide switch that stops this instance persisting anything to the data
/// directory. Held open for the whole normal lifetime; closed only by a handoff.
///
/// During a handoff two D2BotNG processes are alive at once and they share one data
/// directory. The successor signals Adopted at the top of <c>Main</c> — before it runs
/// <see cref="Legacy.Models.Migration"/> and <see cref="FrameworkBootstrap"/> — so the
/// predecessor is still fully live, still receiving D2BS messages, while the successor
/// migrates. Every repository save rewrites its whole file from the in-memory list, and
/// the predecessor's list was parsed by the OLD schema: one run counter arriving in that
/// window rewrites the successor's freshly migrated file and silently drops every field
/// the old build didn't know about. That is how a v0.0.40 update could leave every
/// profile with no <c>framework</c>, which then never self-heals because frameworks.json
/// (written by the successor) survives.
///
/// So the predecessor closes the gate before it spawns the successor: from that moment
/// the data directory belongs to the successor and this process only reads. The gate
/// reopens if the handoff aborts, since then no successor ever took over.
/// </summary>
public sealed class DataWriteGate
{
    private volatile bool _closed;

    /// <summary>True when writes to the data directory must be skipped.</summary>
    public bool IsClosed => _closed;

    /// <summary>Hands the data directory to a successor process. Writes become no-ops.</summary>
    public void Close() => _closed = true;

    /// <summary>Takes the data directory back after a handoff that never completed.</summary>
    public void Reopen() => _closed = false;
}
