namespace D2BotNG.Rendering;

/// <summary>
/// The colour tables D2R tints item art with.
///
/// Generated from the same extraction as the frontend's <c>hdTintTables.ts</c> rather than retyped,
/// because a slip in any one cell is a wrong colour on one item quality and nothing would catch it.
/// The two files must agree: an item posted to Discord and the same item in the viewer are meant to
/// be the same picture.
///
/// A tint value is a composite: <c>(invtrans % 10) * 21 + colourIndex</c>. The high part picks a row
/// of <see cref="Ranges" /> (the HSV band a transform applies within), the low part picks the
/// colour. Values at or above nine rows take the flat <see cref="Flat" /> path instead.
/// </summary>
internal static class HdTintTables
{
    /// <summary><c>invtransform</c> names, in the order the game indexes them. This is also
    /// colors.txt's row order, which is what lets a classic palette shift name a colour here.</summary>
    public static readonly string[] ColorNames =
    [
        "whit",
        "lgry",
        "dgry",
        "blac",
        "lblu",
        "dblu",
        "cblu",
        "lred",
        "dred",
        "cred",
        "lgrn",
        "dgrn",
        "cgrn",
        "lyel",
        "dyel",
        "lgld",
        "dgld",
        "lpur",
        "dpur",
        "oran",
        "bwht",
    ];

    /// <summary>Per row: hue centre and width, saturation centre and width, value centre and width.</summary>
    public static readonly float[][] Ranges =
    [
        [0f, 0f, 0f, 0f, 0f, 0f],
        [0f, 0f, 0f, 0f, 0f, 0f],
        [0f, 0.501f, 0.14f, 0.14f, 0.425f, 0.54f],
        [0f, 0f, 0f, 0f, 0f, 0f],
        [0f, 0f, 0f, 0f, 0f, 0f],
        [0f, 0.501f, 0.175f, 0.175f, 0.4f, 0.4f],
        [0f, 0f, 0f, 0f, 0f, 0f],
        [0f, 0f, 0f, 0f, 0f, 0f],
        [0.05f, 0.06f, 0.5f, 0.501f, 0.585f, 0.415f],
    ];

    /// <summary>Per colour: target RGB, mix strength, then hue shift, saturation scale, value scale.</summary>
    public static readonly float[][] Transforms =
    [
        [0f, 0f, 0f, 0f, 0f, -0.5f, 0.5f],
        [0f, 0f, 0f, 0f, 0f, -0.55f, -0.35f],
        [0f, 0f, 0f, 0f, 0f, -0.6f, -0.5f],
        [0f, 0f, 0f, 0f, 0f, -0.65f, -0.65f],
        [0.595f, 0.96f, 0.98f, 0.4f, -0.47f, 0.2f, 0.6f],
        [0.473f, 0.77f, 0.788f, 0.6f, -0.46f, 0.25f, 0.2f],
        [0.427f, 0.698f, 0.728f, 0.5f, 0.58f, 0.2f, 0.5f],
        [1f, 0f, 0f, 0.5f, 0f, 0.25f, 0.55f],
        [1f, 0f, 0f, 0.5f, 0f, 0.75f, -0.35f],
        [0.9f, 0.1f, 0f, 0.7f, 0f, 1f, 0.5f],
        [0f, 1f, 0f, 0.4f, 0.25f, 0.25f, 0.3f],
        [0f, 1f, 0f, 0.3f, 0.25f, 0f, -0.3f],
        [0f, 1f, 0f, 0.3f, 0.25f, 1f, 0.4f],
        [0.8f, 0.68f, 0.25f, 0.8f, 0.065f, 0.4f, 0.4f],
        [0.57f, 0.49f, 0.28f, 0.7f, 0.05f, 0.4f, -0.2f],
        [0.882f, 0.813f, 0.71f, 0.6f, 0.04f, 0f, 0.5f],
        [0.686275f, 0.403922f, 0.011765f, 0.6f, 0f, 0f, 0.5f],
        [0.553f, 0.137f, 0.51f, 0.3f, -0.33f, 0.8f, 0.4f],
        [0.553f, 0.137f, 0.51f, 0.75f, -0.16f, 0.35f, -0.25f],
        [1f, 0.25f, 0f, 0.42f, 0.04f, 0.3f, 0f],
        [0f, 0f, 0f, 0f, 0f, -0.5f, 1.05f],
    ];

    /// <summary>The flat multiplicative tints, RGBA. Index 6 is deliberately out of gamut.</summary>
    public static readonly float[][] Flat =
    [
        [1f, 1f, 1f, 1f],
        [1f, 0.3f, 0.3f, 1f],
        [0f, 1f, 0f, 1f],
        [0.4117647058823529f, 0.4117647058823529f, 1f, 1f],
        [0.7803921568627451f, 0.7019607843137254f, 0.4666666666666667f, 1f],
        [0.5f, 0.5f, 0.5f, 1f],
        [1.5f, 1.5f, 1.5f, 1f],
        [0.8156862745098039f, 0.7607843137254902f, 0.49019607843137253f, 1f],
        [1f, 0.6588235294117647f, 0f, 1f],
        [1f, 1f, 0.39215686274509803f, 1f],
        [0f, 0.5019607843137255f, 0f, 1f],
        [0.6823529411764706f, 0f, 1f, 1f],
        [0f, 0.7843137254901961f, 0f, 1f],
    ];
}
