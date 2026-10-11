using D2BotNG.Core.Protos.Captures;
using ToolkitVariant = D2ItemToolkit.GameVariant;

namespace D2BotNG.Capture;

/// <summary>
/// Which game a capture is of, read off what the producer reported — the one place that reading
/// is done. Ingest picks the tables an item is resolved with from it, search filters on the
/// columns it fills, and the UI renders with the variant it serves, so all three agree by
/// construction rather than by keeping three copies of a rule in step.
/// </summary>
internal static class GameEdition
{
    /// <summary>.d2s status byte: the expansion bit, the one game answer an older producer gives.</summary>
    private const uint ExpansionCharFlag = 0x20;

    public const int Classic = 1;
    public const int Expansion = 2;
    public const int ReignOfTheWarlock = 3;

    /// <summary>
    /// The game a capture is of.
    /// </summary>
    /// <param name="backend">
    /// Snapshot.backend as stored: "LOD/1.14d" or "D2R/91923", the bare "1.14d" / "d2r" an
    /// intermediate producer sent, or empty from one that predates the field. Only the part before
    /// the build is read; anything not D2R is LOD, which is the only game an older producer ran.
    /// </param>
    /// <param name="gameType">Identity.game_type — 0 Classic, 1 Expansion, 2 RotW — or null when never reported.</param>
    /// <param name="charFlags">Identity.char_flags, read for the expansion bit when there is no game type.</param>
    public static Game Of(string backend, int? gameType, uint charFlags)
    {
        var family = backend.Split('/', 2)[0];
        var flavor = family.Equals("D2R", StringComparison.OrdinalIgnoreCase)
            ? GameFlavor.D2R
            : GameFlavor.Lod;

        var version = gameType is { } type
            ? Math.Clamp(type + 1, Classic, ReignOfTheWarlock)
            : (charFlags & ExpansionCharFlag) != 0 ? Expansion : Classic;
        // Reign of the Warlock is a D2R game; a LOD producer reporting it is reporting something
        // this reading does not know, and the 1.14d expansion is the nearest truth.
        if (flavor == GameFlavor.Lod && version == ReignOfTheWarlock) version = Expansion;

        var variant = flavor == GameFlavor.Lod
            ? GameVariant.Lod114D
            : version == ReignOfTheWarlock
                ? GameVariant.ReignOfTheWarlock
                : GameVariant.Resurrected;

        return new Game { Flavor = flavor, Version = version, Variant = variant };
    }

    /// <summary>The toolkit's variant for a served one. The proto enum mirrors its values.</summary>
    public static ToolkitVariant ToToolkit(GameVariant variant) => variant switch
    {
        GameVariant.Resurrected => ToolkitVariant.Resurrected,
        GameVariant.ReignOfTheWarlock => ToolkitVariant.ReignOfTheWarlock,
        _ => ToolkitVariant.Lod114d,
    };
}
