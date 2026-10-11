/**
 * The D2R item art, as an alternative to the classic DC6 sprites.
 *
 * Same job as `itemRenderer`, different source and different colouring. Classic art is an indexed
 * bitmap the game recolours by swapping palette entries; D2R art is a true-colour sprite the game
 * recolours in a pixel shader, in HSV and linear light. Neither can be expressed as the other, so
 * this is a second decoder behind the same `ItemSprite` rather than an option on the first.
 *
 * Everything here comes from the game: `scripts/generate-hd-assets.mjs` builds the archives and the
 * index from a D2R data dump (the art, which art each item draws, and the colouring tables the
 * game's `SpriteColoringHelper` loads), and `tint` is a port of the shader that applies them.
 *
 * The art is packed as archives of concatenated PNGs with an index giving each sprite's
 * `[archive, offset, length]`, so a slice of an archive IS a whole PNG file and the browser decodes
 * it natively. Archives are fetched whole, on demand, and kept: a character's gear touches a few of
 * them, and fetching per sprite would mean the same bytes several times over.
 */

import type { HdAppearance } from "@/features/items/item-utils";

/** `[archive index, byte offset, byte length]` — a whole PNG inside one of the archives. */
type IndexEntry = [number, number, number];

/** `[hueCentre, hueSpan, satCentre, satSpan, valCentre, valSpan]`. */
type Range = [number, number, number, number, number, number];
/** `[hueShift, satScale, valScale, tintR, tintG, tintB, tintAmount]`. */
type Transform = [number, number, number, number, number, number, number];

interface HdItem {
  /** The sprite drawn, as a path under the game's `hd/global/ui/items`. */
  sprite: string;
  /** items.txt `InvTrans`: which colour range class the item belongs to. */
  invTrans: number;
  /** How many interchangeable graphics (`<sprite>1`..`<sprite>N`) the item rolls between. */
  variants?: number;
}

/** The generated `hdart.json`. */
interface HdIndex {
  archives: number;
  emptySocket: string;
  /** The alpha the game draws the socket marker at. */
  socketAlpha: number;
  /** `"WxH"` (inventory cells) -> socket count -> `[cx, cy]` per socket, half cells, 1-based. */
  socketLayout: Record<string, Record<string, [number, number][]>>;
  coloring: {
    /** colors.txt codes, in index order — the order a palette shift counts in. */
    colors: string[];
    /** Per InvTrans class. */
    ranges: Range[];
    /** Per colour. */
    transforms: Transform[];
    /** Per sprite: replaces the class's range. */
    rangeOverrides: Record<string, Range>;
    /** Per `sprite:colour`: replaces the colour's transform. */
    transformOverrides: Record<string, Transform>;
  };
  items: Record<string, HdItem>;
  /** uniqueitems.txt row -> its own sprite, where it has one. */
  uniques: Record<string, string>;
  /** setitems.txt row -> its own sprite, where it has one. */
  sets: Record<string, string>;
  sprites: Record<string, IndexEntry>;
}

const BASE = "/assets/rendering/hd";

let index: Promise<HdIndex> | null = null;
const archives = new Map<number, Promise<Uint8Array>>();

function loadIndex(): Promise<HdIndex> {
  index ??= fetch(`${BASE}/hdart.json`).then((r) => r.json());
  return index;
}

function loadArchive(n: number): Promise<Uint8Array> {
  let pending = archives.get(n);
  if (!pending) {
    pending = fetch(`${BASE}/hdart${n}.pngx`)
      .then((r) => r.arrayBuffer())
      .then((b) => new Uint8Array(b));
    archives.set(n, pending);
  }
  return pending;
}

/**
 * Where the game draws an item's sockets, as pixel offsets from the item art's top-left at our 30
 * pixels to a cell, plus the marker's alpha; null for a size and count the game has no layout for.
 *
 * The game's own placement (d2r.exe `sub_1401B1710`): a half-cell grid from a table keyed by the
 * item's inventory size and socket count, the marker drawn one pixel left of its cell. Every
 * socket gets the marker, filled or not, and the filler is drawn over it at the same spot.
 */
export async function hdSocketLayout(
  count: number,
  cells: { x: number; y: number },
): Promise<{ positions: { x: number; y: number }[]; alpha: number } | null> {
  const { socketLayout, socketAlpha } = await loadIndex();
  const layout = socketLayout[`${cells.x}x${cells.y}`]?.[count];
  if (!layout) return null;
  const half = 30 / 2;
  return {
    positions: layout.map(([cx, cy]) => ({
      x: (cx - 1) * half - 1,
      y: (cy - 1) * half,
    })),
    alpha: socketAlpha,
  };
}

