/**
 * The game's own text palette, and the shapes everything that renders an item agrees on.
 *
 * The colour tables live here rather than in a renderer because three different sources feed the
 * same components — mule files, v1 character items, v2 captures — and each states colour
 * differently: as a ÿcN marker in text, or as an index the game resolved.
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
 * Handles: \xffc0 through \xffc9, \xffc:, \xffc;, \xffc<
 *
 * `initialColor` is the colour in force before the first marker. It defaults to white, which is
 * right for a whole description — but a single tooltip ROW arrives already anchored to a colour the
 * renderer knows, and starting that row at white would repaint its leading text.
 */
export function parseD2ColoredText(
  text: string,
  initialColor: string = DEFAULT_COLOR,
): ColoredTextSegment[] {
  const segments: ColoredTextSegment[] = [];
  let currentColor = initialColor;

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

  // If no segments created and text has no color codes, return original text.
  // Test `normalized` (not the raw `text`): a line that is *only* a native color
  // marker like "ÿc0" normalizes to the escaped prefix and is fully consumed by
  // the split above, leaving no segments. Checking the raw text here would miss
  // the native form and wrongly re-emit the literal "ÿc0" as visible text.
  if (segments.length === 0 && text && !normalized.includes(COLOR_PREFIX)) {
    segments.push({ text, color: initialColor });
  }

  return segments;
}

/**
 * A marker in either spelling, plus the one character that names the colour.
 *
 * The trailing `?` is for a marker that runs off the end of the line: it names nothing, and the
 * parser drops it rather than showing it, so stripping has to agree. The character class is
 * deliberately anything rather than the known codes — an unrecognised code is still consumed as
 * part of the marker, not printed — but never a newline, which no code is: a marker sitting at the
 * end of a line would otherwise eat the break and run the next line onto it.
 */
const COLOR_MARKER = /(?:ÿc|\\xffc)[^\n]?/g;

/**
 * Strip D2 color codes from text, returning plain text.
 *
 * A replace rather than a parse-and-rejoin: the result is the same text either way, so building a
 * segment array only to throw the colours away is a pass spent for nothing — and this runs per
 * line over whole descriptions.
 */
export function stripD2ColorCodes(text: string): string {
  return text.replace(COLOR_MARKER, "");
}

/**
 * Everything the sprite and tooltip components read off an item.
 *
 * Stated structurally rather than as a message type because three different shapes feed the same
 * renderer: mule-file items, streamed character items, and wire schema v2 captures whose colour
 * and sprite are resolved from the game's tables rather than sent. All three satisfy this; none
 * needs to be converted into another.
 */
/** The two things D2R art needs that its own item code does not carry. */
export interface HdAppearance {
  /** `gfx_index` — which of an item type's interchangeable graphics this one rolled. */
  gfxIndex: number;
  /** The `invtransform` colour name from the unique or set row; null for an untinted item. */
  colorName: string | null;
}

export interface RenderableItem {
  code: string;
  name: string;
  header: string;
  /**
   * The tooltip text, for a source that HAS one: a mule line, a v1 character item. Empty when the
   * source renders instead — see `describe`.
   */
  description: string;
  /**
   * The tooltip text on demand, for a source that has to build it.
   *
   * A v2 capture carries no tooltip at all — the producer stopped sending the game's string — so
   * its text is rendered from the item's own fields, and rendering a whole stash page to draw a
   * grid would be wasteful when only the hovered item is ever read. Anything that DISPLAYS the
   * tooltip prefers this; the cheap readers of `description` (ethereal detection) do not, and must
   * not, since they run per cell.
   */
  describe?: () => string;
  itemColor: number;
  invTrans: number;
  /**
   * What the D2R art path needs on top of the code, when the source can supply it.
   *
   * Absent for a mule line or a v1 character item, and that is not an oversight: D2R art is tinted
   * by the `invtransform` NAME on the item's uniqueitems or setitems row, and v1 sends a classic
   * palette-shift index instead — a resolved answer in the other scheme, which does not convert.
   * Those items render D2R base art untinted, or classic art in full, which is why the style is a
   * per-item fallback rather than a mode.
   */
  hd?: HdAppearance;
  /** Set when the source knows outright, rather than leaving it to the description. */
  ethereal?: boolean;
  sockets: RenderableItem[];
  /**
   * A richer view of the same item, when the source can produce one on demand. Absent for a mule
   * item or a v1 character item, which arrive as finished text with nothing left to re-derive.
   */
  detail?: ItemDetail;
}

