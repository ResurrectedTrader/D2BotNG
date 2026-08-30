import type { Unit as ToolkitUnit, UnitStat } from "d2itemtoolkit";

import type {
  Unit,
  StatList as CapturedStatList,
  Stat as CapturedStat,
} from "@/generated/captures_pb";

/**
 * A captured unit as the toolkit's own `Unit`.
 *
 * The two shapes were built to describe the same producer document, so this is a copy rather
 * than an interpretation — the field names already line up, which is why there is no mapping
 * table here. What it does do is bridge three deliberate differences:
 *
 *  - our protobuf carries int64 stat values, the toolkit carries `number` (see `toToolkitStat`);
 *  - the toolkit has ONE `items` list where the capture has two shapes — an item's `sockets` and a
 *    wearer's `containers` — because a unit is only ever one or the other;
 *  - placement and item level ride along, because the library reads them off the document.
 *
 * The C# side has the same adapter for the same reasons — see `Capture/CapturedUnit.cs`.
 */
export function toToolkitUnit(unit: Unit): ToolkitUnit {
  const cached = converted.get(unit);
  if (cached) return cached;
  const result = build(unit);
  converted.set(unit, result);
  return result;
}

/**
 * Keyed on the capture message itself, which is what makes this safe and what makes it worth
 * doing: the message is immutable and lives as long as the query data holds it, so the conversion
 * of one is always the same object, and it goes away with the capture rather than being evicted.
 *
 * It has to be a cache because the conversion is deep. Rendering ONE item's tooltip converts its
 * wearer too — the library reads set state off the wearer's items — and a wearer is every
 * equipped piece, the inventory, the cube, the belt and every stash page, with all of their stat
 * lists. Without this, hovering twenty items in a stash converted the whole character twenty
 * times, which is the delay a reader can feel. Caching the children as well means an item's own
 * conversion is shared between its tooltip and its sprite lookup instead of paid for twice.
 */
const converted = new WeakMap<Unit, ToolkitUnit>();

function build(unit: Unit): ToolkitUnit {
  return {
    unitType: unit.unitType,
    classId: unit.classId,
    code: unit.code,
    quality: unit.quality,
    itemFlags: unit.itemFlags,
    fileIndex: unit.fileIndex,
    itemLevel: unit.itemLevel,
    rarePrefix: unit.rarePrefix,
    rareSuffix: unit.rareSuffix,
    autoAffix: unit.autoAffix,
    format: unit.format,
    magicPrefix: unit.magicPrefix,
    magicSuffix: unit.magicSuffix,
    earLevel: unit.earLevel,
    playerName: unit.playerName,
    gfxIndex: unit.gfxIndex,
    flagsEx: unit.flagsEx,
    statsLists: unit.statsLists.map(toToolkitStatList),
    stats: unit.stats.map(toToolkitStat),
    // Where it sits, which is what the toolkit's set rules read: `location` to find the equipped
    // pieces, and `x` — the equip location for anything equipped — to tell the alternate weapon
    // set apart from the primary one.
    location: unit.location,
    x: unit.x,
    // One list, because a unit is one thing or the other: an ITEM holds its socket fillers, a
    // WEARER holds what it carries. Concatenated rather than branched since whichever does not
    // apply is empty, and the toolkit tells them apart by depth rather than by a tag.
    items: [...unit.sockets, ...carriedBy(unit)].map(toToolkitUnit),
    // The toolkit wants the BONUSED level, which is exactly what the producer sends as `level`;
    // `hardPoints` is the invested share and has no place in a tooltip.
    skills: unit.skills.map((s) => ({ skill: s.skillId, level: s.level })),
  };
}

/**
 * Everything a wearer is holding, flattened.
 *
 * Flat because the toolkit reads placement off each item rather than off the grouping — the game's
 * inventory is one chain and containers are a presentation of it. Stash PAGES matter here: they
 * are several containers in the capture and none to the toolkit, which only cares that the item is
 * in the stash.
 */
function carriedBy(unit: Unit): Unit[] {
  const containers = unit.containers;
  if (!containers) return [];
  return [
    ...(containers.equipped?.items ?? []),
    ...(containers.inventory?.items ?? []),
    ...(containers.cube?.items ?? []),
    ...(containers.belt?.items ?? []),
    ...(containers.stash?.pages ?? []).flatMap((page) => page.items),
  ];
}

function toToolkitStatList(list: CapturedStatList) {
  return {
    stateNo: list.stateNo,
    flags: list.flags,
    stats: list.stats.map(toToolkitStat),
  };
}

/**
 * Narrowed to the low 32 bits deliberately, not clamped.
 *
 * The capture widens an unsigned stat so JSON never carries a negative — experience at level 99
 * is ~3.52 billion — but the game itself holds int32, and taking those 32 bits back restores
 * exactly what it held. `BigInt.asIntN(32, …)` is the JS spelling of C#'s `unchecked((int)…)`,
 * and the toolkit's own JSON reader uses the same convention, so a value ends up identical
 * whichever route it arrived by.
 */
function toToolkitStat(stat: CapturedStat): UnitStat {
  return {
    id: stat.id,
    value: Number(BigInt.asIntN(32, stat.value)),
    layer: stat.layer,
  };
}
