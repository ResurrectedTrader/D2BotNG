/**
 * The Inventory tab: equipment and mercenary paperdolls, then every storage grid in one panel,
 * with the stash behind a page picker — one page shown at a time, grouped by kind.
 *
 * Shared by both views — it works off the display contract, so it neither knows nor cares which
 * stack resolved the sprites. What each view supplies is which grids exist and what to call them,
 * because that IS the difference: v1 sends a flat list keyed by an id string with one entry per
 * stash page, v2 sends named fields and a stash of pages that carry their kind, type and gold.
 *
 * Gold is shown where it lives: what the character carries beneath the equipment, and what a
 * stash page holds beneath that page. The stats tab keeps the same two figures as rows, so a
 * reader looking for a number finds it in either place.
 */

import { memo, useEffect, useState } from "react";
import { ChevronLeftIcon, ChevronRightIcon } from "@heroicons/react/24/outline";
import { Card, CardContent } from "@/components/ui";
import { useTooltipTextStyle } from "@/features/items";
import { SpriteStyle } from "@/generated/settings_pb";
import { useSpriteStyle } from "@/lib/rendering";
import { useUiPanels } from "@/lib/rendering/hdUi";
import { PanelTitle } from "./CharacterChrome";
import { useGameNames } from "./gameNames";
import { ContainerGrid, GRID_SURFACE_STYLE } from "./ContainerGrid";
import {
  EquipmentPaperdoll,
  MercPaperdoll,
  WeaponSetToggle,
} from "./EquipmentPaperdoll";
import {
  cubeFitsPanel,
  GameCubePanel,
  GameInventoryPanel,
  GameMercPanel,
  GameSmallStashPanel,
  GameStashPanel,
  inventoryFitsPanel,
  smallStashLayout,
  stashFitsPanel,
} from "./GamePanels";
import {
  CONTAINER_LABELS,
  STASH_KIND_LABELS,
  STASH_PAGE_TYPE_LABELS,
  stashPageLabel,
  type DisplayContainer,
  type DisplayIdentity,
  type DisplayMercInfo,
  type DisplayStashPage,
  type StashKind,
} from "./contracts";

/** A named storage grid. The label is the caller's because only it knows what to call a grid. */
export interface LabeledContainer {
  label: string;
  container: DisplayContainer;
}

/** The kinds in the order the game lists them: a character's own pages before the account's. */
const STASH_KIND_ORDER: StashKind[] = ["personal", "shared"];

/** The previous/next arrows beside the page picker. */
const STEP_BUTTON_CLASS =
  "rounded p-1 text-zinc-400 hover:text-zinc-200 disabled:cursor-not-allowed disabled:text-zinc-700 disabled:hover:text-zinc-700";

/**
 * A gold figure under a grid or paperdoll, drawn the way the game draws it: the bare number in
 * the tooltip lettering, centred in a black readout. Always drawn, since "none" is a fact too.
 */
function GoldLine({ gold }: { gold: number }) {
  const textStyle = useTooltipTextStyle();
  return (
    <div className="mt-1 flex justify-center">
      <div
        className="min-w-24 px-3 py-0.5 text-center text-sm leading-tight text-d2-gold"
        // The grid's own wash and border, so the readout reads as part of it.
        style={{ ...textStyle, ...GRID_SURFACE_STYLE }}
        title="Gold"
      >
        {gold.toLocaleString()}
      </div>
    </div>
  );
}

/** Equipment panel: the paperdoll, plus (for expansion chars) the weapon-set toggle right-aligned
 *  in its title. Owns the user's set selection — key this by profile so it re-defaults to the
 *  active set per character but stays put as the active set flips live. */
function EquipmentCard({
  equipped,
  expansion,
  activeSet,
  gold,
  identity,
}: {
  equipped: DisplayContainer | undefined;
  expansion: boolean;
  activeSet: 0 | 1;
  gold: number;
  identity: DisplayIdentity;
}) {
  const [selectedSet, setSelectedSet] = useState<0 | 1>(activeSet);
  const className = useGameNames().characterClass(identity.charClass);
  const title = [
    identity.name,
    `Level ${identity.level}${className ? ` ${className}` : ""}`,
  ]
    .filter(Boolean)
    .join(" · ");
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
          {title}
        </PanelTitle>
        <EquipmentPaperdoll
          equipped={equipped}
          selectedSet={selectedSet}
          activeSet={activeSet}
        />
        <GoldLine gold={gold} />
      </CardContent>
    </Card>
  );
}

