/**
 * CharacterViewer - the "Character" tab. Selects a live character (from the
 * event stream) and renders, under sub-tabs, its equipment/items, stats &
 * skills, and quest/waypoint progression. Online status is derived live from the
 * owning profile's run state (keyed by profile name) rather than the persisted
 * flag, so the dot is always accurate even for stopped/offline characters.
 */

import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import clsx from "clsx";
import { TabGroup, TabList, Tab, TabPanels, TabPanel } from "@headlessui/react";
import { ChevronUpDownIcon, UserIcon } from "@heroicons/react/24/outline";
import { Card, CardContent, EmptyState } from "@/components/ui";
import { isActive } from "@/features/profiles/profile-states";
import { useCharacters, useProfiles } from "@/stores/event-store";
import type { Character, Container } from "@/generated/characters_pb";
import { StatsPanel } from "./StatsPanel";
import { SkillsPanel } from "./SkillsPanel";
import {
  EquipmentPaperdoll,
  MercPaperdoll,
  WeaponSetToggle,
} from "./EquipmentPaperdoll";
import { ContainerGrid } from "./ContainerGrid";
import { ProgressionPanel } from "./ProgressionPanel";
import { AnalyticsPanel } from "./AnalyticsPanel";
import { AREA_NAMES } from "./data/areaNames";

const TAB_CLASS =
  "rounded-md px-3 py-1.5 text-sm font-medium text-zinc-400 outline-none transition-colors hover:text-zinc-200 data-[selected]:bg-zinc-700 data-[selected]:text-zinc-100";

const CLASS_NAMES: Record<number, string> = {
  0: "Amazon",
  1: "Sorceress",
  2: "Necromancer",
  3: "Paladin",
  4: "Barbarian",
  5: "Druid",
  6: "Assassin",
};

const DIFFICULTY_NAMES: Record<number, string> = {
  0: "Normal",
  1: "Nightmare",
  2: "Hell",
};

function findContainer(
  character: Character,
  id: string,
): Container | undefined {
  return character.containers.find((c) => c.id === id);
}

function formatLastSeen(character: Character, online: boolean): string {
  if (online) return "Online";
  const seconds = character.updatedAt?.seconds;
  if (seconds === undefined) return "Offline";
  const when = new Date(Number(seconds) * 1000);
  return `Last seen ${when.toLocaleString()}`;
}

function formatDuration(totalSeconds: number): string {
  const s = totalSeconds % 60;
  const m = Math.floor(totalSeconds / 60) % 60;
  const h = Math.floor(totalSeconds / 3600);
  if (h > 0) return `${h}h ${m}m`;
  if (m > 0) return `${m}m ${s}s`;
  return `${s}s`;
}

/**
 * Live "time in current area" — ticks every second, isolated in its own
 * component so it doesn't re-render the whole viewer (grids etc.). Only rendered
 * while the character is online (a frozen counter would be misleading offline).
 */
function AreaTimer({ since }: { since?: { seconds: bigint } }) {
  const [, setTick] = useState(0);
  useEffect(() => {
    const id = setInterval(() => setTick((n) => n + 1), 1000);
    return () => clearInterval(id);
  }, []);
  if (!since) return null;
  const elapsed = Math.floor(Date.now() / 1000 - Number(since.seconds));
  if (elapsed < 0) return null;
  return <span className="text-zinc-600"> · {formatDuration(elapsed)}</span>;
}

/** Compact panel heading (replaces the heavy CardHeader to save vertical space).
 *  Optional right-aligned slot holds panel-level controls (e.g. the weapon set). */
function PanelTitle({
  children,
  right,
}: {
  children: string;
  right?: ReactNode;
}) {
  return (
    <div className="mb-2 flex items-center justify-between gap-2">
      <h3 className="text-xs font-semibold uppercase tracking-wide text-zinc-400">
        {children}
      </h3>
      {right}
    </div>
  );
}

/** Equipment panel: the paperdoll, plus (for expansion chars) the weapon-set
 *  toggle right-aligned in its title. Owns the user's set selection — key this by
 *  profile so it re-defaults to the active set per character but stays put as the
 *  active set flips live. */
