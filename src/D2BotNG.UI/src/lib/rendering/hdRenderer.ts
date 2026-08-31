/**
 * The D2R item art, as an alternative to the classic DC6 sprites.
 *
 * Same job as `itemRenderer`, different source and different colouring. Classic art is an indexed
 * bitmap the game recolours by swapping palette entries; D2R art is a true-colour PNG the game
 * recolours by a transform in HSV and linear-light space. Neither can be expressed as the other, so
 * this is a second decoder behind the same `ItemSprite` rather than an option on the first.
 *
 * The art is packed the way d2-planner-web packs it: nine archives of concatenated PNGs, and an
 * index giving each sprite's `[archive, offset, length]`. Nothing has to be unpacked at build time
 * — a slice of the archive IS a whole PNG file, which the browser decodes natively. That is also
 * why there is no `dc6Decoder` equivalent here: the only pixel work this file does is the tint.
 *
 * Archives are fetched whole, on demand, and kept. Each is around half a megabyte and holds a few
 * dozen sprites, so a character's gear touches two or three of them; fetching per sprite would mean
 * a range request per item and the same bytes several times over.
 */

import {
  COLOR_NAMES,
  TINT_COLOR,
  TINT_EXTRA,
  TINT_TABLE,
} from "./hdTintTables";
import type { HdAppearance } from "@/features/items/item-utils";

/** `[archive index, byte offset, byte length]` — a whole PNG inside one of the archives. */
type IndexEntry = [number, number, number];

interface HdItem {
  /** The sprite's name in the index, e.g. `armor/ancient_armor`. */
  hd: string;
  /** items.txt `invtrans`, the high half of a tint value. */
  invtrans: number;
  /** How many interchangeable graphics this item's TYPE rolls between, when it does. */
  varinvgfx?: number;
}

interface HdManifest {
  codes: Record<string, HdItem>;
  /** Per sprite: a range table row to use instead of the one its tint selects. */
  rangeOverride: Record<string, number[]>;
  /** Per `sprite:colour`: a transform row to use instead of the colour's own. */
  transformOverride: Record<string, number[]>;
}

const BASE = "/assets/rendering/hd";

let manifest: Promise<
  { index: Record<string, IndexEntry> } & HdManifest
> | null = null;
const archives = new Map<number, Promise<Uint8Array>>();

function loadManifest() {
  manifest ??= Promise.all([
    fetch(`${BASE}/hditemlib.json`).then((r) => r.json()),
    fetch(`${BASE}/hditems.json`).then((r) => r.json()),
  ]).then(([index, items]: [Record<string, IndexEntry>, HdManifest]) => ({
    index,
    ...items,
  }));
  return manifest;
}

function loadArchive(n: number): Promise<Uint8Array> {
  let pending = archives.get(n);
  if (!pending) {
    pending = fetch(`${BASE}/hditems${n}.pngx`)
      .then((r) => r.arrayBuffer())
      .then((b) => new Uint8Array(b));
    archives.set(n, pending);
  }
  return pending;
}

/** Whether this item code has D2R art at all. Answered from the manifest, so it needs it loaded. */
export async function hasHdSprite(code: string): Promise<boolean> {
  const { codes } = await loadManifest();
  return code.toLowerCase() in codes;
}

/**
 * Which sprite an item draws with.
 *
 * `gfxIndex` is the variant the item rolled, for the types that have several interchangeable
 * graphics — charms, rings, amulets, jewels. The variants are named by suffixing the base name, and
 * a type claiming variants does not guarantee the art exists (an item with its own unique sprite
 * keeps that sprite and ignores the variant system), so a missing variant falls back to the base
 * rather than to nothing.
 */
function spriteName(
  item: HdItem,
  gfxIndex: number,
  index: Record<string, IndexEntry>,
): string | null {
  if (item.varinvgfx && gfxIndex > 0) {
    const variant = `${item.hd}${gfxIndex}`.toLowerCase();
    if (variant in index) return variant;
  }
  const base = item.hd.toLowerCase();
  return base in index ? base : null;
}

