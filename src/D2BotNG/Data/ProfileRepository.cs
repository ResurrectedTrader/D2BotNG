using D2BotNG.Core.Protos;
using D2BotNG.Services;

namespace D2BotNG.Data;

public class ProfileRepository : FileRepository<Profile, ProfileList>
{
    private readonly IniWriter _iniWriter;

    public ProfileRepository(Paths paths, IniWriter iniWriter) : base(paths, "profiles.json")
    {
        _iniWriter = iniWriter;
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
        await _iniWriter.WriteAsync(Data);
    }
}
