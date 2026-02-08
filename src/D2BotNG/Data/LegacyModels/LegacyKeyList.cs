using System.Text.Json.Serialization;
using D2BotNG.Core.Protos;

namespace D2BotNG.Data.LegacyModels;

public class LegacyKeyList
{
    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("CDKeys")]
    public List<LegacyCDKey> CDKeys { get; init; } = [];

    public KeyList ToModern()
    {
        var keyList = new KeyList { Name = Name };
        foreach (var key in CDKeys)
        {
            keyList.Keys.Add(new CDKey
            {
                Name = key.Name,
                Classic = key.Classic,
                Expansion = key.Expansion
                // held, realm_downs default to false/0
            });
        }
        return keyList;
    }

}

public class LegacyCDKey
{
    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("Classic")]
    public string Classic { get; init; } = "";

    [JsonPropertyName("Expansion")]
    public string Expansion { get; init; } = "";
}
