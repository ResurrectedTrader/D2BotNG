/**
 * What the shared viewer components actually need.
 *
 * There are two character stacks and they send genuinely different things: v1 sends presentation
 * the game engine already resolved, v2 sends the raw capture those answers come from. Neither is
 * a subset of the other, so there is no honest conversion between them: reshaping v2 into v1 means
 * inventing a skills split and restructuring kills, which is exactly the kind of quiet fudge that
 * later reads as a bug.
 *
 * So each stack gets its own view, and what they share is stated here: the narrow shapes the
 * panels and grids read. Both sides PROJECT into these — no derivation, no guessing — and the
 * schemas stop leaking into the render tree.
 */

import type { RenderableItem } from "@/features/items";

/** A stat row. Both stacks already send exactly this pair. */
export interface StatValue {
  id: number;
  value: bigint;
}

/**
 * A skill, as the panel displays it: what was invested and what it currently is.
 *
 * The two stacks decompose this differently — v1 sends invested plus the gear share, v2 sends
 * invested plus the bonused total — so each states `total` in its own terms rather than one
 * reconstructing the other's arithmetic.
 */
export interface SkillLevels {
  skillId: number;
  invested: number;
  total: number;
}

/** Quests and waypoints for one difficulty. */
export interface DifficultyProgress {
  quests: number[];
  waypoints: number[];
}

/**
 * One kill bucket, flat. v1 nests these difficulty -> class -> spec and v2 keeps them flat, but
 * flattening loses nothing and needs no interpretation, so the flat form is the shared one.
 * `superUnique` keeps the two buckets disjoint: a super-unique is never also counted by class.
 */
export interface KillCount {
  difficulty: number;
  superUnique: boolean;
  id: number;
  spec: number;
  count: bigint;
}

/** Milliseconds spent in one area, on one difficulty. */
export interface AreaDuration {
  difficulty: number;
  area: number;
  milliseconds: bigint;
}

/**
 * What the renderer needs, plus where the item sits.
 *
 * Extends `RenderableItem` rather than restating it: the renderer owns that contract, and a copy
 * here drifts the moment a field is added to one and not the other — which is exactly what
 * happened when items gained their held-Ctrl detail.
 *
 * Note what `code` means, since it is the one field whose name misleads: it is the SPRITE name,
 * not the item code. That is what it has always meant to the rendering pipeline (`<code>.dc6`).
 * v1 receives it already resolved; v2 resolves it from the game's tables, where exceptional and
 * elite tiers collapse to the base art, set and unique items get their own, and rings, amulets,
 * jewels and charms carry a variant suffix.
 */
export interface DisplayItem extends RenderableItem {
  sockets: DisplayItem[];

  /** Where it sits: grid cell and footprint, or the equip-location id in `x` for a slot set. */
  x: number;
  y: number;
  width: number;
  height: number;
  /** Session unit id — a React key that is stable while the game is, unlike the grid position. */
  gid: number;
}

/** A grid of items, or a slot set when the dimensions are zero. */
export interface DisplayContainer {
  /** A React key. Stash pages share a container name, so theirs is page-qualified. */
  id: string;
  width: number;
  height: number;
  items: DisplayItem[];
}

/**
 * What each storage container is called.
 *
 * The ids are the ones both stacks already use — v1 sends them as a container's `id`, v2 as the
 * field name — so this is a label table, not a projection: the same words wherever a container is
 * named, whether that is a grid heading in the viewer or a search result's provenance line.
 */
export const CONTAINER_LABELS: Record<string, string> = {
  equipped: "Equipped",
  inventory: "Inventory",
  cube: "Horadric Cube",
  belt: "Belt",
  stash: "Stash",
};

/** The storage grids the viewer lays out, in the order it lays them out; stash pages follow. */
export const STORAGE_IDS = ["inventory", "cube", "belt"] as const;

/**
 * What to call one stash page.
 *
 * A lone page is just "Stash" — numbering it would imply there are others. Where there are
 * several it is the page's own name if the game gave it one, and its 1-based index if not.
 */
export function stashPageLabel(
  name: string,
  index: number,
  pageCount: number,
): string {
  if (pageCount <= 1) return CONTAINER_LABELS.stash;
  return name || `${CONTAINER_LABELS.stash} ${index + 1}`;
}

/**
 * Difficulty names, indexed by the id both stacks send.
 *
 * One table because it is the same three words in the header line, the stats panel's penalty note
 * and both difficulty pickers — a label, like the container names above, rather than anything
 * either schema has to be interpreted to produce.
 */
export const DIFFICULTY_NAMES = ["Normal", "Nightmare", "Hell"] as const;

/**
 * The header facts, which both stacks send outright — except v2's level, which is stat 12 rather
 * than a field, because a capture stores what the game held and the game holds level as a stat.
 */
export interface CharacterFacts {
  profile: string;
  charName: string;
  account: string;
  realm: string;
  level: number;
  charClass: number;
  difficulty: number;
  area: number;
  /** When the current area was entered, for the live timer. Absent = no timer. */
  areaEnteredAt?: { seconds: bigint };
  /** Active weapon set: 0 primary, 1 secondary. */
  hand: 0 | 1;
  hardcore: boolean;
  ladder: boolean;
  expansion: boolean;
  /** Last report, for the offline caption. Absent reads as a plain "Offline". */
  updatedAt?: { seconds: bigint };
}