/**
 * The alternate tooltip, computed only when someone asks for it.
 *
 * A function rather than data because producing it costs a pass over the item's stats against the
 * game tables, and almost no hovered item is ever inspected this way. It stays behind this
 * interface — rather than the tooltip taking a captured unit and a tables handle — so the renderer
 * keeps working for the three shapes that feed it and never learns about any of them.
 */
export interface ItemDetail {
  /** Shown in place of the description, one entry per row the game would have drawn. */
  lines(): DetailLine[];
}

/**
 * One rendered row, already split into coloured runs.
 *
 * Structured rather than a ÿcN string, because the marker format is a TRANSPORT: a source that has
 * the colour as a number should not encode it into text for the renderer to parse back out. It
 * also fixes a real defect — the game's own text relies on each row carrying its own terminator,
 * and some rows (a socket block's heading) carry none, so splitting the joined string ran two rows
 * together. One entry per row cannot.
 *
 * SEGMENTS rather than one colour for the row, because a row is not always one colour. The row has
 * an anchor colour, and its text can re-anchor part way through: the game embeds a marker of its
 * own in some lines (`ÿc0ÿc0Chance to Block:`), and a roll-range annotation is painted grey and
 * then restores the line's colour after itself. Painting the row in its anchor colour alone drew
 * every one of those markers as literal glyphs.
 */
export interface DetailLine {
  /** Empty means a deliberate blank row — including a row that is only a bare colour marker. */
  segments: ColoredTextSegment[];
}

/** The hex for a D2 colour INDEX, as opposed to the ÿcN character `parseD2ColoredText` reads. */
export function colorForIndex(index: number): string {
  return D2_TEXT_COLORS[String.fromCharCode(48 + index)] ?? DEFAULT_COLOR;
}

/**
 * The colour index the game draws each `dwQualityNo` in — the library's `ItemTooltipColor`, keyed
 * by quality.
 *
 * Restated here rather than imported, because the toolkit is the app's only dynamic import (its
 * embedded tables are a ~735KB blob) and a static import for nine constants would pull it into the
 * initial chunk. Inferior and superior are white and grey rather than a quality colour of their
 * own; a normal item is white.
 */
const QUALITY_COLOR_INDEX: Record<number, number> = {
  1: 5, // inferior — grey
  2: 0, // normal
  3: 0, // superior
  4: 3, // magic — blue
  5: 2, // set — green
  6: 9, // rare — yellow
  7: 4, // unique — gold
  8: 8, // crafted — orange
  9: 10, // tempered
};

/**
 * The colour a `dwQualityNo` is drawn in.
 *
 * For UI that labels an item by quality without rendering its tooltip — a picker, a legend. It
 * exists so those cannot drift from the tooltip: both end up in `D2_TEXT_COLORS`, which is the
 * game's own text palette, rather than one of them reaching for a near-miss from the app's theme.
 */
export function colorForQuality(quality: number): string {
  return colorForIndex(QUALITY_COLOR_INDEX[quality] ?? 0);
}

/**
 * Whether an item should render as ethereal (semi-transparent).
 *
 * The flag is believed when set, and otherwise the description is searched — the raw game tooltip
 * ("Ethereal …") and the mule-file marker (":eth"). Deliberately an OR and not a preference: the
 * flag is a plain proto3 bool, so it reads as `false` on every mule item, which carries the fact
 * only in its text. Treating `false` as an answer would silently stop those rendering ethereal.
 *
 * This runs per CELL, which is why it reads `description` and never `describe()`: a v2 capture has
 * no text to search but states the flag outright, so asking it to render one would render a whole
 * stash page to draw the grid and learn nothing the flag had not already said.
 */
export function isEthereal(item: RenderableItem): boolean {
  if (item.ethereal === true) return true;
  const desc = item.description ?? "";
  return desc.includes("Ethereal") || desc.includes(":eth");
}
