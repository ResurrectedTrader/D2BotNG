/**
 * Turning what the user picked into a `SearchItemsRequest`.
 *
 * The request contract rejects anything it cannot answer literally rather than repairing it, on
 * the grounds that a repaired filter usually matches MORE than was asked and the caller cannot
 * tell. That puts the obligation here: this builder omits a filter it cannot express rather than
 * approximating one, and the UI shows what it dropped.
 */

import {
  ItemColumn,
  type Ordering,
  type SearchItemsRequest,
  type StatCondition,
  type StatConditionGroup,
  type StatListScope,
  SocketScope,
  StatSurface,
  Tier,
} from "@/generated/captures_pb";
import { isPackedStat } from "@/lib/toolkit";
import type { StatFilterOption, StatTerm } from "./statCatalog";

/** One row in the stat-filter editor: a catalogue pick plus the bounds typed against it. */
export interface StatFilterRow {
  id: number;
  option: StatFilterOption | null;
  /** As typed, so a half-entered number does not become 0. */
  min: string;
  max: string;
  /**
   * The SECOND bound pair, for the options that expose two independent numbers. Which two depends
   * on the option: a chance-to-cast bounds its packed skill LEVEL here while `min`/`max` bound the
   * chance, and a damage range bounds its HIGH stat here while `min`/`max` bound the low one.
   */
  min2: string;
  max2: string;
}

export function emptyStatRow(id: number): StatFilterRow {
  return { id, option: null, min: "", max: "", min2: "", max2: "" };
}

/**
 * What to sort by, as the control holds it.
 *
 * `kind` "default" leaves the request's ordering unset, which the store reads as its own grouping
 * order — character, then container, then position. That is deliberately not spelled as a column:
 * it is several, and the store owns the list.
 */
type SortKey =
  | { kind: "default" }
  /**
   * A COLUMN of the item, named by a result line that displays one.
   *
   * The same handle as below and a different axis: a requirement is not a stat on a stat list, it
   * is a property of the row, so the tooltip library attaches no stat ids to those lines and they
   * could never be reached through `statIds`. `SORTABLE_SECTIONS` is the bridge — the section the
   * library labels the line with, mapped to the column the store orders on.
   */
  | { kind: "column"; column: ItemColumn; label: string }
  /**
   * A stat named by a RESULT LINE, which is the only sort handle there is.
   *
   * Clicking a modifier on a result sorts by it, and what the reader clicked is one rendered line —
   * whose stat key the tooltip library carries on the line itself, so there is nothing to resolve.
   * A catalogue pick would be a different handle: it stands for a wording and may cover several
   * stats at several scales, only some of which an ordering could rank on.
   *
   * The layer is always stated, never dropped when it is zero: a rendered line has one, and 0 is a
   * real layer — the Amazon, and her Bow tab. Leaving it unset there would rank "+2 to Amazon Skill
   * Levels" by every class's +skills at once, and the stored column is NOT NULL DEFAULT 0, so a
   * plain unlayered stat is matched by sending 0 rather than by sending nothing.
   */
  | { kind: "line"; statIds: number[]; layer: number; label: string };

export interface SortChoice {
  key: SortKey;
  descending: boolean;
}

/**
 * Result lines that rank by an item COLUMN rather than by a stat, keyed by the section the tooltip
 * library labels them with.
 *
 * A requirement is a property of the item row — resolved at ingest, with every affix and the
 * ethereal discount already applied — so there is no stat list to read it off and no stat id on the
 * line. The store has ordered on these columns all along; this is the handle to reach them, and it
 * is the same gesture as ranking by a modifier because to a reader they are the same gesture.
 *
 * `WeaponDamage` is absent here on purpose: which column a damage row ranks on depends on WHICH
 * line it is, which the section alone does not say — see `DAMAGE_COLUMN_BY_KIND`. `ArmorClass` is
 * absent for the opposite reason — defence IS a stat (31), the library labels that line with it,
 * and it ranks through the ordinary stat path with no column at all.
 */
export const SORTABLE_SECTIONS: Record<
  string,
  { column: ItemColumn; descending: boolean }
