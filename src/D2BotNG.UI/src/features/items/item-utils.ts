/**
 * Item utility functions
 *
 * Helper functions for working with D2 items.
 */

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

  // Split on the color code prefix
  const parts = text.split(COLOR_PREFIX);

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
 * Format a timestamp for display.
 */
export function formatTimestamp(
  timestamp: { seconds?: bigint; nanos?: number } | undefined,
): string {
  if (!timestamp?.seconds) return "";

  const date = new Date(Number(timestamp.seconds) * 1000);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffMins = Math.floor(diffMs / 60000);
  const diffHours = Math.floor(diffMs / 3600000);
  const diffDays = Math.floor(diffMs / 86400000);

  // Show relative time for recent items
  if (diffMins < 1) {
    return "Just now";
  } else if (diffMins < 60) {
    return `${diffMins}m ago`;
  } else if (diffHours < 24) {
    return `${diffHours}h ago`;
  } else if (diffDays < 7) {
    return `${diffDays}d ago`;
  }

  // Show absolute date for older items
  return date.toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
    year: date.getFullYear() !== now.getFullYear() ? "numeric" : undefined,
  });
}