/** Whether this item code has D2R art at all. Answered from the index, so it needs it loaded. */
export async function hasHdSprite(code: string): Promise<boolean> {
  const { items } = await loadIndex();
  return code.toLowerCase() in items;
}

/** The appearance of an item nothing more is known about: base art, first look. */
export const PLAIN_APPEARANCE: HdAppearance = { variant: 0 };

/**
 * Which sprite an item draws with.
 *
 * A unique or set row with art of its own draws that, whatever variant it rolled — the game files
 * the art per row, and a row's art is one sprite. Otherwise the base item's, suffixed with the
 * variant for the types that roll between several (charms, rings, amulets, jewels).
 */
function spriteFor(
  item: HdItem,
  appearance: HdAppearance,
  data: HdIndex,
): string {
  const { named } = appearance;
  const own = named
    ? (named.set ? data.sets : data.uniques)[named.fileIndex]
    : undefined;
  if (own) return own;
  const { variant } = appearance;
  return item.variants && variant >= 1 && variant <= item.variants
    ? `${item.sprite}${variant}`
    : item.sprite;
}

/**
 * The tint the game would draw an item with, or null for none.
 *
 * `ITEMS_GetItemPalette` hands the draw a palette slot `class * 21 + colour`, and only when the
 * class (items.txt InvTrans) is 1..8 but not 3 or 4 — gold and brown have no slot — and the colour
 * is a real one. No slot, no tint at all: the overrides below are not consulted either.
 * `sub_1400921A0` then reads the slot's two halves separately: the class picks a range (unless the
 * sprite overrides it) and the colour a transform (unless the sprite overrides it for that colour).
 */
function tintFor(
  item: HdItem,
  sprite: string,
  colour: number,
  { coloring }: HdIndex,
): { range: Range; transform: Transform } | null {
  const cls = item.invTrans;
  if (cls < 1 || cls > 8 || cls === 3 || cls === 4) return null;
  if (!(colour >= 0 && colour < coloring.transforms.length)) return null;
  return {
    range: coloring.rangeOverrides[sprite] ?? coloring.ranges[cls],
    transform:
      coloring.transformOverrides[`${sprite}:${colour}`] ??
      coloring.transforms[colour],
  };
}

/**
 * The kernel's own constants, in the precision it holds them: the shader multiplies by a sixth and
 * offsets by thirds, as float32 literals, rather than dividing.
 */
const SIXTH = Math.fround(1 / 6);
const THIRD = Math.fround(1 / 3);
const TWO_THIRDS = Math.fround(2 / 3);

/**
 * Single-precision rounding, applied after EVERY operation below — not as decoration. This file
 * and `HdSpriteSource.cs` must produce the same pixels, and C#'s `float` rounds each operation;
 * an unrounded intermediate here (`1 - s * f` computed in double) disagreed with it on a quarter
 * of all tinted sprites. Powers are the one exception both sides share: computed in double, then
 * rounded, since neither runtime's single-precision pow is specified to the last bit.
 */
const f32 = Math.fround;
const LIN_KNEE = f32(0.04045);
const SRGB_KNEE = f32(0.003131);
const SLOPE = f32(12.92);
const SCALE = f32(1.055);
const OFFSET = f32(0.055);
const GAMMA = f32(2.4);
const INV_GAMMA = f32(1 / GAMMA);

/** sRGB to linear light, as the shader writes it: the piecewise curve over `|c|`. */
function toLinear(c: number): number {
  return c < LIN_KNEE
    ? f32(c / SLOPE)
    : f32(Math.pow(f32(Math.abs(f32(c + OFFSET)) / SCALE), GAMMA));
}

/** Linear light to sRGB, as the shader's output stage writes it (at the default gamma of 2.4). */
function toSrgb(c: number): number {
  return c < SRGB_KNEE
    ? f32(c * SLOPE)
    : f32(f32(SCALE * f32(Math.pow(Math.abs(c), INV_GAMMA))) - OFFSET);
}

/**
 * Distance around the unit circle, in single precision: `min(|d|, 1 - |d|)`. The shader measures
 * all three of hue, saturation and value this way — not only hue.
 */
function wrapped(a: number, b: number): number {
  const d = Math.fround(Math.abs(Math.fround(a - b)));
  return Math.min(Math.fround(1 - d), d);
}

