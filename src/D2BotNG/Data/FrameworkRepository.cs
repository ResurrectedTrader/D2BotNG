using D2BotNG.Core.Protos;

namespace D2BotNG.Data;

/// <summary>
/// File-backed collection of frameworks (frameworks.json). A framework bundles the game
/// install directory, botting-framework directory, inject DLL(s), and game version; the
/// launched executable is the profile's own d2_path. Profiles reference a framework by
/// name (<see cref="Profile.Framework"/>).
/// </summary>
public class FrameworkRepository : FileRepository<Framework, FrameworkCollection>
{
    public FrameworkRepository(Paths paths, DataWriteGate writeGate) : base(paths, writeGate, "frameworks.json") { }

    protected override string GetKey(Framework f) => f.Name;

    protected override IList<Framework> GetItems(FrameworkCollection list) => list.Frameworks;

    protected override FrameworkCollection CreateList(IEnumerable<Framework> items)
    {
        // Persist sorted by name so the on-disk file is stable and diff-friendly.
        // This does NOT determine what clients see: the in-memory list stays in
        // creation order until a reload, and BuildFrameworksSnapshotAsync sorts
        // every snapshot itself.
        var list = new FrameworkCollection();
        list.Frameworks.AddRange(items.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase));
        return list;
    }
}