> = {
  // Ascending first, because the useful end of a requirement is the LOW one: the question is
  // which of these a character can actually wear.
  RequiredLevel: { column: ItemColumn.REQUIRED_LEVEL, descending: false },
  RequiredStrength: { column: ItemColumn.REQUIRED_STRENGTH, descending: false },
  RequiredDexterity: {
    column: ItemColumn.REQUIRED_DEXTERITY,
    descending: false,
  },
};

/**
 * The column a damage line ranks on, by the kind of line it is.
 *
 * The HIGH end of that kind, because that is the number a reader compares weapons on. Each kind is
 * its own column rather than one "damage" column: an item draws only the lines it has, so ranking
 * by two-handed damage puts every one-handed weapon last — which is the honest answer, since they
 * do not have the thing being ranked.
 *
 * `ThrowingPotion` shares the throw column: the game labels that line with the same string, and it
 * is the only line such an item draws.
 */
export const DAMAGE_COLUMN_BY_KIND: Record<string, ItemColumn> = {
  OneHand: ItemColumn.DAMAGE_ONE_HAND_MAX,
  TwoHand: ItemColumn.DAMAGE_TWO_HAND_MAX,
  Throw: ItemColumn.DAMAGE_THROW_MAX,
  ThrowingPotion: ItemColumn.DAMAGE_THROW_MAX,
};

export const DEFAULT_SORT: SortChoice = {
  key: { kind: "default" },
  // Ascending, because on the store's own order that means the natural reading order — character,
  // then container, then down the grid. Descending is the better default once a real key is
  // chosen (the biggest modifier, the highest requirement), so the control flips it there.
  descending: false,
};

/**
 * The sort as the contract wants it, or undefined for the store's own order.
 *
 * A clicked result line is the only sort handle, and it already names its stat key — so this is a
 * transcription rather than a resolution, and every other key leaves the ordering unset for the
 * store to apply its own.
 */
function buildOrdering(
  sort: SortChoice,
  sockets: SocketScope,
): Ordering | undefined {
  if (sort.key.kind === "column") {
    return {
      $typeName: "d2bot.captures.Ordering",
      descending: sort.descending,
      by: { case: "column", value: sort.key.column },
    } as Ordering;
  }
  if (sort.key.kind !== "line") return undefined;

  return {
    $typeName: "d2bot.captures.Ordering",
    descending: sort.descending,
    by: {
      case: "stat",
      value: {
        $typeName: "d2bot.captures.StatOrder",
        statIds: sort.key.statIds,
        layer: sort.key.layer,
        // Ranks on whichever surface the panel is matching on — by the total, when that is what
        // the filters used — except for a packed stat, which the merged surface does not carry.
        // Ordering by one there is worse than ordering by nothing: every item ties on NULL, the
        // store cannot tell an empty ranking from a legitimate one, and the page comes back in its
        // default order with an arrow next to a line claiming it is ranked.
        ...(sort.key.statIds.some(isPackedStat)
          ? rawScope(sockets)
          : mergedScope(sockets)),
      },
    },
  } as Ordering;
}

/**
 * One pick from the merged item selector.
 *
 * Four different things a reader thinks of as "which item", which the contract answers through
 * three different fields — so they are chosen from one list and split apart at build time rather
 * than making the reader know which axis their answer lives on.
 */
export type SpecificPick =
  | { kind: "base"; id: number; name: string }
  | { kind: "unique"; id: number; name: string }
  | { kind: "set"; id: number; name: string }
  | { kind: "runeword"; id: number; name: string };

/** Which request field a pick lands in. Picks sharing one field are alternatives; fields AND. */
function identityField(pick: SpecificPick): string {
  switch (pick.kind) {
    case "base":
      return "a base item";
    case "runeword":
      return "a runeword";
    default:
      return "a unique or set item";
  }
}

/**
 * Whether the item picks span more than one request field, and so narrow each other.
 *
 * The picker merges four things a reader calls "which item", but the contract answers them through
 * separate fields that AND — which is what makes "Treachery, on a Wire Fleece" askable. The cost is
 * that a base plus an unrelated unique is a contradiction, and the store answers a contradiction
 * literally, with nothing. It cannot tell a deliberate pairing from a mistaken one; this is what
 * lets the UI say so before the reader concludes their items are missing.
 */
export function identityFieldSpan(picks: SpecificPick[]): string[] {
  return [...new Set(picks.map(identityField))].sort();
}

/** ItemStatCost `item_numsockets`. Written once, on the item's base list. */
const STAT_NUM_SOCKETS = 194;

