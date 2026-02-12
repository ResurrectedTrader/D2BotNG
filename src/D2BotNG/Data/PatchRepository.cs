using D2BotNG.Core.Protos;

namespace D2BotNG.Data;

public class PatchRepository : FileRepository<Patch, PatchList>
{
    private static readonly string[] ModuleNames =
    [
        "D2CLIENT.dll", "D2COMMON.dll", "D2GFX.dll", "D2LANG.dll", "D2WIN.dll",
        "D2NET.dll", "D2GAME.dll", "D2LAUNCH.dll", "FOG.dll", "BNCLIENT.dll",
        "STORM.dll", "D2CMP.dll", "D2MULTI.dll", "D2MCPCLIENT.dll", "Game.exe"
    ];

    public PatchRepository(Paths paths) : base(paths, "patches.json") { }

    protected override string GetKey(Patch patch) => $"{patch.Name}{patch.Version}";

    protected override IList<Patch> GetItems(PatchList list) => list.Patches;

    protected override PatchList CreateList(IEnumerable<Patch> items)
    {
        var list = new PatchList();
        list.Patches.AddRange(items);
        return list;
    }

    public async Task<IEnumerable<Patch>> GetPatchesForVersionAsync(string version)
    {
        return (await GetAllAsync()).Where(p => p.Version == version);
    }

    public static string GetModuleName(D2Module module)
    {
        return ModuleNames[(int)module];
    }
}
