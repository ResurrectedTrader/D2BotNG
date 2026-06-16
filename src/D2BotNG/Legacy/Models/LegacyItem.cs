using System.Text.Json.Serialization;
using D2BotNG.Core.Protos;
using JetBrains.Annotations;

namespace D2BotNG.Legacy.Models;

/// <summary>
/// Represents the item format sent by Kolbot/D2Bot game client.
/// Use ToModern() to convert to the protobuf Item type.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class LegacyItem
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("image")]
    public string Image { get; init; } = "";

    [JsonPropertyName("textColor")]
    public int TextColor { get; init; } = -1;

    [JsonPropertyName("itemColor")]
    public int ItemColor { get; init; } = -1;

    [JsonPropertyName("header")]
    public string Header { get; init; } = "";

    [JsonPropertyName("sockets")]
    public List<string> Sockets { get; init; } = [];

    // Character-viewer fields (absent for mule-file items; default to 0/false).
    // For slot containers (equipped/merc) the equip-location id rides in x (y = 0).
    [JsonPropertyName("gid")]
    public uint Gid { get; init; }

    [JsonPropertyName("ethereal")]
    public bool Ethereal { get; init; }

    [JsonPropertyName("quality")]
    public int Quality { get; init; }

    [JsonPropertyName("location")]
    public int Location { get; init; }

    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("w")]
    public int W { get; init; }

    [JsonPropertyName("h")]
    public int H { get; init; }

    // Defaults to 8 (invgreybrown) so items lacking an invTrans field — existing mule-file
    // items, or a sender that doesn't emit it — keep the legacy inventory tint. Character
    // items send an explicit value (including 0 for non-tintable bases), which overrides this.
    [JsonPropertyName("invTrans")]
    public int InvTrans { get; init; } = 8;

    public Item ToModern()
    {
        var item = new Item
        {
            Header = Header,
            Code = Image,
            Name = Title,
            Description = Description,
            ItemColor = ItemColor,
            TextColor = TextColor,
            Gid = Gid,
            Ethereal = Ethereal,
            Quality = Quality,
            Location = Location,
            X = X,
            Y = Y,
            Width = W,
            Height = H,
            InvTrans = InvTrans
        };

        foreach (var socketCode in Sockets)
        {
            var socketItem = new Item { ItemColor = -1, TextColor = -1 };

            if (socketCode.Contains('|'))
            {
                var parts = socketCode.Split('|');
                socketItem.Code = parts[0];
                if (int.TryParse(parts[1], out var color))
                {
                    socketItem.ItemColor = color;
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