/** Quality ids the specific-item pairs are keyed by; `file_index` is overloaded per quality. */
const QUALITY_SET = 5;
const QUALITY_UNIQUE = 7;

export interface PropertyFilters {
  /** Owning profile names. Empty is every character, so there is no "all" member to contradict. */
  profiles: string[];
  qualities: number[];
  tiers: Tier[];
  /** Base items, uniques, set items and runewords, from one list. */
  items: SpecificPick[];
  itemTypes: number[];
  /**
   * Which containers to look in; empty means anywhere.
   *
   * The ONLY control over where an item sits: "not worn" is this list without `equipped`, rather
   * than a second, inverted field that could contradict it.
   */
  containers: string[];
  /** Which inventory graphic, for the bases that have more than one. */
  gfxIndexes: number[];
  requiredLevel: { min: string; max: string };
  /** What the item demands of a wearer, both resolved at ingest. */
  requiredStrength: { min: string; max: string };
  requiredDexterity: { min: string; max: string };
  /**
   * How many sockets the item has.
   *
   * A stat rather than a column — `item_numsockets` — but exposed as a property because that is
   * what it is to a reader. Sound as a plain bound, unlike defence or damage: the game writes it
   * once, on the base list, so there is exactly one source and no summing question.
   */
  sockets: { min: string; max: string };
  /** The item's OWN level — what it rolled at, not what it demands of a wearer. */
  itemLevel: { min: string; max: string };
  /**
   * Whether what is socketed into an item counts towards its modifiers.
   *
   * The only stat-scope question a reader is asked. A bound is always compared against the item's
   * TOTAL — the number its tooltip prints — because that is what someone typing "30 all
   * resistances" means; whether a rune may make up part of that 30 is a real question, and this is
   * it. `FILLERS_ONLY` is not offered: "what the gems alone add up to" has no total, and asking
   * which item holds a particular gem is a search for the gem.
   */
  socketScope: SocketScope;
  /** Which flag bits must be set / clear, as a pair of masks. */
  flagsAll: number;
  flagsNone: number;
}

export const EMPTY_PROPERTIES: PropertyFilters = {
  profiles: [],
  qualities: [],
  tiers: [],
  items: [],
  itemTypes: [],
  containers: [],
  gfxIndexes: [],
  requiredLevel: { min: "", max: "" },
  requiredStrength: { min: "", max: "" },
  requiredDexterity: { min: "", max: "" },
  sockets: { min: "", max: "" },
  itemLevel: { min: "", max: "" },
  socketScope: SocketScope.WITH_FILLERS,
  flagsAll: 0,
  flagsNone: 0,
};

function parseBound(text: string): number | undefined {
  const trimmed = text.trim();
  if (trimmed === "") return undefined;
  const value = Number(trimmed);
  return Number.isFinite(value) ? Math.trunc(value) : undefined;
}

/**
 * Which surface a condition reads, and the socket scope that goes with it.
 *
 * A reader only ever picks the socket half; the surface is chosen here. MERGED is what every
 * ordinary modifier uses, and RAW exists for the packed ones the merged view cannot hold — see
 * `scopeFor`.
 */
interface StatScope {
  surface: StatSurface;
  sockets: SocketScope;
  lists?: StatListScope;
}

/**
 * The scope every ordinary modifier reads.
 *
 * Always the item's TOTAL, because that is the number its tooltip prints and the one a reader types
 * — "30 all resistances" has to find a helm carrying 15 of its own and 15 from a rune. The store
 * keeps that total both with and without the fillers, which is the whole of the socket choice.
 */
function mergedScope(sockets: SocketScope): StatScope {
  return { surface: StatSurface.MERGED, sockets };
}

/**
 * The set-tier lists — `state_no` 165 to 170 — which no search counts.
 *
 * A tier's stats exist only because the WEARER holds the other pieces, so they are not this item's
 * worth: a Tal Rasha's belt would search as 98 defence while worn and 38 the moment it is muled,
 * and the same query would answer differently depending on what a bot happened to be wearing when
 * it last reported. The merged surface excludes them at ingest, and this is what makes the raw
 * surface agree — otherwise the two would disagree about the same item.
 */
const SET_TIER_STATES = [165, 166, 167, 168, 169, 170];

