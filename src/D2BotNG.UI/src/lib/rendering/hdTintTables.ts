/**
 * The colour tables D2R tints item art with, transcribed from d2-planner-web's own generator
 * rather than retyped: 21 colour names, a 9x6 range table, a 21x7 transform table and 13 flat
 * tints. A slip in any cell is a wrong colour on one item quality and nothing would catch it.
 *
 * A tint value is a composite: `(invtrans % 10) * 21 + colourIndex`. The high part picks a row of
 * TINT_TABLE (the HSV range a transform applies within), the low part picks the colour. Values at
 * or above 9 rows use the flat multiplicative TINT_EXTRA path instead.
 */

/** `invtransform` names, in the order the game indexes them. */
export const COLOR_NAMES = [
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
] as const;

/** Per range-table row: hue centre and width, saturation centre and width, value centre and width. */
export const TINT_TABLE: readonly (readonly number[])[] = [
  [0, 0, 0, 0, 0, 0],
  [0, 0, 0, 0, 0, 0],
  [0, 0.501, 0.14, 0.14, 0.425, 0.54],
  [0, 0, 0, 0, 0, 0],
  [0, 0, 0, 0, 0, 0],
  [0, 0.501, 0.175, 0.175, 0.4, 0.4],
  [0, 0, 0, 0, 0, 0],
  [0, 0, 0, 0, 0, 0],
  [0.05, 0.06, 0.5, 0.501, 0.585, 0.415],
];

/** Per colour: target RGB, mix strength, then hue shift, saturation scale, value scale. */
export const TINT_COLOR: readonly (readonly number[])[] = [
  [0, 0, 0, 0, 0, -0.5, 0.5],
  [0, 0, 0, 0, 0, -0.55, -0.35],
  [0, 0, 0, 0, 0, -0.6, -0.5],
  [0, 0, 0, 0, 0, -0.65, -0.65],
  [0.595, 0.96, 0.98, 0.4, -0.47, 0.2, 0.6],
  [0.473, 0.77, 0.788, 0.6, -0.46, 0.25, 0.2],
  [0.427, 0.698, 0.728, 0.5, 0.58, 0.2, 0.5],
  [1, 0, 0, 0.5, 0, 0.25, 0.55],
  [1, 0, 0, 0.5, 0, 0.75, -0.35],
  [0.9, 0.1, 0, 0.7, 0, 1, 0.5],
  [0, 1, 0, 0.4, 0.25, 0.25, 0.3],
  [0, 1, 0, 0.3, 0.25, 0, -0.3],
  [0, 1, 0, 0.3, 0.25, 1, 0.4],
  [0.8, 0.68, 0.25, 0.8, 0.065, 0.4, 0.4],
  [0.57, 0.49, 0.28, 0.7, 0.05, 0.4, -0.2],
  [0.882, 0.813, 0.71, 0.6, 0.04, 0, 0.5],
  [0.686275, 0.403922, 0.011765, 0.6, 0, 0, 0.5],
  [0.553, 0.137, 0.51, 0.3, -0.33, 0.8, 0.4],
  [0.553, 0.137, 0.51, 0.75, -0.16, 0.35, -0.25],
  [1, 0.25, 0, 0.42, 0.04, 0.3, 0],
  [0, 0, 0, 0, 0, -0.5, 1.05],
];

/** The flat multiplicative tints, RGBA. Index 6 is deliberately out of gamut. */
export const TINT_EXTRA: readonly (readonly number[])[] = [
  [1, 1, 1, 1],
  [1, 0.3, 0.3, 1],
  [0, 1, 0, 1],
  [0.4117647058823529, 0.4117647058823529, 1, 1],
  [0.7803921568627451, 0.7019607843137254, 0.4666666666666667, 1],
  [0.5, 0.5, 0.5, 1],
  [1.5, 1.5, 1.5, 1],
  [0.8156862745098039, 0.7607843137254902, 0.49019607843137253, 1],
  [1, 0.6588235294117647, 0, 1],
  [1, 1, 0.39215686274509803, 1],
  [0, 0.5019607843137255, 0, 1],
  [0.6823529411764706, 0, 1, 1],
  [0, 0.7843137254901961, 0, 1],
];
