/**
 * Projecting a captured unit into something renderable.
 *
 * Shared by the v2 character view and item search, because both face the same gap: v2 stores the
 * raw `D2UnitStrc` capture rather than the presentation the game resolved from it, so the sprite
 * name, the palette shift and the transform table come from the game's own tables here.
 */

import type { TooltipEngine } from "d2itemtoolkit";
import type {
  Container as CapturedContainer,
  Unit,
} from "@/generated/captures_pb";
import {
  colorForIndex,
  parseD2ColoredText,
  type ColoredTextSegment,
  type HdAppearance,
} from "@/features/items/item-utils";
import { toToolkitUnit } from "./toolkitUnit";
import type { DisplayContainer, DisplayItem } from "./contracts";

/** `dwFlags` bit 22. v2 carries the fact outright instead of v1's bool. */
export const ETHEREAL_FLAG = 0x400000;
/** `dwFlags` bit 26 — set on a runeword, which is what makes `magic_prefix[0]` a runes.txt id. */
export const RUNEWORD_FLAG = 0x4000000;
/** `dwFlags` bit 4. */
export const IDENTIFIED_FLAG = 0x10;
/** `dwFlags` bit 11. */
export const SOCKETED_FLAG = 0x800;

/**
 * ItemStatCost row 194, `item_numsockets` — how many sockets the item HAS.
 *
 * A capture nests only the sockets that are filled, so an empty one is not a missing item but an
 * absent array element, and the count is the only thing that says how many there were. It rides
 * the base list (`STATLIST_EXTENDED`, state 0) on every item flagged socketed, but the stat is
 * looked for across all of them rather than by list, since which list carries it is the game's
 * business and not a fact this projection should depend on.
 */
const STAT_NUM_SOCKETS = 194;

/**
 * The sprite the game draws in an empty socket — an item code with no item behind it.
 *
 * It is the convention the renderer already speaks: v1 arrives with these standing in for the
 * empty sockets, and the sprite and PNG paths both key their translucent-overlay handling on this
 * exact code. v2 sends no such placeholder, so the empty sockets simply went unrendered until it
 * followed the same convention.
 */
const EMPTY_SOCKET_CODE = "gemsocket";

/** `dwQualityNo` for the two qualities whose row carries an inventory transform. */
const QUALITY_SET = 5;
const QUALITY_UNIQUE = 7;

/**
 * What the D2R art needs beyond the item code: which graphic it rolled, and what to tint it.
 *
 * The colour is the `invtransform` NAME — "lred", "dgld" — off the item's own uniqueitems or
 * setitems row, which is the same column the classic path resolves through colors.txt into a
 * palette index. D2R indexes it by name instead, so this reads the raw cell rather than the
 * resolved shift; there is no converting one into the other.
 *
 * `file_index` selects that row and is overloaded by quality, so only these two qualities can name
 * one. Everything else draws in its own colours, which is what the game does too.
 */
function toHdAppearance(unit: Unit, engine: TooltipEngine): HdAppearance {
  const table =
    unit.quality === QUALITY_UNIQUE
      ? engine.data.uniqueItems
      : unit.quality === QUALITY_SET
        ? engine.data.setItems
        : null;

  const colorName =
    table && unit.fileIndex >= 0 && unit.fileIndex < table.rowCount
      ? table.getString(unit.fileIndex, "invtransform") || null
      : null;

  return { gfxIndex: unit.gfxIndex, colorName };
}

/**
 * The item's socket count, or 0 for one that has none.
 *
 * Gated on the SOCKETED flag rather than taken from the stat alone, because the stat alone is not
 * the game's own answer. Two of Tal Rasha's set pieces — the Horadric Crest and the Guardianship —
 * carry `item_numsockets = 1` with the flag clear and nothing socketed into them, and reading the
 * stat by itself drew an empty socket on a helm and an armour that plainly have none. Across a live
 * store the flag never disagrees with reality: 11 items set it and every one has the stat, 175
 * clear it and not one has a filler.
 */
function socketCount(unit: Unit): number {
  if ((unit.itemFlags & SOCKETED_FLAG) === 0) return 0;

  let count = 0;
  for (const list of unit.statsLists) {
    for (const stat of list.stats) {
      if (stat.id === STAT_NUM_SOCKETS) {
        count = Math.max(count, Number(stat.value));
      }
    }
  }
  return count;
}

/**
 * The socket row as it is drawn: the fillers, then a placeholder for each socket still open.
 *
 * Appended rather than slotted in, because the capture sends fillers contiguously from socket 0 —
 * the array position IS the socket index — so everything after the last filler is empty. A count
 * that disagrees with what arrived is trusted downwards only: fewer placeholders than sockets is a
 * drawing that understates, more than the sprite has positions for is one the renderer would drop
 * anyway.
 */
