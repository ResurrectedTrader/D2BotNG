/**
 * Item utility functions
 *
 * Helper functions for working with D2 items.
 */

import type { Item } from "@/generated/items_pb";

/**
 * D2 text color codes used in item descriptions.
 * These match the reference implementation's TextColors array.
 */
const D2_TEXT_COLORS: Record<string, string> = {
  "0": "#ffffff", // White
  "1": "#ff4d4d", // Red
  "2": "#00ff00", // Green
  "3": "#6969ff", // Blue
  "4": "#c7b377", // Gold/Tan
  "5": "#696969", // Gray
  "6": "#000000", // Black
  "7": "#d0c27d", // Light gold
  "8": "#ffa800", // Orange
  "9": "#ffff64", // Yellow
  ":": "#008000", // Dark green
  ";": "#ae00ff", // Purple
  "<": "#00c800", // Bright green
};

const DEFAULT_COLOR = "#ffffff";

/** A colored text segment */
export interface ColoredTextSegment {
  text: string;
  color: string;
}

/** Color code prefix - literal backslash-xffc */
const COLOR_PREFIX = String.raw`\xffc`;

/**
 * Parse D2 color-coded text into segments with colors.
 * Uses simple string splitting to avoid regex hex escape issues.
 * Handles: \xffc0 through \xffc9, \xffc:, \xffc;, \xffc<
 */
export function parseD2ColoredText(text: string): ColoredTextSegment[] {
  const segments: ColoredTextSegment[] = [];
  let currentColor = DEFAULT_COLOR;

  // d2bs sends the raw game tooltip, where the color marker is the native byte
  // U+00FF ("ÿc<code>"); mule files use the escaped "\xffc<code>" form. Normalize
  // the native form to the escaped one so the single split below handles both.
  const normalized = text.replace(/ÿc/g, COLOR_PREFIX);

  // Split on the color code prefix
  const parts = normalized.split(COLOR_PREFIX);

  for (let i = 0; i < parts.length; i++) {
    const part = parts[i];

    if (i === 0) {
      // First part has no color code prefix
      if (part) {
        segments.push({ text: part, color: currentColor });
      }
      continue;
    }

    // First character is the color code
    if (part.length > 0) {
      const colorCode = part[0];
      if (colorCode in D2_TEXT_COLORS) {
        currentColor = D2_TEXT_COLORS[colorCode];
      }
      // Rest of the part is the text
      const textContent = part.slice(1);
      if (textContent) {
        segments.push({ text: textContent, color: currentColor });
      }
    }
  }

  // If no segments created and text has no color codes, return original text
  if (segments.length === 0 && text && !text.includes(COLOR_PREFIX)) {
    segments.push({ text, color: DEFAULT_COLOR });
  }

  return segments;
}

/**
 * Strip D2 color codes from text, returning plain text.
 */
export function stripD2ColorCodes(text: string): string {
  return parseD2ColoredText(text)
    .map((s) => s.text)
    .join("");
}

/**
 * Whether an item should render as ethereal (semi-transparent). Detected from the
 * description text — both the raw game tooltip ("Ethereal …") and the mule-file
 * marker (":eth") — so live character items and logged mule items behave the same.
 */
export function isEthereal(item: Item): boolean {
  const desc = item.description ?? "";
  return desc.includes("Ethereal") || desc.includes(":eth");
}