const RAW_LISTS: StatListScope = {
  $typeName: "d2bot.captures.StatListScope",
  excludeStates: SET_TIER_STATES,
  flagsAll: undefined,
  flagsNone: undefined,
};

/**
 * The scope a particular modifier has to use, which is not always the one asked for.
 *
 * A PACKED value — charges, chance-to-cast, the by-time triples — is absent from the merged surface
 * entirely: the stored word encodes two or three fields, so it can be neither summed nor compared
 * against a bound, and the library excludes it from its totals by design. A merged condition for
 * one would match nothing at all, with no rejection to explain why, so those go to raw whatever the
 * preference says. Raw is also the right surface for them on the merits: "which item grants this
 * charge" is a provenance question.
 *
 * Asked by stat id rather than inferred from the option's shape. `levelBound` covers the two
 * families whose LAYER packs a skill level, but the by-time family packs its value instead and
 * looks like an ordinary linear modifier from here — which is exactly how it went to the merged
 * surface and matched nothing.
 *
 * Only the SURFACE is decided here. The socket scope is still the panel's: "the item's own" is a
 * choice among sources, which raw answers as readily as merged — so a reader who asked to ignore
 * runes has to be obeyed on a charge as much as on a resistance.
 */
function rawScope(sockets: SocketScope): StatScope {
  return {
    surface: StatSurface.RAW,
    sockets,
    lists: RAW_LISTS,
  };
}

function scopeFor(option: StatFilterOption, sockets: SocketScope): StatScope {
  const packed = option.terms.some((t) => t.statIds.some(isPackedStat));
  return option.levelBound || packed ? rawScope(sockets) : mergedScope(sockets);
}

/** A condition for one term, with the row's bounds applied in that term's own value scale. */
function conditionFor(
  term: StatTerm,
  row: StatFilterRow,
  option: StatFilterOption,
  scope: StatScope,
): StatCondition {
  const condition = {
    $typeName: "d2bot.captures.StatCondition",
    statIds: term.statIds,
    ...scope,
  } as StatCondition;

  if (option.levelBound) {
    // The skill's level is packed into the layer's low bits, so bounding it is a LAYER RANGE over
    // the one skill. An open end falls back to the whole packed field, which reads as "any level"
    // — and is also what pins the condition to this skill rather than any skill.
    const base = term.layer ?? 0;
    const min2 = parseBound(row.min2);
    const max2 = parseBound(row.max2);
    condition.layer = base | Math.max(0, Math.min(min2 ?? 0, option.levelMask));
    condition.layerMax =
      base |
      (max2 !== undefined
        ? Math.max(0, Math.min(max2, option.levelMask))
        : option.levelMask);
  } else if (term.layer !== undefined) {
    condition.layer = term.layer;
  }

  if (option.valueBound) {
    const min = parseBound(row.min);
    const max = parseBound(row.max);
    if (min !== undefined) condition.minValue = term.toRaw(min);
    if (max !== undefined) condition.maxValue = term.toRaw(max);
  }
  return condition;
}

/**
 * A group of modifier rows, and how many of them an item has to satisfy.
 *
 * An UNCOUNTED group means every row must hold — the plain "all of these" case, which is also what
 * a lone group of one row is. A counted one is "at least N of these", the thing worth having a
 * group for at all.
 */
export interface StatFilterGroup {
  id: number;
  rows: StatFilterRow[];
  /** As typed. Blank = all of them, which is what the contract reads an absent `min_matches` as. */
  minMatches: string;
  /**
   * Reads the count as an upper bound instead of a lower one.
   *
   * Not a second field on the request: the contract has no `max_matches`, because a count-based
   * upper bound is computed over the items that matched SOMETHING and so can never see the ones
   * that matched nothing — exactly the items "at most 1 of these" is meant to include. Negating
   * "at least N+1" does see them, since the negation is applied over every item.
   */
  atMost: boolean;
}

export function emptyStatGroup(id: number): StatFilterGroup {
  return { id, rows: [emptyStatRow(1)], minMatches: "", atMost: false };
}

/**
 * The high half of a damage range: its own stat, bounded by the row's SECOND pair.
 *
 * Scaled by the low stat's mapping, which is safe because a damage pair shares one scale by
 * construction — both halves are the same quantity measured at either end.
 */
