using System.Text.Json.Serialization;

namespace D2BotNG.Legacy.Models;

public class LegacyResponse
{
    [JsonPropertyName("request")]
    public string Request { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "failed";

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    public override string ToString()
    {
        return $"{nameof(Request)}: {Request}, {nameof(Status)}: {Status}, {nameof(Body)}: {Body}";
    }
}