/**
 * What qualifies a page beyond its name: its type, when not the plain one, and a previous
 * season's remove-only page. Undefined for an ordinary page, which needs no qualifier.
 */
function pageNote(page: DisplayStashPage): string | undefined {
  const notes = [
    STASH_PAGE_TYPE_LABELS[page.type],
    page.removeOnly
      ? page.season > 0
        ? `Past season ${page.season}`
        : "Past season"
      : undefined,
  ].filter(Boolean);
  return notes.length > 0 ? notes.join(" · ") : undefined;
}

/** A page's identity, as a React key and as the selection. */
function pageKey(page: DisplayStashPage): string {
  return `${page.kind}-${page.index}`;
}

/**
 * The stash: a page picker, the selected page's grid, and that page's gold.
 *
 * One page at a time rather than every page side by side, because a PlugY stash can run to
 * dozens of pages of a few hundred cells each, and because that is how the game itself shows it.
 * The picker is a dropdown grouped by kind with previous/next arrows beside it, not a row of
 * tabs: pages carry names, and a row of a dozen named tabs was wider than the grid it selected
 * for. The picker is skipped for a lone page — one choice is a heading with extra chrome.
 *
 * Owns the selection; keyed by profile from the caller so it resets per character. A selection
 * that no longer exists (the page was deleted, or the character changed under a shared key) falls
 * back to the first page rather than to nothing.
 */