/**
 * The kernel's own constants, in the precision it holds them.
 *
 * `1/3` as a double and `1/3` as a float32 differ in the last bit, and a hue that lands on a range
 * boundary is decided by exactly that bit — see the note on `tint`.
 */
const SIXTH = Math.fround(1 / 6);
const THIRD = Math.fround(1 / 3);
const TWO_THIRDS = Math.fround(2 / 3);

/** sRGB to linear light, the piecewise curve the standard defines. */
function toLinear(c: number): number {
  return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
}

function toSrgb(c: number): number {
  return c <= 0.0031308 ? c * 12.92 : 1.055 * Math.pow(c, 1 / 2.4) - 0.055;
}

/**
 * Distance around the unit circle, in single precision.
 *
 * The `fround` calls are not decoration. The game's implementation is a float32 kernel and the test
 * is a strict `<` against a span, so doubles land on the other side of a boundary constantly. A
 * pure blue has saturation exactly 1, whose distance from a 0.14 centre is exactly 0.14 in double
 * precision — and so EXCLUDED — but 0.13999998 in single, and so included. In doubles those pixels
 * kept their own colour while every neighbour recoloured.
 */
function wrapped(a: number, b: number): number {
  const d = Math.fround(Math.abs(Math.fround(a - b)));
  return Math.min(Math.fround(1 - d), d);
}

/**
 * Recolour in place.
 *
 * Two paths, chosen by the tint value's own magnitude. Below nine range-table rows it is a
 * SELECTIVE transform: pixels whose hue, saturation and value all sit inside a named range are
 * rotated and mixed toward a target colour, and everything else is left exactly as it was — which
 * is how a helm's gold trim recolours while its leather does not. At or above nine it is a flat
 * multiply in linear light over every pixel.
 *
 * Zero, anything below one full row, and row nine exactly are all "no tint": the game uses those to
 * mean the item draws in its own colours.
 *
 * This reproduces the game's kernel bit for bit, INCLUDING the parts that look like mistakes, and
 * that is the whole discipline of this function. Two of them are marked below: the hue computed as
 * a difference of quotients, and the unreduced sector. Both look like arithmetic that could be
 * simplified, and simplifying either one is visibly wrong on real artwork — the first turned Tal
 * Rasha's belt from purple to red, because D2's palettes put entire items exactly on a range
 * boundary that a strict comparison then decides differently.
 *
 * Verified against the reference implementation's own generated images, not just against synthetic
 * pixels: every opaque pixel of a tinted belt lands within 2/255.
 */