function rangeMaxCondition(
  row: StatFilterRow,
  option: StatFilterOption,
  scope: StatScope,
): StatCondition {
  const scale = option.terms[0];
  const condition = {
    $typeName: "d2bot.captures.StatCondition",
    statIds: option.rangeMaxStatIds!,
    ...scope,
  } as StatCondition;
  const min = parseBound(row.min2);
  const max = parseBound(row.max2);
  if (min !== undefined) condition.minValue = scale.toRaw(min);
  if (max !== undefined) condition.maxValue = scale.toRaw(max);
  return condition;
}

/**
 * A row becomes either a bare condition or a group of its own, and which is not cosmetic:
 *
 *  - one term            -> a bare condition.
 *  - `requireAll`        -> a group with no `min_matches`, which the contract reads as "all of
 *                           them". This is what makes "+15 to All Resistances" mean all four.
 *  - several terms       -> a group with `min_matches` 1, i.e. OR. Needed rather than pooling the
 *                           ids into one condition because sources sharing a description do not
 *                           necessarily share a value scale, and one condition has one bound.
 */
function buildRow(
  row: StatFilterRow,
  sockets: SocketScope,
): {
  condition?: StatCondition;
  extra?: StatCondition;
  group?: StatConditionGroup;
} {
  const option = row.option;
  if (!option || option.terms.length === 0) return {};

  const scope = scopeFor(option, sockets);

  // A damage RANGE is two distinct quantities, not alternatives: the low stat bounded by the first
  // pair and the high stat by the second, both required. Top-level conditions are AND-ed, so they
  // go out as two bare conditions rather than a group. Its low half is one term by construction,
  // so there is nothing else to build.
  if (option.rangeMaxStatIds) {
    return {
      condition: conditionFor(option.terms[0], row, option, scope),
      extra: rangeMaxCondition(row, option, scope),
    };
  }

  const conditions = option.terms.map((term) =>
    conditionFor(term, row, option, scope),
  );
  if (conditions.length === 1) return { condition: conditions[0] };

  return {
    group: {
      $typeName: "d2bot.captures.StatConditionGroup",
      conditions,
      // Absent = all of them, which is exactly `requireAll`.
      minMatches: option.requireAll ? undefined : 1,
      negate: false,
    } as StatConditionGroup,
  };
}

/**
 * Which of an option's stat sources ONE condition can carry.
 *
 * A filter normally keeps a term per source, because each then gets its own bound in its own value
 * scale. Inside a counted group there is only one condition per row, and a condition has one pair
 * of bounds over the raw column — and raw values on different scales are not comparable, since stat
 * 7 stores life ×256 while stat 270 stores it ×128.
 *
 * So the pooled ids are the ones sharing the FIRST term's scale, which is the primary one for every
 * option that has this shape. `omitted` says whether that left anything out, so the UI can show it
 * rather than quietly matching on a subset.
 */
function pooledSources(option: StatFilterOption): {
  statIds: number[];
  omitted: number;
} {
  const terms = option.terms;
  // The scale as a probe rather than a declared field: a term exposes the mapping, not the shift,
  // and two terms agree exactly when they map the same input to the same raw value.
  const scale = terms[0]?.toRaw(1);
  const usable = terms.filter((t) => t.toRaw(1) === scale);
  return {
    statIds: usable.flatMap((t) => t.statIds),
    omitted: terms.length - usable.length,
  };
}

/**
 * Why a row cannot go into a counted group, or undefined when it can.
 *
 * The contract's groups do not nest — `conditions` is a flat list — so a row that is itself more
 * than one condition has no shape to take here. Both cases below used to be flattened into the one
 * pooled condition anyway, which quietly answered a DIFFERENT question and in the one direction a
 * reader cannot detect: more items, not fewer.
 */
export type CountedRowRefusal = "all" | "range";

export function countedRowRefusal(
  row: StatFilterRow,
): CountedRowRefusal | undefined {
  const option = row.option;
  if (!option) return undefined;

  // `stat_ids` within one condition are OR-ed, so pooling an ALL modifier inverts it: "all four
  // resistances at 15" would go out as "any one resistance at 15".
  if (option.requireAll && pooledSources(option).statIds.length > 1)
    return "all";

  // A damage range is two quantities with a bound each, and a condition carries one pair. Pooling
  // kept the low bound and dropped the high one without saying so.
  if (option.rangeMaxStatIds && rangeMaxBounded(row)) return "range";

  return undefined;
}