function EquipmentCard({
  equipped,
  expansion,
  activeSet,
}: {
  equipped: Container | undefined;
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

function ModeBadges({ character }: { character: Character }) {
  const mode = character.mode;
  if (!mode) return null;
  return (
    <div className="flex gap-1">
      {mode.hardcore && (
        <span className="rounded bg-red-900/50 px-1 py-0.5 text-[10px] font-medium text-red-300">
          HC
        </span>
      )}
      {mode.ladder && (
        <span className="rounded bg-green-900/50 px-1 py-0.5 text-[10px] font-medium text-green-300">
          Ladder
        </span>
      )}
      {!mode.expansion && (
        <span className="rounded bg-blue-900/50 px-1 py-0.5 text-[10px] font-medium text-blue-300">
          Classic
        </span>
      )}
    </div>
  );
}

/** A labeled storage grid for the consolidated Items panel. */
function LabeledGrid({
  label,
  container,
}: {
  label: string;
  container: Container;
}) {
  return (
    <div className="shrink-0">
      <div className="mb-1 whitespace-nowrap text-xs font-medium text-zinc-500">
        {label}
      </div>
      <ContainerGrid container={container} />
    </div>
  );
}

/**
 * The character name, doubling as the character selector: clicking it opens a
 * searchable dropdown of all known characters. Embedded in the header line so it
 * doesn't cost a separate row.
 */
function CharacterSelector({
  characters,
  selected,
  onSelect,
  isOnline,
}: {
  characters: Character[];
  selected: Character;
  onSelect: (profile: string) => void;
  isOnline: (profile: string) => boolean;
}) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (
        containerRef.current &&
        !containerRef.current.contains(e.target as Node)
      ) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return characters;
    return characters.filter((c) =>
      [c.charName, c.profile, c.account, c.realm].some((s) =>
        s.toLowerCase().includes(q),
      ),
    );
  }, [characters, search]);

  function handleSelect(profile: string) {
    onSelect(profile);
    setOpen(false);
    setSearch("");
  }

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="group -mx-1 flex items-center gap-2 rounded px-1 hover:bg-zinc-800/60 focus:outline-none"
      >
        <span
          className={clsx(
            "h-2.5 w-2.5 flex-shrink-0 rounded-full",
            isOnline(selected.profile) ? "bg-green-500" : "bg-zinc-600",
          )}
        />
        <h2 className="text-xl font-bold text-zinc-100">
          {selected.charName || selected.profile}
        </h2>
        <ChevronUpDownIcon className="h-5 w-5 flex-shrink-0 text-zinc-500 group-hover:text-zinc-300" />
      </button>

      {open && (
        <div className="absolute left-0 z-50 mt-1 max-h-80 w-72 overflow-hidden rounded-lg bg-zinc-800 shadow-lg ring-1 ring-zinc-700">
          <div className="border-b border-zinc-700 p-2">
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search characters..."
              className="block w-full rounded border-0 bg-zinc-900 px-2 py-1.5 text-sm text-zinc-100 placeholder:text-zinc-500 focus:outline-none focus:ring-1 focus:ring-d2-gold"
              autoFocus
            />
          </div>
          <div className="max-h-60 overflow-y-auto p-1">
            {filtered.map((c) => (
              <button
                key={c.profile}
                type="button"
                onClick={() => handleSelect(c.profile)}
                className={clsx(
                  "flex w-full items-center gap-2 rounded px-2 py-1.5 text-left text-sm transition-colors",
                  c.profile === selected.profile
                    ? "bg-d2-gold/20 text-d2-gold"
                    : "text-zinc-300 hover:bg-zinc-700",
                )}
              >
                <span
                  className={clsx(
                    "h-2 w-2 flex-shrink-0 rounded-full",
                    isOnline(c.profile) ? "bg-green-500" : "bg-zinc-600",
                  )}
                />
                <span className="flex-1 truncate">
                  {c.charName || c.profile}
                </span>
                {(c.account || c.realm) && (
                  <span className="truncate text-xs text-zinc-500">
                    {[c.account, c.realm].filter(Boolean).join(" · ")}
                  </span>
                )}
              </button>
            ))}
            {filtered.length === 0 && (
              <p className="px-2 py-3 text-center text-sm text-zinc-500">
                No matches found
              </p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

export function CharacterViewer() {
  const characters = useCharacters();
  const profiles = useProfiles();

  // Online is derived from the owning profile's run state (keyed by name), so it
  // tracks start/stop live and never goes stale on a persisted character.
  const onlineProfiles = useMemo(
    () =>
      new Set(
        profiles
          .filter((p) => isActive(p.status?.state))
          .map((p) => p.profile.name),
      ),
    [profiles],
  );
  const isOnline = (profile: string) => onlineProfiles.has(profile);

  const [selectedProfile, setSelectedProfile] = useState<string | null>(null);
  const selected = useMemo(() => {
    if (characters.length === 0) return undefined;
    return (
      characters.find((c) => c.profile === selectedProfile) ?? characters[0]
    );
  }, [characters, selectedProfile]);

  if (!selected) {
    return (
      <EmptyState
        icon={UserIcon}
        title="No live characters yet"
        description="Character state appears here once a running profile reports it. Start a profile to begin tracking."
      />
    );
  }

  const online = isOnline(selected.profile);
  const expansion = selected.mode?.expansion ?? false;
  // Active weapon set from the snapshot's top-level `hand` (0 primary / 1 secondary).
  // The WeaponSwitch char flag is lobby-only, so `hand` is the live in-game source.
  const activeSet: 0 | 1 = selected.hand === 1 ? 1 : 0;
  const areaName = AREA_NAMES[selected.area];
  const equipped = findContainer(selected, "equipped");
  const merc = findContainer(selected, "merc");
  const inventory = findContainer(selected, "inventory");
  const cube = findContainer(selected, "cube");
  const belt = findContainer(selected, "belt");
  const stashPages = selected.containers
    .filter((c) => c.id === "stash")
    .sort((a, b) => a.page - b.page);

  return (
    <div className="space-y-4">
      {/* Header — the character name doubles as the selector */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <CharacterSelector
          characters={characters}
          selected={selected}
          onSelect={setSelectedProfile}
          isOnline={isOnline}
        />
        <span className="text-sm text-zinc-400">
          Level {selected.level}
          {CLASS_NAMES[selected.charClass]
            ? ` ${CLASS_NAMES[selected.charClass]}`
            : ""}
        </span>
        {DIFFICULTY_NAMES[selected.difficulty] && (
          <span className="text-xs text-zinc-500">
            {DIFFICULTY_NAMES[selected.difficulty]}
          </span>
        )}
        {areaName && (
          <span className="text-xs text-zinc-400">
            {areaName}
            {/* The timer only renders when areaEnteredAt is set; the backend clears
                it on load and stamps it only on a real game/area entry, so it never
                counts from a stale, previous-session value. */}
            {online && <AreaTimer since={selected.areaEnteredAt} />}
          </span>
        )}
        <ModeBadges character={selected} />
        {(selected.account || selected.realm) && (
          <span className="text-xs text-zinc-500">
            {[selected.account, selected.realm].filter(Boolean).join(" · ")}
          </span>
        )}
        <span className="ml-auto text-xs text-zinc-500">
          {formatLastSeen(selected, online)}
        </span>
      </div>

      <TabGroup>
        <TabList className="inline-flex flex-wrap gap-1 rounded-lg bg-zinc-800/60 p-1">
          <Tab className={TAB_CLASS}>Inventory</Tab>
          <Tab className={TAB_CLASS}>Stats &amp; Skills</Tab>
          <Tab className={TAB_CLASS}>Progression</Tab>
          <Tab className={TAB_CLASS}>Analytics</Tab>
        </TabList>
        <TabPanels className="mt-4">
          {/* Inventory: equipment + merc paperdolls, then all stored items in one full-width panel.
              Kept mounted so the weapon-set toggle selection survives tab switches. */}
          <TabPanel unmount={false}>
            <div className="space-y-4">
              <div className="flex flex-wrap items-start justify-center gap-4">
                <EquipmentCard
                  key={selected.profile}
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
                    {inventory && (
                      <LabeledGrid label="Inventory" container={inventory} />
                    )}
                    {cube && (
                      <LabeledGrid label="Horadric Cube" container={cube} />
                    )}
                    {belt && <LabeledGrid label="Belt" container={belt} />}
                    {stashPages.map((page) => (
                      <LabeledGrid
                        key={page.page}
                        label={
                          stashPages.length > 1
                            ? page.name || `Stash ${page.page + 1}`
                            : "Stash"
                        }
                        container={page}
                      />
                    ))}
                  </div>
                </CardContent>
              </Card>
            </div>
          </TabPanel>

          {/* Stats + skills */}
          <TabPanel>
            <div className="space-y-4">
              <Card>
                <CardContent>
                  <PanelTitle>Stats</PanelTitle>
                  <StatsPanel character={selected} />
                </CardContent>
              </Card>
              <Card>
                <CardContent>
                  <PanelTitle>Skills</PanelTitle>
                  <SkillsPanel character={selected} />
                </CardContent>
              </Card>
            </div>
          </TabPanel>

          {/* Progression: quests + waypoints, per difficulty (defaults to current) */}
          <TabPanel>
            <Card>
              <CardContent>
                <ProgressionPanel key={selected.profile} character={selected} />
              </CardContent>
            </Card>
          </TabPanel>

          {/* Analytics: time-in-area + monster/super-unique kills, per difficulty */}
          <TabPanel>
            <Card>
              <CardContent>
                <AnalyticsPanel key={selected.profile} character={selected} />
              </CardContent>
            </Card>
          </TabPanel>
        </TabPanels>
      </TabGroup>
    </div>
  );
}
