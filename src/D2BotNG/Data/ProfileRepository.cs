using D2BotNG.Core.Protos;
using D2BotNG.Services;

namespace D2BotNG.Data;

public class ProfileRepository : FileRepository<Profile, ProfileList>
{
    private readonly IniWriter _iniWriter;
    private readonly FrameworkRepository _frameworkRepository;

    public ProfileRepository(Paths paths, IniWriter iniWriter, FrameworkRepository frameworkRepository)
        : base(paths, "profiles.json")
    {
        _iniWriter = iniWriter;
        _frameworkRepository = frameworkRepository;
    }

    protected override string GetKey(Profile p) => p.Name;

    protected override IList<Profile> GetItems(ProfileList list) => list.Profiles;

    protected override ProfileList CreateList(IEnumerable<Profile> items)
    {
        var list = new ProfileList();
        list.Profiles.AddRange(items);
        return list;
    }

    protected override async Task SaveAsync()
    {
        await base.SaveAsync();
        // Rewrite each framework's d2bs.ini so it reflects only its assigned profiles.
        // Runs under the repository lock (so use Items, not GetAllAsync, which would
        // deadlock), which also orders ini writes with profile saves.
        await _iniWriter.WriteAsync(Items.ToList(), await _frameworkRepository.GetAllAsync());
    }

    /// <summary>
    /// Rewrites every framework's d2bs.ini from the current profile list, under the
    /// repository lock so the write stays ordered against concurrent profile saves.
    /// For callers that changed frameworks without touching profiles.
    /// </summary>
    public async Task RewriteInisAsync()
    {
        await EnsureLoadedAsync();

        await Lock.WaitAsync();
        try
        {
            await _iniWriter.WriteAsync(Items.ToList(), await _frameworkRepository.GetAllAsync());
        }
        finally
        {
            Lock.Release();
        }
    }
}