/**
 * Whether a COUNTED group holds a row no single condition can carry, which makes it unaskable.
 *
 * There is no shape for such a group. Leaving the row out changes the question in a direction that
 * depends on the group: for "at least N" the same count over fewer members is stricter, but an "at
 * most N" group goes out NEGATED, so a dropped member makes it match MORE than was asked — the one
 * direction a reader cannot detect. So the whole group is refused before a request is built, where
 * the row can be pointed at and moved, rather than quietly reinterpreted after.
 */
export function countedGroupRefused(group: StatFilterGroup): boolean {
  if (parseBound(group.minMatches) === undefined) return false;
  return group.rows.some((row) => row.option && countedRowRefusal(row));
}

/** Whether the second (high) pair of a damage range has a bound typed into it. */
function rangeMaxBounded(row: StatFilterRow): boolean {
  return (
    parseBound(row.min2) !== undefined || parseBound(row.max2) !== undefined
  );
}

/**
 * One row as exactly ONE condition, for use inside a COUNTED group.
 *
 * Letting a row's terms in as separate conditions would be worse than wrong: each would count
 * towards `min_matches` on its own, so "at least 2 of these 3" could be satisfied by ONE modifier
 * that happens to have two sources. So the terms are pooled into a single condition, which
 * `stat_ids` allows — but only for the ones sharing a value scale, since a condition has one pair
 * of bounds. `pooledSources` makes that call, and `countedRowOmits` asks the same function what it
 * left out, so the group UI cannot disagree with the request about which sources a label stands
 * for.
 *
 * A row `countedRowRefusal` names has no shape here and is left out. Which is why a group holding
 * one is refused up front by `countedGroupRefused` — the count over fewer members is a different
 * question, and for a negated group a wider one.
 */
function buildCountedRow(
  row: StatFilterRow,
  sockets: SocketScope,
): StatCondition | undefined {
  const option = row.option;
  if (!option || option.terms.length === 0) return undefined;
  if (countedRowRefusal(row)) return undefined;

  const primary = option.terms[0];
  const { statIds } = pooledSources(option);
  return {
    ...conditionFor(primary, row, option, scopeFor(option, sockets)),
    statIds,
  };
}

/** Whether a row inside a counted group has sources its single condition cannot carry. */
export function countedRowOmits(row: StatFilterRow): number {
  return row.option ? pooledSources(row.option).omitted : 0;
}

function buildGroups(
  groups: StatFilterGroup[],
  sockets: SocketScope,
): { conditions: StatCondition[]; groups: StatConditionGroup[] } {
  const conditions: StatCondition[] = [];
  const built: StatConditionGroup[] = [];

  for (const group of groups) {
    const rows = group.rows.filter((r) => r.option);
    if (rows.length === 0) continue;

    const count = parseBound(group.minMatches);
    if (count === undefined) {
      // Plain AND: every row stands alone, and each keeps whatever shape it needs.
      for (const row of rows) {
        const one = buildRow(row, sockets);
        if (one.condition) conditions.push(one.condition);
        if (one.extra) conditions.push(one.extra);
        if (one.group) built.push(one.group);
      }
      continue;
    }

    // "At most N" is "NOT at least N+1". An upper bound at or above the row count constrains
    // nothing at all, so the group is dropped rather than sent — the one filter this builder omits
    // for being vacuous rather than inexpressible.
    if (group.atMost && count >= rows.length) continue;

    const members = rows
      .map((row) => buildCountedRow(row, sockets))
      .filter((c): c is StatCondition => c !== undefined);
    if (members.length === 0) continue;

    built.push({
      $typeName: "d2bot.captures.StatConditionGroup",
      conditions: members,
      // Out of range is rejected by the store rather than clamped, so the UI bounds the input
      // instead of sending something it knows will be refused. An upper bound of N goes out as
      // "not at least N+1", which is why it can exceed the typed number by one. It is the TYPED
      // count this bounds and nothing else: a group whose rows do not all become members is
      // refused before a request is built, so the clamp cannot stand in for a dropped row.
      minMatches: Math.max(
        1,
        Math.min(group.atMost ? count + 1 : count, members.length),
      ),
      negate: group.atMost,
    } as StatConditionGroup);
  }

  return { conditions, groups: built };
}

