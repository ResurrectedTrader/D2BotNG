/**
 * EquipmentPaperdoll / MercPaperdoll - equipped items laid out by equip slot
 * (sent in item.x; y is always 0 for slot containers). v1 uses labeled slot
 * boxes (zero assets). Helm sits above the body armor (center column).
 *
 * Expansion characters have a second weapon set. The game reports the *active*
 * set in equip locations 4/5 and the inactive one in 11/12; which set is active
 * comes from the snapshot's top-level `hand` field (passed in as `activeSet`),
 * because the WeaponSwitch char flag is only valid in the lobby. A I/II toggle
 * picks which set to view (defaulting to the active one, with the active set
 * marked); the selection is the user's and stays put as the active set flips live.
 */

import { useState } from "react";
import clsx from "clsx";
import type { Container } from "@/generated/characters_pb";
import type { Item } from "@/generated/items_pb";
import { ItemImage, ItemTooltip, useItemContextMenu } from "@/features/items";

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
// The top-left cell is empty (the weapon-set toggle now lives in the panel title).
const GRID_AREAS = `
  ".      helm   amulet"
  "weapon armor  offhand"
  "ringL  belt   ringR"
  "gloves .      boots"
`;

function Slot({
  area,
  label,
  item,
}: {
  area: string;
  label: string;
  item: Item | undefined;
}) {
  const [hovered, setHovered] = useState(false);
  const { contextMenu, onContextMenu } = useItemContextMenu({ item });
  return (
    <div
      style={{ gridArea: area }}
      className="flex min-h-[72px] items-center justify-center rounded bg-zinc-900/40 ring-1 ring-zinc-800"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onContextMenu={item ? onContextMenu : undefined}
    >
      {item ? (
        <ItemTooltip item={item} showSprite={false}>
          <div>
            <ItemImage item={item} size="lg" showSockets={hovered} />
          </div>
        </ItemTooltip>
      ) : (
        <span className="text-[10px] uppercase tracking-wide text-zinc-600">
          {label}
        </span>
      )}
      {item && contextMenu}
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
    <div className="inline-flex gap-0.5 rounded-md bg-zinc-800/60 p-0.5 text-xs">
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
          {set === activeSet && (
            <span className="absolute right-0.5 top-0.5 h-1.5 w-1.5 rounded-full bg-green-500" />
          )}
        </button>
      ))}
    </div>
  );
}

export function EquipmentPaperdoll({
  equipped,
  selectedSet,
  activeSet,
}: {
  equipped: Container | undefined;
  selectedSet: 0 | 1;
  activeSet: 0 | 1;
}) {
  // The active set's weapons sit in 4/5; the inactive set's in 11/12.
  const selectedIsActive = selectedSet === activeSet;
  const weaponSlot = selectedIsActive ? 4 : 11;
  const offhandSlot = selectedIsActive ? 5 : 12;

  const bySlot = new Map<number, Item>();
  for (const item of equipped?.items ?? []) bySlot.set(item.x, item);

  return (
    <div
      className="grid w-fit gap-2"
      style={{
        gridTemplateAreas: GRID_AREAS,
        gridTemplateColumns: "repeat(3, 4.5rem)",
      }}
    >
      {SLOTS.map((def) => {
        const slotId =
          def.area === "weapon"
            ? weaponSlot
            : def.area === "offhand"
              ? offhandSlot
              : def.slot;
        return (
          <Slot
            key={def.area}
            area={def.area}
            label={def.label}
            item={bySlot.get(slotId)}
          />
        );
      })}
    </div>
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

export function MercPaperdoll({ merc }: { merc: Container }) {
  const bySlot = new Map<number, Item>();
  for (const item of merc.items) bySlot.set(item.x, item);

  return (
    <div
      className="grid w-fit gap-2"
      style={{
        gridTemplateAreas: MERC_GRID_AREAS,
        gridTemplateColumns: "repeat(2, 4.5rem)",
      }}
    >
      {MERC_SLOTS.map((def) => (
        <Slot
          key={def.area}
          area={def.area}
          label={def.label}
          item={bySlot.get(def.slot)}
        />
      ))}
    </div>
  );
}