function socketsOf(
  unit: Unit,
  engine: TooltipEngine,
  viewers: ItemViewers,
): DisplayItem[] {
  const filled = unit.sockets.map((s) => toDisplayItem(s, engine, viewers));
  const empty = socketCount(unit) - filled.length;
  for (let i = 0; i < empty; i++) filled.push(emptySocket());
  return filled;
}

/**
 * A socket with nothing in it. Only the fields the sprite reads mean anything — the rest exist
 * because this stands in an item list, and it is never hovered, keyed or placed: the tooltip and
 * the context menu belong to the item it sits in.
 */
function emptySocket(): DisplayItem {
  return {
    code: EMPTY_SOCKET_CODE,
    name: "",
    header: "",
    description: "",
    // -1 is "no palette shift" — the renderer draws the socket art in its own colours.
    itemColor: -1,
    invTrans: 0,
    sockets: [],
    x: 0,
    y: 0,
    width: 1,
    height: 1,
    gid: 0,
  };
}

/**
 * The held-Ctrl view: the same item, rendered with what the game never shows.
 *
 * Two departures, both the library's own rather than anything reconstructed here. `sockets:
 * 'separated'` draws the item WITHOUT its fillers and then one block per gem or rune below it, so a
 * reader can tell which one is responsible for what; the game always merges them. `ranges` annotates
 * each stat line with the span it could have rolled within, from the tables the item's own record
 * points at.
 *
 * They compose deliberately: with the sockets separated, each filler's spans land against ITS lines
 * rather than against a merged total, so "Fire Resist +28% [11-20]" cannot happen — where 28 was
 * item plus jewel and 11-20 was the item alone.
 *
 * A closure rather than precomputed data: this costs a pass over the item's stats against the game
 * tables, and it is wanted for the one item under the pointer, never for the thousand in a stash.
 *
 * The wearer matters for more than the level line here. It is what the library reads to decide
 * which set tiers are EARNED, so a set piece's range annotation reflects the set actually worn. A
 * search result has no wearer, and correctly gets the item's own spans only.
 */
function toDetail(unit: Unit, engine: TooltipEngine, viewers: ItemViewers) {
  // Computed on first ask and then kept. Lazy because almost no hovered item is ever inspected
  // this way; CACHED because the alternate view is toggled — releasing Ctrl throws the rendered
  // rows away, and without this every press paid for the render again, which for an item whose
  // roll ranges have to be reconstructed from the affix tables is long enough to see.
  let rows: RenderedLine[] | null = null;
  return {
    lines: (): RenderedLine[] =>
      (rows ??= renderLines(unit, engine, viewers, { breakdown: true })),
  };
}

/**
 * One rendered row, keeping the stat identity the library puts on each line.
 *
 * `statIds` is every stat the row displays a number for, empty on anything that displays none — a
 * name, a requirement, a blank. With `layer` it is the key into `ItemRollRanges.stats`, and it is
 * what lets a reader click a modifier on a result and sort by exactly that.
 *
 * All of them, not just the first, because a row is often not one stat: "Adds 1-4 Cold Damage" is
 * mindam and maxdam on one line and "+2 to All Attributes" is a DescGrp standing for four. Ranking
 * by the first alone would order an all-attributes line by strength — right by luck here, since the
 * numbers agree, and wrong the moment they do not. The store's MAX over the set is what the line
 * actually says.
 */
export interface RenderedLine {
  /**
   * Which of the game's tooltip writers produced the row, verbatim from the library.
   *
   * Carried because a row is not always wanted where the tooltip wants it: a search result has no
   * character, so the set-piece list and the set bonuses are about a wearer nobody named.
   */
  section: string;
  /**
   * The row split at its embedded colour markers, anchored at the colour the library assigned it.
   * Empty means a blank row — which includes a row whose whole content was a bare marker.
   */
  segments: ColoredTextSegment[];
  /** The same row as plain text, markers removed. For labelling and for blank detection. */
  text: string;
  statIds: number[];
  layer: number;
}

/**
 * The one way this app departs from the game's own tooltip, as a question rather than as knobs.
 *
 * Deliberately narrower than `TooltipOptions`: a render here is either the game's text or the
 * held-Ctrl breakdown, so a caller picks the view rather than assembling one and a half-configured
 * combination cannot be expressed.
 *
 * There used to be a second question — "what is this WORTH", for the worn set piece whose socket
 * fillers the game drops (`ITEM_RecalcAllEquippedItems` rebuilds an equipped set item's stat list
 * with set state alone). Toolkit 0.4.0 removed the choice: render, merged stats and filler stats
 * all count the fillers unconditionally, because the game's own answer is not even stable — the
 * mods come back when the piece is re-socketed or re-equipped and go again on the next recalc, so
 * the same equipped helm draws +15 and +30 in different snapshots of one session.
 */
