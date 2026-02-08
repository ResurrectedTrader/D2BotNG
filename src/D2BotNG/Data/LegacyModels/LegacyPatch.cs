using System.Text.Json.Serialization;
using D2BotNG.Core.Protos;
using Google.Protobuf;

namespace D2BotNG.Data.LegacyModels;

/// <summary>
/// Represents the patch format used by the legacy D2Bot framework.
/// Use ToModern() to convert to the protobuf Patch type.
/// </summary>
public class LegacyPatch
{
    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("Version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("Module")]
    public int Module { get; init; }

    [JsonPropertyName("Offset")]
    public int Offset { get; init; }

    [JsonPropertyName("Data")]
    public byte[] Data { get; init; } = [];

    public Patch ToModern()
    {
        return new Patch
        {
            Name = Name,
            Version = Version,
            Module = (D2Module)Module,
            Offset = Offset,
            Data = ByteString.CopyFrom(Data)
        };
    }

}
