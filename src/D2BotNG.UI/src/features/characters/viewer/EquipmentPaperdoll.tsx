/**
 * EquipmentPaperdoll / MercPaperdoll - equipped items laid out by equip slot
 * (sent in item.x; y is always 0 for slot containers). Labeled slot boxes, zero
 * assets. Helm sits above the body armor (center column).
 *
 * Expansion characters have a second weapon set. The game reports the *active*
 * set in equip locations 4/5 and the inactive one in 11/12; which set is active
 * comes from the snapshot's top-level `hand` field (passed in as `activeSet`),
 * because the WeaponSwitch char flag is only valid in the lobby. A I/II toggle
 * picks which set to view (defaulting to the active one, with the active set
 * marked); the selection is the user's and stays put as the active set flips live.
 */

import clsx from "clsx";
import { ActiveDot, SegmentedControl } from "./CharacterChrome";
import { ItemCell } from "./ItemCell";
import type { DisplayContainer, DisplayItem } from "./contracts";

interface SlotDef {
  slot: number; // D2 equip-location id, carried in item.x for slot containers
  label: string;
  area: string;
}

// D2 equip locations: 1 head, 2 amulet, 3 torso, 4 right-hand, 5 left-hand,
// 6 right ring, 7 left ring, 8 belt, 9 feet, 10 gloves, 11 alt right, 12 alt left.
const SLOTS: SlotDef[] = [
  { slot: 1, label: "Helm", area: "helm" },
  { slot: 2, label: "Amulet", area: "amulet" },
  { slot: 4, label: "Weapon", area: "weapon" },
  { slot: 3, label: "Armor", area: "armor" },
  { slot: 5, label: "Off-hand", area: "offhand" },
  // Rings mirror the paperdoll (the character faces you), like the weapon/off-hand:
  // right ring (loc 6) on the left, left ring (loc 7) on the right.
  { slot: 6, label: "Ring", area: "ringL" },
  { slot: 8, label: "Belt", area: "belt" },
  { slot: 7, label: "Ring", area: "ringR" },
  { slot: 10, label: "Gloves", area: "gloves" },
  { slot: 9, label: "Boots", area: "boots" },
];

// Helm centered above the body armor; amulet upper-right above the off-hand.
// The top-left cell is empty; the weapon-set toggle sits in the panel title.
const GRID_AREAS = `
  ".      helm   amulet"
  "weapon armor  offhand"
  "ringL  belt   ringR"
  "gloves .      boots"
`;

/**
 * A slot layout filled from an equipped container.
 *
 * The character's paperdoll and the mercenary's are the same thing with a different set of slots
 * and a different arrangement of them, so they are one component: everything that made them look
 * like two — the lookup by equip location, the grid, the slot boxes — is identical.
 */
function Paperdoll({
  slots,
  areas,
  columns,
  container,
  locationOf,
}: {
  slots: SlotDef[];
  areas: string;
  columns: number;
  container: DisplayContainer | undefined;
  /** Which equip location a slot reads, when it is not the slot's own. */
  locationOf?: (def: SlotDef) => number;
}) {
  const bySlot = new Map<number, DisplayItem>();
  for (const item of container?.items ?? []) bySlot.set(item.x, item);

  return (
    <div
      className="grid w-fit gap-2"
      style={{
        gridTemplateAreas: areas,
        gridTemplateColumns: `repeat(${columns}, 4.5rem)`,
      }}
    >
      {slots.map((def) => (
        <ItemCell
          key={def.area}
          item={bySlot.get(locationOf ? locationOf(def) : def.slot)}
          className="flex min-h-[72px] items-center justify-center rounded bg-zinc-900/40 ring-1 ring-zinc-800"
          style={{ gridArea: def.area }}
          spriteClassName="min-h-16 min-w-16"
          empty={
            <span className="text-[10px] uppercase tracking-wide text-zinc-600">
              {def.label}
            </span>
          }
        />
      ))}
    </div>
  );
}

/** The I/II weapon-set view toggle, rendered in the Equipment panel title for
 *  expansion characters. The green dot marks the live active set; the selection
 *  is the user's view choice and is owned by the parent. */
export function WeaponSetToggle({
  selectedSet,
  onSelect,
  activeSet,
}: {
  selectedSet: 0 | 1;
  onSelect: (set: 0 | 1) => void;
  activeSet: 0 | 1;
}) {
  return (
    <SegmentedControl>
      {([0, 1] as const).map((set) => (
        <button
          key={set}
          type="button"
          onClick={() => onSelect(set)}
          title={
            set === activeSet
              ? "Active weapon set"
              : `Weapon set ${set === 0 ? "I" : "II"}`
          }
          className={clsx(
            "relative rounded px-2 py-0.5 font-medium",
            selectedSet === set
              ? "bg-zinc-700 text-zinc-100"
              : "text-zinc-400 hover:text-zinc-200",
          )}
        >
          {set === 0 ? "I" : "II"}
          {set === activeSet && <ActiveDot />}
        </button>
      ))}
    </SegmentedControl>
  );
}

export function EquipmentPaperdoll({
  equipped,
  selectedSet,
  activeSet,
}: {
  equipped: DisplayContainer | undefined;
  selectedSet: 0 | 1;
  activeSet: 0 | 1;
}) {
  // The active set's weapons sit in 4/5; the inactive set's in 11/12. Only the two hands move —
  // everything else is worn regardless of which set is drawn.
  const selectedIsActive = selectedSet === activeSet;

  return (
    <Paperdoll
      slots={SLOTS}
      areas={GRID_AREAS}
      columns={3}
      container={equipped}
      locationOf={(def) => {
        if (selectedIsActive) return def.slot;
        if (def.area === "weapon") return 11;
        if (def.area === "offhand") return 12;
        return def.slot;
      }}
    />
  );
}

// Mercenary gear: weapon on the left, helm stacked over armor on the right.
const MERC_SLOTS: SlotDef[] = [
  { slot: 4, label: "Weapon", area: "weapon" },
  { slot: 1, label: "Helm", area: "helm" },
  { slot: 3, label: "Armor", area: "armor" },
];

const MERC_GRID_AREAS = `
  "weapon helm"
  "weapon armor"
`;

export function MercPaperdoll({ merc }: { merc: DisplayContainer }) {
  return (
    <Paperdoll
      slots={MERC_SLOTS}
      areas={MERC_GRID_AREAS}
      columns={2}
      container={merc}
    />
  );
}