/**
 * Recolour in place: a port of D2R's UI pixel shader in its item-tint mode (`hd/shaders/ps_ui_p.bin`,
 * UI_DRAW_MODE 4).
 *
 * A pixel whose hue, saturation and value all sit strictly inside the range is rotated in hue,
 * scaled in saturation and value, converted to linear light and mixed toward the tint colour by
 * the tint amount; every other pixel keeps its own colour. That is how a helm's trim recolours
 * while its leather does not.
 *
 * Bit-faithful where it matters, in single precision throughout: the range test is a strict `<`,
 * and the art is drawn from few enough distinct colours that whole items sit exactly on a range
 * boundary, which float64 arithmetic decides the other way. Two things that look like mistakes are
 * the shader's own and are kept:
 *
 *  - hue is a DIFFERENCE OF TWO QUOTIENTS, not the single division it reduces to — the two disagree
 *    in the last bit, which is the bit a boundary is decided by;
 *  - the sector is not reduced modulo 6. `frac` of a tiny negative rounds to exactly 1 in single
 *    precision, which makes the sector 6, matches no case, and falls through to the last arm.
 *
 * Scaled saturation and value are not clamped, and neither is the mix: the shader carries a channel
 * past 1 into the linear mix, and clamping earlier is a visibly duller colour.
 */
function tint(
  pixels: Uint8ClampedArray,
  range: Range,
  transform: Transform,
): void {
  const [hueAt, hueSpan, satAt, satSpan, valAt, valSpan] = range.map(
    Math.fround,
  );
  const [hueShift, satScale, valScale, tr, tg, tb, amount] = transform.map(
    Math.fround,
  );
  const target = [toLinear(tr), toLinear(tg), toLinear(tb)];

  for (let i = 0; i < pixels.length; i += 4) {
    if (pixels[i + 3] === 0) continue;

    const r = Math.fround(pixels[i] / 255);
    const g = Math.fround(pixels[i + 1] / 255);
    const b = Math.fround(pixels[i + 2] / 255);
    const max = Math.max(r, g, b);
    const delta = Math.fround(max - Math.min(r, g, b));

    let hue = 0;
    let sat = 0;
    if (delta !== 0) {
      sat = Math.fround(delta / max);
      const half = Math.fround(delta * 0.5);
      const axis = (c: number) =>
        Math.fround(
          Math.fround(Math.fround(Math.fround(max - c) * SIXTH) + half) / delta,
        );
      const fromR = axis(r);
      const fromG = axis(g);
      const fromB = axis(b);
      if (r === max) hue = Math.fround(fromB - fromG);
      else if (g === max) hue = Math.fround(Math.fround(fromR + THIRD) - fromB);
      else hue = Math.fround(Math.fround(fromG + TWO_THIRDS) - fromR);
      if (hue < 0) hue = Math.fround(hue + 1);
      if (hue > 1) hue = Math.fround(hue - 1);
    }

    if (
      !(wrapped(hue, hueAt) < hueSpan) ||
      !(wrapped(sat, satAt) < satSpan) ||
      !(wrapped(max, valAt) < valSpan)
    ) {
      continue;
    }

    const shifted = f32(hue + hueShift);
    const h = f32(shifted - Math.floor(shifted));
    const s = f32(f32(sat * satScale) + sat);
    const v = f32(f32(max * valScale) + max);

    const h6 = f32(h * 6);
    const sector = Math.floor(h6);
    const f = f32(h6 - sector);
    const p = f32(v * f32(1 - s));
    const q = f32(v * f32(1 - f32(s * f)));
    const t = f32(v * f32(1 - f32(s * f32(1 - f))));
    const rgb =
      s === 0
        ? [v, v, v]
        : sector === 0
          ? [v, t, p]
          : sector === 1
            ? [q, v, p]
            : sector === 2
              ? [p, v, t]
              : sector === 3
                ? [p, q, v]
                : sector === 4
                  ? [t, p, v]
                  : [v, p, q];

    for (let c = 0; c < 3; c++) {
      const lin = toLinear(rgb[c]);
      const mixed = f32(lin + f32(amount * f32(f32(lin * target[c]) - lin)));
      // Halves round up — `MidpointRounding.AwayFromZero` on the C# side, never its default.
      pixels[i + c] = Math.round(
        f32(Math.min(1, Math.max(0, toSrgb(mixed))) * 255),
      );
    }
  }
}

export type { HdAppearance } from "@/features/items/item-utils";

/**
 * One sprite's D2R pixels, tinted — and nothing else.
 *
 * Deliberately not a finished item. Sizing the canvas, centring the artwork in it, laying out the
 * socket markers and dimming an ethereal item are all the classic renderer's job, and they are the
 * same job whichever artwork is used: the two are drawn at the same scale, 30 pixels to an
 * inventory cell. So this swaps the pixels and leaves every other decision where it already was.
 *
 * `colour` is the item's palette shift — a colors.txt index, which is exactly the colour half of
 * the game's palette slot — or -1 for none. Every source already carries it, so mule lines, v1
 * characters and v2 captures all tint the same way.
 *
 * Null when there is no D2R art for the code, which the caller treats as "use the classic sprite"
 * rather than as an error.
 */