function StashBlock({ pages }: { pages: DisplayStashPage[] }) {
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const selectedExists = pages.some((p) => pageKey(p) === selectedKey);
  const selected = selectedExists
    ? pages.find((p) => pageKey(p) === selectedKey)!
    : pages[0];

  // Commit the fallback. Left as it was, a page that came back after being dropped would
  // snap the view back to it, since the stale key would match again.
  useEffect(() => {
    if (selectedKey !== null && !selectedExists) setSelectedKey(null);
  }, [selectedKey, selectedExists]);

  if (!selected) return null;

  // The arrows walk the pages in list order — every personal page, then every shared one — so
  // stepping off the end of one kind lands on the start of the next.
  const position = pages.indexOf(selected);
  const selectedTypeNote = pageNote(selected);
  const groups = STASH_KIND_ORDER.map((kind) => ({
    kind,
    pages: pages.filter((p) => p.kind === kind),
  })).filter((g) => g.pages.length > 0);

  return (
    <div className="shrink-0">
      <div className="mb-1 whitespace-nowrap text-xs font-medium text-zinc-500">
        {CONTAINER_LABELS.stash}
      </div>
      {pages.length > 1 && (
        <div className="mb-2 flex items-center gap-1">
          <button
            type="button"
            aria-label="Previous stash page"
            disabled={position === 0}
            onClick={() => setSelectedKey(pageKey(pages[position - 1]))}
            className={STEP_BUTTON_CLASS}
          >
            <ChevronLeftIcon className="h-4 w-4" />
          </button>
          <select
            aria-label="Stash page"
            value={pageKey(selected)}
            onChange={(e) => setSelectedKey(e.target.value)}
            title={
              selectedTypeNote
                ? `${stashPageLabel(selected)} · ${selectedTypeNote}`
                : undefined
            }
            className="min-w-0 flex-1 rounded-md border-0 bg-zinc-800 py-1 pl-2 pr-7 text-xs text-zinc-100 ring-1 ring-inset ring-zinc-700 focus:ring-2 focus:ring-inset focus:ring-d2-gold"
          >
            {groups.map((group) => (
              <optgroup key={group.kind} label={STASH_KIND_LABELS[group.kind]}>
                {group.pages.map((page) => {
                  const typeNote = pageNote(page);
                  const label = stashPageLabel(page);
                  return (
                    <option key={pageKey(page)} value={pageKey(page)}>
                      {typeNote ? `${label} · ${typeNote}` : label}
                    </option>
                  );
                })}
              </optgroup>
            ))}
          </select>
          <button
            type="button"
            aria-label="Next stash page"
            disabled={position === pages.length - 1}
            onClick={() => setSelectedKey(pageKey(pages[position + 1]))}
            className={STEP_BUTTON_CLASS}
          >
            <ChevronRightIcon className="h-4 w-4" />
          </button>
        </div>
      )}
      <ContainerGrid container={selected.container} />
      <GoldLine gold={selected.gold} />
    </div>
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
  gold,
  equipped,
  merc,
  storage,
  stash,
  identity,
  mercInfo,
}: {
  /** Remounts the equipment card and the stash per character, so their selections re-default. */
  profileKey: string;
  /** Who the character is, named at the top of its panel. */
  identity: DisplayIdentity;
  /** The living mercenary's name, level and kind, for its panel's band; absent when unknown. */
  mercInfo?: DisplayMercInfo;
  expansion: boolean;
  activeSet: 0 | 1;
  /** Gold carried on the character. */
  gold: number;
  equipped: DisplayContainer | undefined;
  merc: DisplayContainer | undefined;
  storage: LabeledContainer[];
  /** Sorted by kind then index; empty until a stash is reported. */
  stash: DisplayStashPage[];
}) {
  // In the D2R art style the game's own panels draw what they were drawn for: the paperdoll and
  // inventory, the mercenary, a 10x10 stash, and the composed classic (6x4) and LoD (6x8)
  // stashes. Anything else keeps the plain grids.
  const style = useSpriteStyle();
  const loaded = useUiPanels();
  const panels = style === SpriteStyle.D2R ? loaded : null;
  const inventory = storage.find((s) => s.container.id === "inventory");
  const cube = storage.find((s) => s.container.id === "cube");
  const inventoryPanel =
    panels && inventoryFitsPanel(panels, inventory?.container) ? panels : null;
  const stashPanel =
    panels && stash.length > 0 && stashFitsPanel(panels, stash) ? panels : null;
  const smallStashPanel =
    panels && !stashPanel && smallStashLayout(stash) ? panels : null;
  // A mercenary is reported only while it is alive. The cube goes under a living mercenary,
  // else on its own panel — never on RotW's advanced stash pages, where the game keeps it, so it
  // is always in one place whatever the game.
  const cubeFits = !!panels && cubeFitsPanel(panels, cube?.container);
  const cubeUnderMerc = cubeFits && merc !== undefined;
  const cubePanel = cubeFits && !cubeUnderMerc ? panels : null;
  const cubeDrawn = cubeUnderMerc || cubePanel !== null;
  // The belt is left out: the game shows it on the HUD, not on any of these panels.
  const grids = storage.filter(
    (s) =>
      s.container.id !== "belt" &&
      !(inventoryPanel && s === inventory) &&
      !(cubeDrawn && s === cube),
  );
  const stashOnPanel = stashPanel !== null || smallStashPanel !== null;

  if (panels) {
    return (
      <div className="space-y-4">
        <div className="flex flex-wrap items-start justify-center gap-4">
          {stashPanel && (
            <GameStashPanel
              key={`stash-${profileKey}`}
              panels={stashPanel}
              pages={stash}
            />
          )}
          {smallStashPanel && (
            <GameSmallStashPanel
              key={`stash-${profileKey}`}
              panels={smallStashPanel}
              pages={stash}
            />
          )}
          {/* Always stash, then the character, then the mercenary with the cube or the cube alone. */}
          {inventoryPanel ? (
            <GameInventoryPanel
              key={profileKey}
              panels={inventoryPanel}
              equipped={equipped}
              inventory={inventory?.container}
              gold={gold}
              expansion={expansion}
              activeSet={activeSet}
              identity={identity}
            />
          ) : (
            <EquipmentCard
              key={profileKey}
              equipped={equipped}
              expansion={expansion}
              activeSet={activeSet}
              gold={gold}
              identity={identity}
            />
          )}
          {merc && (
            <GameMercPanel
              panels={panels}
              merc={merc}
              cube={cubeUnderMerc ? cube?.container : undefined}
              info={mercInfo}
            />
          )}
          {cubePanel && cube && (
            <GameCubePanel panels={cubePanel} cube={cube.container} />
          )}
        </div>
        {(grids.length > 0 || (!stashOnPanel && stash.length > 0)) && (
          <Card>
            <CardContent>
              <PanelTitle>Items</PanelTitle>
              <div className="flex flex-wrap items-start justify-center gap-x-8 gap-y-6">
                {grids.map(({ label, container }) => (
                  <div key={container.id} className="shrink-0">
                    <div className="mb-1 whitespace-nowrap text-xs font-medium text-zinc-500">
                      {label}
                    </div>
                    <ContainerGrid container={container} />
                  </div>
                ))}
                {!stashOnPanel && stash.length > 0 && (
                  <StashBlock key={profileKey} pages={stash} />
                )}
              </div>
            </CardContent>
          </Card>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-center gap-4">
        <EquipmentCard
          key={profileKey}
          equipped={equipped}
          expansion={expansion}
          activeSet={activeSet}
          gold={gold}
          identity={identity}
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
            {stash.length > 0 && <StashBlock key={profileKey} pages={stash} />}
          </div>
        </CardContent>
      </Card>
    </div>
  );
});