function tint(
  pixels: Uint8ClampedArray,
  value: number,
  sprite: string,
  overrides: Pick<HdManifest, "rangeOverride" | "transformOverride">,
): void {
  if (
    !value ||
    value < COLOR_NAMES.length ||
    value === COLOR_NAMES.length * 9
  ) {
    return;
  }

  const row = Math.floor(value / COLOR_NAMES.length);
  const colour = value % COLOR_NAMES.length;

  if (row >= TINT_TABLE.length) {
    const [tr, tg, tb, ta] = TINT_EXTRA[colour] ?? [1, 1, 1, 1];
    const lin = [toLinear(tr), toLinear(tg), toLinear(tb)];
    for (let i = 0; i < pixels.length; i += 4) {
      for (let c = 0; c < 3; c++) {
        const v = toLinear(pixels[i + c] / 255);
        pixels[i + c] = Math.round(
          Math.min(1, Math.max(0, toSrgb(v + ta * (v * lin[c] - v)))) * 255,
        );
      }
    }
    return;
  }

  const range = overrides.rangeOverride[sprite] ?? TINT_TABLE[row];
  const transform =
    overrides.transformOverride[`${sprite}:${COLOR_NAMES[colour]}`] ??
    TINT_COLOR[colour];
  const [hueAt, hueSpan, satAt, satSpan, valAt, valSpan] = range;
  const [tr, tg, tb, mix, hueShift, satScale, valScale] = transform;
  const target = [toLinear(tr), toLinear(tg), toLinear(tb)];

  for (let i = 0; i < pixels.length; i += 4) {
    if (pixels[i + 3] === 0) continue;

    // Single precision throughout, for the reason given on `wrapped`.
    const r = Math.fround(pixels[i] / 255);
    const g = Math.fround(pixels[i + 1] / 255);
    const b = Math.fround(pixels[i + 2] / 255);
    const max = Math.max(r, g, b);
    const delta = Math.fround(max - Math.min(r, g, b));

    // Hue as a DIFFERENCE OF TWO QUOTIENTS, which is how the game computes it, rather than as the
    // single division the algebra reduces to. The two agree to about one part in ten million and
    // disagree about which side of a boundary a value falls on — and the range test below is a
    // strict comparison. D2's art is drawn from few enough distinct colours that a whole item's
    // worth of pixels can land exactly on a span edge: a Mesh Belt's leather sits at hue 0.11
    // against a centre of 0.05 and a span of 0.06, so the game excludes it and the single-division
    // form included it. Tal Rasha's belt came out red instead of purple.
    let hue = 0;
    if (delta !== 0) {
      const half = Math.fround(delta * 0.5);
      // Multiplied by a sixth rather than divided by six, and offset by single-precision thirds,
      // because that is the arithmetic the kernel performs. Each of those is a rounding difference
      // of one unit in the last place, and one unit in the last place is what decides a boundary.
      const axis = (c: number) =>
        Math.fround(
          Math.fround(Math.fround(Math.fround(max - c) * SIXTH) + half) / delta,
        );
      const fromR = axis(r);
      const fromG = axis(g);
      const fromB = axis(b);
      if (max === r) hue = Math.fround(fromB - fromG);
      else if (max === g) hue = Math.fround(Math.fround(fromR + THIRD) - fromB);
      else hue = Math.fround(Math.fround(fromG + TWO_THIRDS) - fromR);
      if (hue < 0) hue = Math.fround(hue + 1);
      else if (hue > 1) hue = Math.fround(hue - 1);
    }
    const sat = max === 0 ? 0 : Math.fround(delta / max);

    if (
      wrapped(hue, hueAt) >= hueSpan ||
      wrapped(sat, satAt) >= satSpan ||
      wrapped(max, valAt) >= valSpan
    ) {
      continue;
    }

    // Unclamped, as the kernel leaves them: a scale past 1 is meaningful to the sector arithmetic
    // below, and clamping here changed which pixels came out grey.
    const shifted = Math.fround(hue + hueShift);
    const h = Math.fround(shifted - Math.floor(shifted));
    const s = Math.fround(sat + sat * satScale);
    const v = Math.fround(max + max * valScale);

    // Not reduced modulo 6. `h` can be exactly 1 — `fract` of a tiny negative rounds to 1 in single
    // precision — which makes this 6, matches no case below, and falls through to the last arm with
    // a fractional part of zero, so blue takes the value of `v`. Reducing it to sector 0 is the
    // "correct" HSV answer and the wrong one here: the game does this, and an item's colour is
    // whatever the game draws, not whatever the textbook says.
    const sector = Math.floor(h * 6);
    const f = h * 6 - sector;
    const p = v * (1 - s);
    const q = v * (1 - s * f);
    const t = v * (1 - s * (1 - f));
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

    // Not clamped before the conversion, only after. An unclamped `s` or `v` can push a channel
    // past 1 here, and the kernel carries that through the linear mix — clamping first capped
    // channels the game lets saturate, which is a visibly duller colour rather than a rounding
    // difference.
    for (let c = 0; c < 3; c++) {
      const lin = toLinear(rgb[c]);
      const mixed = lin + mix * (lin * target[c] - lin);
      pixels[i + c] = Math.round(Math.min(1, Math.max(0, toSrgb(mixed))) * 255);
    }
  }
}

export type { HdAppearance } from "@/features/items/item-utils";

