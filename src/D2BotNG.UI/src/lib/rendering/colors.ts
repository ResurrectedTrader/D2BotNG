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

/**
 * Maps color code index to colors (ÿc0-ÿc<)
 */
export const TextColors: RGB[] = [
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
  D2Colors.DarkGreen, // :
  D2Colors.Purple, // ;
  D2Colors.BrightGreen, // <
];

/**
 * Gets the color index for a D2 color code character
 */
export function getColorIndex(code: string): number {
  if (code === ":") return 10;
  if (code === ";") return 11;
  if (code === "<") return 12;
  if (code >= "0" && code <= "9") return parseInt(code, 10);
  return 0;
}

/**
 * Gets color for a D2 color code character
 */
export function getTextColor(code: string): RGB {
  const index = getColorIndex(code);
  return index < TextColors.length ? TextColors[index] : D2Colors.White;
}