export function buildSearchRequest(
  properties: PropertyFilters,
  statGroups: StatFilterGroup[],
  sort: SortChoice,
  offset: number,
  limit: number,
): SearchItemsRequest {
  const { conditions, groups } = buildGroups(
    statGroups,
    properties.socketScope,
  );

  /** An inclusive bound, or undefined when both ends are blank. */
  function range(bounds: { min: string; max: string }) {
    const min = parseBound(bounds.min);
    const max = parseBound(bounds.max);
    return min === undefined && max === undefined
      ? undefined
      : { $typeName: "d2bot.captures.Int32Range" as const, min, max };
  }

  // Sockets are a STAT (`item_numsockets`) rather than a column, so the property field becomes a
  // condition alongside the modifier ones. HOST_ONLY rather than the panel's scope: it is the
  // item's own count, and a filler has no socket count of its own to fold in.
  const socketMin = parseBound(properties.sockets.min);
  const socketMax = parseBound(properties.sockets.max);
  if (socketMin !== undefined || socketMax !== undefined) {
    conditions.push({
      $typeName: "d2bot.captures.StatCondition",
      statIds: [STAT_NUM_SOCKETS],
      sockets: SocketScope.HOST_ONLY,
      // Stated rather than left to the enum's zero. It is the same RAW the default resolves to,
      // but every other condition here carries its surface explicitly, and a socket count read off
      // a total would be a different question.
      surface: StatSurface.RAW,
      minValue: socketMin,
      maxValue: socketMax,
    } as StatCondition);
  }

  return {
    $typeName: "d2bot.captures.SearchItemsRequest",
    profiles: properties.profiles,
    classIds: properties.items.flatMap((i) =>
      i.kind === "base" ? [i.id] : [],
    ),
    itemTypes: properties.itemTypes,
    tiers: properties.tiers,
    qualities: properties.qualities,
    // A unique and a set item are both a (quality, file_index) pair, because file_index is
    // overloaded by quality — several uniques share one base code, so the pair is the identity.
    specificItems: properties.items.flatMap((i) =>
      i.kind === "unique" || i.kind === "set"
        ? [
            {
              $typeName: "d2bot.captures.SpecificItem" as const,
              quality: i.kind === "unique" ? QUALITY_UNIQUE : QUALITY_SET,
              fileIndex: i.id,
            },
          ]
        : [],
    ),
    runewords: properties.items.flatMap((i) =>
      i.kind === "runeword" ? [i.id] : [],
    ),
    gfxIndexes: properties.gfxIndexes,
    itemFlagsAll: properties.flagsAll || undefined,
    itemFlagsNone: properties.flagsNone || undefined,
    containers: properties.containers,
    conditions,
    groups,
    requiredLevel: range(properties.requiredLevel),
    itemLevel: range(properties.itemLevel),
    requiredStrength: range(properties.requiredStrength),
    requiredDexterity: range(properties.requiredDexterity),
    offset,
    limit,
    ordering: buildOrdering(sort, properties.socketScope),
  } as SearchItemsRequest;
}

/**
 * Whether a BUILT request would narrow anything at all.
 *
 * Worth asking before sending: an empty request is a request for every item of every character,
 * which is a slow way to render a page nobody asked for.
 *
 * Asked of the request rather than of the filter state, because the two can honestly disagree —
 * this builder drops a filter it cannot express (a vacuous "at most", a group left with no members)
 * and a chosen modifier is therefore not proof that anything reached the wire. Reading the state
 * let a single unsendable row arm the Search button and fetch every item there is.
 */
export function requestHasFilter(request: SearchItemsRequest): boolean {
  return (
    request.profiles.length > 0 ||
    request.conditions.length > 0 ||
    request.groups.length > 0 ||
    request.classIds.length > 0 ||
    request.itemTypes.length > 0 ||
    request.tiers.length > 0 ||
    request.qualities.length > 0 ||
    request.specificItems.length > 0 ||
    request.runewords.length > 0 ||
    request.gfxIndexes.length > 0 ||
    request.containers.length > 0 ||
    request.itemFlagsAll !== undefined ||
    request.itemFlagsNone !== undefined ||
    [
      request.requiredLevel,
      request.requiredStrength,
      request.requiredDexterity,
      request.itemLevel,
    ].some((r) => r !== undefined)
  );
}