/**
 * A D2R appearance for a source that only knows the classic palette shift.
 *
 * D2R tints by a colour NAME off the item's unique or set row, and only a v2 capture carries the
 * row index to look one up. But the shift index every source already sends indexes colors.txt, and
 * colors.txt row N holds that same colour's name — verified against all 320 unique and set rows
 * that define one, agreeing on every single one. So a mule line or a v1 character reaches the D2R
 * art from what it already sends.
 *
 * The variant graphic is not recoverable here and does not need to be: those sources send the
 * sprite name the game already resolved, and `renderHdSprite` reads the variant back out of it.
 */
export function appearanceFromShift(
  colorShift: number | undefined,
): HdAppearance {
  return {
    gfxIndex: 0,
    colorName:
      colorShift !== undefined && colorShift >= 0
        ? (COLOR_NAMES[colorShift] ?? null)
        : null,
  };
}

/**
 * One sprite's D2R pixels, tinted — and nothing else.
 *
 * Deliberately not a finished item. Sizing the canvas, centring the artwork in it, laying out the
 * socket markers and dimming an ethereal item are all the classic renderer's job, and they are the
 * same job whichever artwork is used: the two are drawn at the same scale, 30 pixels to an
 * inventory cell. So this swaps the pixels and leaves every other decision where it already was.
 * The first attempt reimplemented the compositing here, and immediately disagreed with it — a
 * runeword's runes came out stacked down the middle of the item instead of laid out in columns.
 *
 * Null when there is no D2R art for the code, which the caller treats as "use the classic sprite"
 * rather than as an error: the archives cover the base game, and a modded code has none.
 */
export async function renderHdSprite(
  code: string,
  appearance: HdAppearance,
): Promise<ImageData | null> {
  const data = await loadManifest();
  const key = code.toLowerCase();

  // A code may already have its variant baked in. The v2 capture reports the item and the graphic
  // it rolled separately, but wire schema v1 and the mule files carry the resolved sprite name the
  // game handed them — `amu2`, not `amu` plus a 2 — so a trailing digit is read back off the code
  // when the whole thing names no item. Tried in that order because real codes end in digits too:
  // `ob5` is an item, not the fifth variant of `ob`.
  const exact = data.codes[key];
  const variant = exact ? null : /^(.*?)(\d)$/.exec(key);
  const item = exact ?? (variant ? data.codes[variant[1]] : undefined);
  const gfxIndex = exact
    ? appearance.gfxIndex
    : variant
      ? Number(variant[2])
      : appearance.gfxIndex;

  // Most codes name an ITEM, which is what carries the variant list and the transform group. A few
  // name a sprite directly and have no item behind them — `gemsocket`, the marker drawn in an empty
  // socket, is one, and looking only at items found nothing and drew no empty sockets at all.
  const sprite = item
    ? spriteName(item, gfxIndex, data.index)
    : key in data.index
      ? key
      : null;
  if (!sprite) return null;

  const [archive, offset, length] = data.index[sprite];
  const bytes = await loadArchive(archive);
  const png = bytes.subarray(offset, offset + length);

  // The slice is a whole PNG, so the browser's own decoder does the work — there is no `dc6Decoder`
  // equivalent to write. `slice()` rather than a view: the Blob must own its bytes, or it aliases
  // the entire archive.
  const bitmap = await createImageBitmap(
    new Blob([png.slice().buffer as ArrayBuffer], { type: "image/png" }),
  );

  const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
  const ctx = canvas.getContext("2d");
  if (!ctx) return null;
  ctx.drawImage(bitmap, 0, 0);
  bitmap.close();

  const image = ctx.getImageData(0, 0, canvas.width, canvas.height);
  // A sprite with no item behind it has no transform group and takes no tint.
  const value = item ? tintValue(item.invtrans, appearance.colorName) : 0;
  if (value) tint(image.data, value, sprite, data);
  return image;
}

/**
 * The composite tint: the base item's transform row and the quality's colour, in one number.
 *
 * `% 10` because the row is the low digit of `invtrans` — the game packs other things above it.
 */
function tintValue(invtrans: number, colorName?: string | null): number {
  if (!colorName) return 0;
  const colour = COLOR_NAMES.indexOf(colorName as (typeof COLOR_NAMES)[number]);
  if (colour < 0) return 0;
  return (invtrans % 10) * COLOR_NAMES.length + colour;
}
