using Google.Protobuf;

namespace D2BotNG.Utilities;

/// <summary>
/// Shared protobuf JSON serialization configuration used across repositories and migration.
/// </summary>
public static class ProtobufJsonConfig
{
    public static readonly JsonFormatter Formatter =
        new(JsonFormatter.Settings.Default.WithIndentation());

    public static readonly JsonParser Parser =
        new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));
}
