using System.Text.Json.Serialization;

namespace D2BotNG.Legacy.Models;

public class LegacyRequest
{
    [JsonPropertyName("session")] public string Session { get; set; } = "";

    [JsonPropertyName("profile")] public string Profile { get; set; } = "";

    [JsonPropertyName("func")] public string Func { get; set; } = "";

    [JsonPropertyName("args")] public string[] Args { get; set; } = [];

    public override string ToString()
    {
        return
            $"{nameof(Session)}: {Session}, {nameof(Profile)}: {Profile}, {nameof(Func)}: {Func}, {nameof(Args)}: [{string.Join(", ", Args)}]";
    }
}
