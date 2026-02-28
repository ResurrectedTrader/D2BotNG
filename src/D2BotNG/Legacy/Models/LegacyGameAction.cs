using System.Text.Json.Serialization;

namespace D2BotNG.Legacy.Models;

public class LegacyGameAction
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("profile")]
    public string Profile { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("data")]
    public string Data { get; set; } = "";
}
