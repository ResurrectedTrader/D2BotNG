using D2BotNG.Core.Protos;

namespace D2BotNG.Data;

public class PatchRepository : FileRepository<Patch, PatchCollection>
{
    private static readonly string[] ModuleNames =
    [
        "D2CLIENT.dll", "D2COMMON.dll", "D2GFX.dll", "D2LANG.dll", "D2WIN.dll",
        "D2NET.dll", "D2GAME.dll", "D2LAUNCH.dll", "FOG.dll", "BNCLIENT.dll",
        "STORM.dll", "D2CMP.dll", "D2MULTI.dll", "D2MCPCLIENT.dll", "Game.exe"
    ];

    public PatchRepository(Paths paths, DataWriteGate writeGate) : base(paths, writeGate, "patches.json") { }

    protected override string GetKey(Patch patch) => $"{patch.Name}{patch.Version}";

    protected override IList<Patch> GetItems(PatchCollection list) => list.Patches;

    protected override PatchCollection CreateList(IEnumerable<Patch> items)
    {
        var list = new PatchCollection();
        list.Patches.AddRange(items);
        return list;
    }

    public async Task<List<Patch>> GetPatchesForVersionAsync(string version)
    {
        var patches = await GetAllAsync();
        return patches.Where(p => p.Version.Equals(version, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static string GetModuleName(D2Module module)
    {
        return ModuleNames[(int)module];
    }
}
