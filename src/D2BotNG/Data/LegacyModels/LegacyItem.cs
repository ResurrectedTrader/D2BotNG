using System.Text.Json.Serialization;
using D2BotNG.Core.Protos;

namespace D2BotNG.Data.LegacyModels;

/// <summary>
/// Represents the item format sent by Kolbot/D2Bot game client.
/// Use ToItem() to convert to the protobuf Item type.
/// </summary>
public class LegacyItem
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("image")]
    public string Image { get; init; } = "";

    [JsonPropertyName("textColor")]
    public int TextColor { get; init; }

    [JsonPropertyName("itemColor")]
    public int ItemColor { get; init; }

    [JsonPropertyName("header")]
    public string Header { get; init; } = "";

    [JsonPropertyName("sockets")]
    public List<string> Sockets { get; init; } = [];

    public Item ToModern()
    {
        var item = new Item
        {
            Header = Header,
            Code = Image,
            Name = Title,
            Description = Description,
            ItemColor = ItemColor >= 0 ? (uint)ItemColor : 0,
            TextColor = TextColor >= 0 ? (uint)TextColor : 0
        };

        foreach (var socketCode in Sockets)
        {
            var socketItem = new Item();

            if (socketCode.Contains('|'))
            {
                var parts = socketCode.Split('|');
                socketItem.Code = parts[0];
                if (int.TryParse(parts[1], out var color) && color >= 0)
                {
                    socketItem.ItemColor = (uint)color;
                }
            }
            else
            {
                socketItem.Code = socketCode;
            }

            item.Sockets.Add(socketItem);
        }

        return item;
    }
}