export interface RenderOptions {
  /** The held-Ctrl view: fillers in their own blocks, every stat annotated with its roll span. */
  breakdown?: boolean;
}

/**
 * The tooltip as ROWS, rather than as one string to be split apart again.
 *
 * This is not a preference. The game's text relies on each row carrying its own terminator, and
 * some rows carry none — a socket block's heading is `"Ko Rune"` with no newline, so the joined
 * string runs it into the modifier beneath it. One entry per row cannot do that, the blank
 * separators between filler blocks arrive as rows rather than as artefacts to be trimmed around,
 * and the colour comes as a number instead of a marker the renderer has to parse back out.
 *
 * The line's own text already carries the roll-range annotation when it was asked for; nothing is
 * spliced in later, so rendering from rows loses none of it.
 */
export function renderLines(
  unit: Unit,
  engine: TooltipEngine,
  viewers: ItemViewers,
  options: RenderOptions = {},
): RenderedLine[] {
  const tooltip = engine.render(
    toToolkitUnit(unit),
    viewers.wearer ? toToolkitUnit(viewers.wearer) : null,
    {
      // `ranges: {}` is presence-as-switch with the library's default format and colour, and
      // `[ilvl 67]` after the name belongs to the same view: item level is what decides which
      // affixes an item could have rolled, so it answers the question the spans are being read
      // for. The game draws no such line, which is why it appears here and nowhere else.
      ...(options.breakdown
        ? { sockets: "separated" as const, ranges: {}, showItemLevel: true }
        : {}),
      ...clientPlayerOption(viewers),
    },
  );

  const rows = tooltip.lines.map((line) => {
    // The terminator is the row boundary, not content.
    const raw = (line.text ?? "").replace(/\n+$/, "");
    // `line.color` is only the colour the row STARTS in. Its text can carry markers of its own —
    // the game embeds them, and a range annotation is painted grey and then restores the line's
    // colour behind itself — so the row is split here rather than painted flat.
    const segments = parseD2ColoredText(raw, colorForIndex(line.color));
    return {
      section: String(line.section),
      segments,
      text: segments.map((s) => s.text).join(""),
      // `shownStats` is null on a line that speaks for the one stat, so it falls back to that; -1
      // means the line displays no stat at all and yields nothing to rank by.
      statIds: line.shownStats ?? (line.statId >= 0 ? [line.statId] : []),
      layer: line.layer,
    };
  });

  return trimBlankRows(rows);
}

/**
 * Which damage line is which, in the order the tooltip draws them.
 *
 * The tooltip labels every damage row `WeaponDamage` and attaches no stat id to any of them, so a
 * rendered line does not say whether it is the one-hand, two-hand or throw one — and they rank on
 * different columns, because a two-handed weapon has no one-hand line at all. The library states
 * that `damage().lines` is exactly what `render` puts in that section, in display order, so the
 * Nth entry here IS the Nth `WeaponDamage` row on screen.
 *
 * Empty for anything that draws no damage line, which is everything that is not a weapon.
 */
export function damageKinds(unit: Unit, engine: TooltipEngine): string[] {
  try {
    return engine
      .damage(toToolkitUnit(unit))
      .lines.map((line) => String(line.kind));
  } catch {
    return [];
  }
}

/**
 * Drop the blank rows at both ENDS, keeping the ones between — those are the layout, and the game
 * separates its blocks with them.
 *
 * Shared with anything that filters rows OUT of a rendered tooltip, because removing a block
 * re-exposes the blank that introduced it: a set item whose wearer-specific block is dropped ends
 * on a gap, which reads as a rendering fault rather than as a deliberate omission.
 */
export function trimBlankRows(rows: RenderedLine[]): RenderedLine[] {
  let start = 0;
  let end = rows.length;
  while (start < end && rows[start].text.trim() === "") start++;
  while (end > start && rows[end - 1].text.trim() === "") end--;
  return rows.slice(start, end);
}

/**
 * The two units a tooltip can read.
 *
 * They are the same unit almost everywhere, and differ on exactly one panel: a MERCENARY's. The
 * game reads requirements, the class restriction and block chance off LoadItemDesc's own unit
 * (0x48dee0) — the merc — but `INV_FormatAttackSpeedText` ignores it and calls GetPlayerUnit_0
 * twice (0x486201, 0x486250), so a merc's weapon is timed against the CHARACTER. That is not
 * derivable from one unit, which is why both are carried.
 */
