using D2BotNG.Core.Protos;

namespace D2BotNG.Services;

public static class Extensions
{
    public static void PreserveStatsFrom(this Profile target, Profile source)
    {
        target.Runs = source.Runs;
        target.Chickens = source.Chickens;
        target.Deaths = source.Deaths;
        target.Crashes = source.Crashes;
        target.Restarts = source.Restarts;
        target.KeyRuns = source.KeyRuns;
    }
}
