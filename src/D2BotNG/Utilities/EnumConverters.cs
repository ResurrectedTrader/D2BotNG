using D2BotNG.Core.Protos;

namespace D2BotNG.Utilities;

public static class EnumConverters
{
    public static string ToIniString(this Difficulty difficulty) => difficulty switch
    {
        Difficulty.Normal => "0",
        Difficulty.Nightmare => "1",
        Difficulty.Hell => "2",
        Difficulty.Highest => "3",
        _ => "0"
    };

    public static string ToIniString(this Realm realm) => realm switch
    {
        Realm.UsWest => "West",
        Realm.UsEast => "East",
        Realm.Europe => "Europe",
        Realm.Asia => "Asia",
        _ => ""
    };

    public static string ToIniString(this GameMode mode) => mode switch
    {
        GameMode.SinglePlayer => "Single Player",
        GameMode.BattleNet => "Battle.net",
        GameMode.OpenBattleNet => "Open Battle.net",
        GameMode.TcpHost => "Host TCP/IP Game",
        GameMode.TcpJoin => "Join TCP/IP Game",
        _ => ""
    };

    public static Difficulty ParseDifficulty(string value)
    {
        var lower = value.ToLowerInvariant();
        return lower switch
        {
            "normal" or "0" => Difficulty.Normal,
            "nightmare" or "1" => Difficulty.Nightmare,
            "hell" or "2" => Difficulty.Hell,
            "highest" or "3" => Difficulty.Highest,
            _ => Difficulty.Unspecified
        };
    }

    public static Realm ParseRealm(string value)
    {
        var lower = value.ToLowerInvariant();
        return lower switch
        {
            "west" or "uswest" or "uswest.battle.net" => Realm.UsWest,
            "east" or "useast" or "useast.battle.net" => Realm.UsEast,
            "europe" or "europe.battle.net" => Realm.Europe,
            "asia" or "asia.battle.net" => Realm.Asia,
            _ => Realm.Unspecified
        };
    }
}