export async function renderHdSprite(
  code: string,
  appearance: HdAppearance,
  colour: number,
): Promise<ImageData | null> {
  const data = await loadIndex();
  // The item's own code when the source knows it: `code` is the classic sprite name, which for a
  // unique or set is that row's classic art and names no D2R item.
  const key = (appearance.code ?? code).toLowerCase();

  // A code may already have its variant baked in. The v2 capture reports the item and the graphic
  // it rolled separately, but wire schema v1 and the mule files carry the resolved classic sprite
  // name — `amu2`, not `amu` plus a variant — whose digit is the same 1-based number the D2R art
  // is suffixed with. Read back off the code only when the whole thing names no item, because real
  // codes end in digits too: `ob5` is an item, not the fifth variant of `ob`.
  let item = data.items[key];
  let look = appearance;
  if (!item) {
    const baked = /^(.*?)(\d)$/.exec(key);
    if (baked && data.items[baked[1]]) {
      item = data.items[baked[1]];
      look = { ...appearance, variant: Number(baked[2]) };
    }
  }

  // A few codes name a sprite rather than an item — the empty-socket marker.
  const sprite = item
    ? spriteFor(item, look, data)
    : key === "gemsocket"
      ? data.emptySocket
      : null;
  if (!sprite || !(sprite in data.sprites)) return null;

  const [archive, offset, length] = data.sprites[sprite];
  const bytes = await loadArchive(archive);
  const image = await decodePng(bytes.subarray(offset, offset + length));
  const tinted = item ? tintFor(item, sprite, colour, data) : null;
  if (tinted) tint(image.data, tinted.range, tinted.transform);
  return image;
}

/**
 * One of the generator's PNGs (8-bit RGBA, not interlaced) to pixels, decoded here rather than by
 * the browser.
 *
 * Not for want of a decoder: a 2D canvas stores PREMULTIPLIED 8-bit colour, so drawing an image and
 * reading it back rounds every semi-transparent pixel's colour before the tint sees it — and the
 * tint's range test is a strict comparison. Decoding the file directly hands the kernel the same
 * bytes `HdSpriteSource` gets, and runs unchanged outside a browser.
 */
async function decodePng(png: Uint8Array): Promise<ImageData> {
  const view = new DataView(png.buffer, png.byteOffset, png.byteLength);
  const width = view.getUint32(16);
  const height = view.getUint32(20);
  if (png[24] !== 8 || png[25] !== 6 || png[28] !== 0) {
    throw new Error("D2R sprite is not an 8-bit RGBA, non-interlaced PNG");
  }

  const idat: Uint8Array[] = [];
  for (let at = 8; at < png.length; ) {
    const length = view.getUint32(at);
    const type = String.fromCharCode(...png.subarray(at + 4, at + 8));
    if (type === "IDAT") idat.push(png.subarray(at + 8, at + 8 + length));
    at += 12 + length;
  }
  const inflated = new Uint8Array(
    await new Response(
      new Blob(idat as BlobPart[])
        .stream()
        .pipeThrough(new DecompressionStream("deflate")),
    ).arrayBuffer(),
  );

  // Undo the per-row filters (PNG spec, section 9), four bytes to a pixel.
  const stride = width * 4;
  const pixels = new Uint8ClampedArray(stride * height);
  for (let y = 0; y < height; y++) {
    const filter = inflated[y * (stride + 1)];
    const src = y * (stride + 1) + 1;
    const row = y * stride;
    for (let x = 0; x < stride; x++) {
      const a = x >= 4 ? pixels[row + x - 4] : 0;
      const b = y > 0 ? pixels[row - stride + x] : 0;
      const c = x >= 4 && y > 0 ? pixels[row - stride + x - 4] : 0;
      const raw = inflated[src + x];
      let predicted = 0;
      if (filter === 1) predicted = a;
      else if (filter === 2) predicted = b;
      else if (filter === 3) predicted = (a + b) >> 1;
      else if (filter === 4) {
        const p = a + b - c;
        const pa = Math.abs(p - a);
        const pb = Math.abs(p - b);
        const pc = Math.abs(p - c);
        predicted = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      }
      pixels[row + x] = (raw + predicted) & 0xff;
    }
  }
  return new ImageData(pixels, width, height);
}
