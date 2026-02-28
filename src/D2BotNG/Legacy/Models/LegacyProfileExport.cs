using D2BotNG.Core.Protos;
using D2BotNG.Engine;
using JetBrains.Annotations;

namespace D2BotNG.Legacy.Models;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class LegacyProfileExport
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Account { get; set; } = "";
    public string Character { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public string Realm { get; set; } = "";
    public string Game { get; set; } = "";
    public string Entry { get; set; } = "";
    public string Tag { get; set; } = "";

    public static LegacyProfileExport FromProfile(Profile profile, ProfileInstance? instance) => new()
    {
        Name = profile.Name,
        Status = instance?.Status ?? MapRunState(instance?.State ?? RunState.Stopped),
        Account = profile.Account,
        Character = profile.Character,
        Difficulty = MapDifficulty(profile.Difficulty),
        Realm = MapRealm(profile.Realm),
        Game = profile.D2Path,
        Entry = Path.GetFileName(profile.EntryScript),
        Tag = profile.InfoTag
    };

    internal static string MapRunState(RunState state) => state switch
    {
        RunState.Stopped => "Stopped",
        RunState.Starting => "Starting",
        RunState.Running => "Running",
        RunState.Error => "Error",
        RunState.Stopping => "Stopping",
        _ => "Unknown"
    };

    internal static string MapDifficulty(Difficulty difficulty) => difficulty switch
    {
        Core.Protos.Difficulty.Normal => "Normal",
        Core.Protos.Difficulty.Nightmare => "Nightmare",
        Core.Protos.Difficulty.Hell => "Hell",
        Core.Protos.Difficulty.Highest => "Highest",
        _ => ""
    };

    internal static string MapRealm(Realm realm) => realm switch
    {
        Core.Protos.Realm.UsWest => "uswest",
        Core.Protos.Realm.UsEast => "useast",
        Core.Protos.Realm.Europe => "europe",
        Core.Protos.Realm.Asia => "asia",
        _ => ""
    };
}
