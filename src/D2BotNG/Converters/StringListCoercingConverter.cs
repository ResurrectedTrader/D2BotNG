using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2BotNG.Converters;

public sealed class StringListCoercingConverter : JsonConverter<string[]>
{
    public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected array");

        var result = new List<string>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return result.ToArray();

            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    result.Add(reader.GetString()!);
                    break;

                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out var l))
                        result.Add(l.ToString());
                    else if (reader.TryGetDouble(out var d))
                        result.Add(d.ToString(CultureInfo.InvariantCulture));
                    else
                        throw new JsonException("Unsupported number format");
                    break;

                case JsonTokenType.True:
                case JsonTokenType.False:
                    result.Add(reader.GetBoolean().ToString());
                    break;

                case JsonTokenType.Null:
                    result.Add("null");
                    break;

                default:
                    throw new JsonException($"Unsupported token {reader.TokenType}");
            }
        }

        throw new JsonException("Unexpected end of array");
    }

    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var s in value)
            writer.WriteStringValue(s);
        writer.WriteEndArray();
    }
}
