/**
 * D2 text color definitions matching in-game color codes
 */

export interface RGB {
  r: number;
  g: number;
  b: number;
}

export const D2Colors = {
  White: { r: 255, g: 255, b: 255 },
  Red: { r: 255, g: 77, b: 77 },
  Green: { r: 0, g: 255, b: 0 },
  Blue: { r: 105, g: 105, b: 255 },
  Gold: { r: 199, g: 179, b: 119 },
  Gray: { r: 105, g: 105, b: 105 },
  Black: { r: 0, g: 0, b: 0 },
  Tan: { r: 208, g: 194, b: 125 },
  Orange: { r: 255, g: 168, b: 0 },
  Yellow: { r: 255, g: 255, b: 100 },
  DarkGreen: { r: 0, g: 128, b: 0 },
  Purple: { r: 174, g: 0, b: 255 },
  BrightGreen: { r: 0, g: 200, b: 0 },
} as const;

/** The classic font's thirteen colours, by index. */
const CLASSIC: RGB[] = [
  D2Colors.White, // 0
  D2Colors.Red, // 1
  D2Colors.Green, // 2
  D2Colors.Blue, // 3
  D2Colors.Gold, // 4
  D2Colors.Gray, // 5
  D2Colors.Black, // 6
  D2Colors.Tan, // 7
  D2Colors.Orange, // 8
  D2Colors.Yellow, // 9
  D2Colors.DarkGreen, // 10 ':'
  D2Colors.Purple, // 11 ';'
  D2Colors.BrightGreen, // 12 '<'
];

/**
 * D2R's text colours past the classic thirteen, as its HD renderer draws them — read from a running
 * d2r.exe (build 91923). The code character is `'0' + index` in both games
 * (`D2RGFX_D2R_Text_ApplyColorCode` 0x14008c9f0 writes `colorIndex + 48`; `D2R_Text_HasColorCode`
 * 0x140666ff0 accepts 0..38).
 *
 * `D2RGFX_D2_Text` (0x140668260) resolves 13..29 through one hash map and 30..38 through another
 * (lookups at 0x14060B350 / 0x1405F1F50). Each entry carries a classic index and its own RGBA; the
 * RGBA is what is drawn (`D2RGFX_DrawUnitInfo` reads it directly), and is filled from the HD profile
 * at startup — hence read live rather than from the image, where it is zero. Named by what uses
 * them (`ITEM_GetFontColor` 0x14008de00, `D2RGFX_GetStringColorFromItemAndRarity` 0x1401d77a0).
 */
const D2R_EXTENDED: RGB[] = [
  { r: 240, g: 240, b: 240 }, // 13 '=' normal
  { r: 199, g: 179, b: 119 }, // 14 '>'
  { r: 255, g: 255, b: 100 }, // 15 '?' rare
  { r: 255, g: 168, b: 0 }, // 16 '@' crafted
  { r: 0, g: 128, b: 0 }, // 17 'A' tempered
  { r: 110, g: 110, b: 255 }, // 18 'B' magic
  { r: 0, g: 252, b: 0 }, // 19 'C' set
  { r: 199, g: 179, b: 119 }, // 20 'D' unique
  { r: 240, g: 240, b: 240 }, // 21 'E'
  { r: 240, g: 240, b: 240 }, // 22 'F' rejuvenation potion
  { r: 240, g: 240, b: 240 }, // 23 'G' stamina potion
  { r: 240, g: 240, b: 240 }, // 24 'H' gold
  { r: 99, g: 99, b: 99 }, // 25 'I' ethereal
  { r: 255, g: 168, b: 0 }, // 26 'J' rune name
  { r: 99, g: 99, b: 99 }, // 27 'K' socketed
  { r: 255, g: 168, b: 0 }, // 28 'L' event item name
  { r: 154, g: 72, b: 252 }, // 29 'M' desecrated
  { r: 238, g: 214, b: 141 }, // 30 'N'
  { r: 6, g: 165, b: 221 }, // 31 'O'
  { r: 255, g: 128, b: 255 }, // 32 'P'
  { r: 170, g: 170, b: 255 }, // 33 'Q'
  { r: 0, g: 255, b: 0 }, // 34 'R'
  { r: 255, g: 255, b: 118 }, // 35 'S'
  { r: 208, g: 62, b: 62 }, // 36 'T'
  { r: 130, g: 197, b: 255 }, // 37 'U' roll ranges
  { r: 255, g: 0, b: 0 }, // 38 'V'
];

/**
 * Which game's palette a piece of text is drawn in. The two agree on 0..12 except blue, which D2R's
 * HD renderer draws as 110,110,255 (its table at PanelManager.GlobalData + 0x188, read live; the
 * legacy-graphics table keeps 1.14d's 105,105,255). 1.14d has nothing past 12; D2R has 13..38.
 */
export type TextPalette = "lod" | "d2r";

const PALETTES: Record<TextPalette, RGB[]> = {
  // 1.14d text never names 13+, but a stray one reads in D2R's colour rather than as nothing.
  lod: [...CLASSIC, ...D2R_EXTENDED],
  d2r: [
    ...CLASSIC.slice(0, 3),
    { r: 110, g: 110, b: 255 }, // 3 — D2R HD blue
    ...CLASSIC.slice(4),
    ...D2R_EXTENDED,
  ],
};

/** A palette's colours, by index. */
export function paletteColors(palette: TextPalette = "lod"): RGB[] {
  return PALETTES[palette];
}

/** Every colour a 1.14d marker can name, by index (and D2R's past 12). */
export const TextColors: RGB[] = PALETTES.lod;

/**
 * The colour index a code character names, or -1 for one that names none.
 */
export function colorIndexOf(code: string): number {
  const index = code.charCodeAt(0) - 48;
  return index >= 0 && index < TextColors.length ? index : -1;
}

/**
 * Gets the color index for a D2 color code character; an unknown one reads as white (0).
 */
export function getColorIndex(code: string): number {
  return Math.max(0, colorIndexOf(code));
}

/**
 * Gets color for a D2 color code character
 */
export function getTextColor(code: string): RGB {
  const index = getColorIndex(code);
  return index < TextColors.length ? TextColors[index] : D2Colors.White;
}
