using D2BotNG.Core.Protos.Captures;
using D2BotNG.Utilities;
using Xunit;

namespace D2BotNG.Tests.Capture;

/// <summary>
/// Pins what proto3 JSON does with presence, because two design decisions rest on it: merc
/// dismissal has to ride `keyframe` rather than `"merc": null`, and a wearer document has to be
/// detected by whether `name` was SENT rather than by what it contains.
/// </summary>
public class MercPresenceTests
{
    [Fact]
    public void AbsentMercLeavesTheFieldUnset()
    {
        var snapshot = ProtobufJsonConfig.Parser.Parse<Snapshot>("""{"schemaVersion":2}""");
        Assert.Null(snapshot.Merc);
    }

    [Fact]
    public void ExplicitNullMercIsIndistinguishableFromAbsent()
    {
        var snapshot = ProtobufJsonConfig.Parser.Parse<Snapshot>("""{"schemaVersion":2,"merc":null}""");

        // proto3 JSON maps null to "the default value", and a message field's default is unset —
        // so this is null too, exactly like the absent case above. Marking the field `optional`
        // does not change it: a message field already has explicit presence, and the information
        // is lost in the JSON mapping rather than in the presence model.
        Assert.Null(snapshot.Merc);
    }

    [Fact]
    public void AnEmptyNameIsPresentWhileAnAbsentOneIsNot()
    {
        // Where an OPTIONAL SCALAR differs from the message field above: the has-bit follows key
        // presence, not value, so an empty name is still a document that arrived. The store reads
        // exactly this to decide whether a wearer block is present.
        var sent = ProtobufJsonConfig.Parser.Parse<Unit>("""{"name":"","area":83}""");
        Assert.True(sent.HasName);

        var omitted = ProtobufJsonConfig.Parser.Parse<Unit>("""{"area":83}""");
        Assert.False(omitted.HasName);
    }
}
