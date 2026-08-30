/**
 * The Inventory tab: equipment and mercenary paperdolls, then every storage grid in one panel.
 *
 * Shared by both views — it works off the display contract, so it neither knows nor cares which
 * stack resolved the sprites. What each view supplies is which grids exist and what to call them,
 * because that IS the difference: v1 sends a flat list keyed by an id string with one entry per
 * stash page, v2 sends named fields and a stash of pages.
 */

import { memo, useState } from "react";
import { Card, CardContent } from "@/components/ui";
import { PanelTitle } from "./CharacterChrome";
import { ContainerGrid } from "./ContainerGrid";
import {
  EquipmentPaperdoll,
  MercPaperdoll,
  WeaponSetToggle,
} from "./EquipmentPaperdoll";
import type { DisplayContainer } from "./contracts";

/** A named storage grid. The label is the caller's because only it knows how to name a stash page. */
export interface LabeledContainer {
  label: string;
  container: DisplayContainer;
}

/** Equipment panel: the paperdoll, plus (for expansion chars) the weapon-set toggle right-aligned
 *  in its title. Owns the user's set selection — key this by profile so it re-defaults to the
 *  active set per character but stays put as the active set flips live. */
function EquipmentCard({
  equipped,
  expansion,
  activeSet,
}: {
  equipped: DisplayContainer | undefined;
  expansion: boolean;
  activeSet: 0 | 1;
}) {
  const [selectedSet, setSelectedSet] = useState<0 | 1>(activeSet);
  return (
    <Card>
      <CardContent>
        <PanelTitle
          right={
            expansion ? (
              <WeaponSetToggle
                selectedSet={selectedSet}
                onSelect={setSelectedSet}
                activeSet={activeSet}
              />
            ) : undefined
          }
        >
          Equipment
        </PanelTitle>
        <EquipmentPaperdoll
          equipped={equipped}
          selectedSet={selectedSet}
          activeSet={activeSet}
        />
      </CardContent>
    </Card>
  );
}

/**
 * Memoised because it is by far the most expensive thing the viewer draws — a stash page alone is a
 * few hundred cells — and the shell above it re-renders on every capture change from ANY profile,
 * which with a manager full of bots is several a second. Every prop is identity-stable across one
 * of those: both views build the containers in a `useMemo` keyed on the capture, and the rest are
 * primitives, so the gear is reconciled only when the gear itself moves.
 */
export const InventoryTab = memo(function InventoryTab({
  profileKey,
  expansion,
  activeSet,
  equipped,
  merc,
  storage,
}: {
  /** Remounts the equipment card per character, so its set selection re-defaults. */
  profileKey: string;
  expansion: boolean;
  activeSet: 0 | 1;
  equipped: DisplayContainer | undefined;
  merc: DisplayContainer | undefined;
  storage: LabeledContainer[];
}) {
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-center gap-4">
        <EquipmentCard
          key={profileKey}
          equipped={equipped}
          expansion={expansion}
          activeSet={activeSet}
        />
        {merc && merc.items.length > 0 && (
          <Card>
            <CardContent>
              <PanelTitle>Mercenary</PanelTitle>
              <MercPaperdoll merc={merc} />
            </CardContent>
          </Card>
        )}
      </div>

      <Card>
        <CardContent>
          <PanelTitle>Items</PanelTitle>
          <div className="flex flex-wrap items-start justify-center gap-x-8 gap-y-6">
            {storage.map(({ label, container }) => (
              <div key={container.id} className="shrink-0">
                <div className="mb-1 whitespace-nowrap text-xs font-medium text-zinc-500">
                  {label}
                </div>
                <ContainerGrid container={container} />
              </div>
            ))}
          </div>
        </CardContent>
      </Card>
    </div>
  );
});