interface ItemViewers {
  /** Whose panel this is: the merc, for merc gear. Requirements and set state read this. */
  wearer?: Unit;
  /** The character, when that is a different unit from the wearer. Only attack speed reads it. */
  clientPlayer?: Unit;
}

function clientPlayerOption(viewers: ItemViewers) {
  // Unset means "same as the viewer", which is right everywhere except the merc panel — so it is
  // only supplied when it genuinely differs.
  return viewers.clientPlayer && viewers.clientPlayer !== viewers.wearer
    ? { clientPlayer: toToolkitUnit(viewers.clientPlayer) }
    : {};
}

/**
 * One captured item, ready to render.
 *
 * A capture carries no tooltip text, so every description here is rendered from the tables. That is
 * why it is a closure and not a field: a grid holds a few hundred items and exactly one of them is
 * ever under the pointer, so the text is built for the item asked about and then kept, rather than
 * for the whole stash page up front.
 *
 * The wearer is passed for two reasons. The required level is viewer-dependent, so rendered
 * against nobody it reads as unmet and shows red on gear the character is plainly wearing. And the
 * library now derives SET state — which pieces are owned, which are worn, which tiers are earned —
 * by walking `viewer.items`, which the adapter fills from the wearer's containers. Without one, a
 * set piece renders as though carried alone.
 *
 * The try is a guard rather than a known path: nothing in the shipped tables is expected to throw,
 * and losing a whole view over one item would still be a poor trade.
 *
 * A search result has no wearer at all, which is why `wearer` is optional rather than faked: the
 * item may sit on a mule of any class, so there is no viewer whose level would be the right one.
 */
export function toDisplayItem(
  unit: Unit,
  engine: TooltipEngine,
  viewers: ItemViewers = {},
): DisplayItem {
  const appearance = engine.appearance(toToolkitUnit(unit));

  /**
   * The tooltip, rendered — there is no captured one to prefer any more.
   *
   * The producer used to send the game's own string, obtained by setting the ItemDesc globals,
   * calling LoadItemDesc and reading D2Win's buffer. It costs a game-thread hop per item to obtain
   * something derivable from the fields already captured, and it was not even a stable answer: the
   * game drops a worn set piece's socket fillers on some recalcs and not others, so one session
   * captured the same equipped helm at `All Resistances +15` and at `+30`. The library renders the
   * 30 unconditionally, which is what the item grants when anything reads it.
   *
   * On demand, and memoised, because this is per item in a grid — a stash page is a few hundred —
   * and only the one under the pointer is ever read. Kept OUT of `description` for that reason:
   * `isEthereal` consults that for every cell it draws, and it needs no help here, since a capture
   * states the flag outright.
   */
  let rendered: string | null = null;
  const describe = () => {
    if (rendered !== null) return rendered;
    try {
      rendered = engine.render(
        toToolkitUnit(unit),
        viewers.wearer ? toToolkitUnit(viewers.wearer) : null,
        clientPlayerOption(viewers),
      ).coloredText;
    } catch {
      rendered = "";
    }
    return rendered;
  };

  return {
    // The SPRITE name, which is what `code` has always meant to the renderer.
    code: appearance.image,
    name: unit.title,
    // No header. It exists for sources that carry a line the description does NOT — a mule log
    // entry — whereas a rendered tooltip already opens with the item's name, so repeating it here
    // printed the name twice, the second time in the panel's plain colour rather than the item's.
    header: "",
    // Empty rather than eagerly rendered: the tooltip asks `describe` for the text it shows, and
    // the cheap readers of this field (ethereal detection) are answered by the flag below.
    description: "",
    describe,
    itemColor: appearance.color,
    invTrans: appearance.invTrans,
    hd: toHdAppearance(unit, engine),
    ethereal: (unit.itemFlags & ETHEREAL_FLAG) !== 0,
    detail: toDetail(unit, engine, viewers),
    sockets: socketsOf(unit, engine, viewers),
    x: unit.x,
    y: unit.y,
    width: unit.width,
    height: unit.height,
    gid: unit.gid,
  };
}

export function toDisplayContainer(
  id: string,
  container: CapturedContainer | undefined,
  engine: TooltipEngine,
  viewers: ItemViewers,
): DisplayContainer | undefined {
  if (!container) return undefined;
  return {
    id,
    width: container.width,
    height: container.height,
    items: container.items.map((i) => toDisplayItem(i, engine, viewers)),
  };
}
