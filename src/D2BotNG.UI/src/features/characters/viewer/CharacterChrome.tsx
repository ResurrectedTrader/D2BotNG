/**
 * The frame both character views render inside: the header line, the four sub-tabs, and the small
 * controls the panels inside them share.
 *
 * Everything here reads plain facts, so it is shared rather than duplicated per stack. What goes
 * IN the tabs is the caller's, because that is where the two schemas actually differ.
 */

import { useEffect, useState, type ReactNode } from "react";
import { TabGroup, TabList, Tab, TabPanels, TabPanel } from "@headlessui/react";
import clsx from "clsx";
import { Card, CardContent } from "@/components/ui";
import { DIFFICULTY_NAMES, type CharacterFacts } from "./contracts";
import { AREA_NAMES } from "./data/areaNames";
import { useGameNames } from "./gameNames";

const TAB_CLASS =
  "rounded-md px-3 py-1.5 text-sm font-medium text-zinc-400 outline-none transition-colors hover:text-zinc-200 data-[selected]:bg-zinc-700 data-[selected]:text-zinc-100";

/** A span of time as the viewer writes it everywhere: "1h 23m", "4m 12s", "9s". */
export function formatDuration(totalSeconds: number): string {
  const s = totalSeconds % 60;
  const m = Math.floor(totalSeconds / 60) % 60;
  const h = Math.floor(totalSeconds / 3600);
  if (h > 0) return `${h}h ${m}m`;
  if (m > 0) return `${m}m ${s}s`;
  return `${s}s`;
}

function formatLastSeen(
  updatedAt: { seconds: bigint } | undefined,
  online: boolean,
): string {
  if (online) return "Online";
  if (updatedAt === undefined) return "Offline";
  const when = new Date(Number(updatedAt.seconds) * 1000);
  return `Last seen ${when.toLocaleString()}`;
}

/**
 * Live "time in current area" — ticks every second, isolated in its own component so it doesn't
 * re-render the whole viewer (grids etc.). Only rendered while the character is online: a frozen
 * counter would be misleading offline.
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

function ModeBadges({ facts }: { facts: CharacterFacts }) {
  return (
    <div className="flex gap-1">
      {facts.hardcore && (
        <span className="rounded bg-red-900/50 px-1 py-0.5 text-[10px] font-medium text-red-300">
          HC
        </span>
      )}
      {facts.ladder && (
        <span className="rounded bg-green-900/50 px-1 py-0.5 text-[10px] font-medium text-green-300">
          Ladder
        </span>
      )}
      {!facts.expansion && (
        <span className="rounded bg-blue-900/50 px-1 py-0.5 text-[10px] font-medium text-blue-300">
          Classic
        </span>
      )}
    </div>
  );
}

/**
 * The shell every segmented picker in the viewer sits in — the difficulty tabs and the weapon-set
 * toggle. Shared so two controls that are meant to read as the same kind of thing cannot drift
 * apart; what goes inside is each control's own, since their buttons are sized differently.
 */
export function SegmentedControl({ children }: { children: ReactNode }) {
  return (
    <div className="inline-flex gap-0.5 rounded-md bg-zinc-800/60 p-0.5 text-xs">
      {children}
    </div>
  );
}

/** Marks the option that is live right now, as opposed to the one being looked at. */
export function ActiveDot() {
  return (
    <span className="absolute right-0.5 top-0.5 h-1.5 w-1.5 rounded-full bg-green-500" />
  );
}

/**
 * The difficulty picker, shared by the Progression and Analytics tabs.
 *
 * A difficulty with nothing behind it is disabled rather than dropped, so the set of three stays
 * put and the reader can see there is nothing there. What counts as "nothing" is the caller's:
 * one panel has quest records and the other has kills and time in area. The active difficulty
 * stays selectable either way — it is where the character is now, even before its first report.
 */
export function DifficultySelector({
  selected,
  onSelect,
  activeDifficulty,
  hasData,
}: {
  selected: number;
  onSelect: (difficulty: number) => void;
  activeDifficulty: number;
  hasData: (difficulty: number) => boolean;
}) {
  return (
    <SegmentedControl>
      {DIFFICULTY_NAMES.map((name, id) => {
        const enabled = id === activeDifficulty || hasData(id);
        return (
          <button
            key={id}
            type="button"
            disabled={!enabled}
            onClick={() => onSelect(id)}
            title={
              id === activeDifficulty
                ? "Last seen difficulty"
                : enabled
                  ? undefined
                  : "No data"
            }
            className={clsx(
              "relative rounded px-3 py-1 font-medium",
              !enabled
                ? "cursor-not-allowed text-zinc-600"
                : selected === id
                  ? "bg-zinc-700 text-zinc-100"
                  : "text-zinc-400 hover:text-zinc-200",
            )}
          >
            {name}
            {id === activeDifficulty && <ActiveDot />}
          </button>
        );
      })}
    </SegmentedControl>
  );
}

/** Compact panel heading (replaces the heavy CardHeader to save vertical space).
 *  Optional right-aligned slot holds panel-level controls (e.g. the weapon set). */
export function PanelTitle({
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

export function CharacterChrome({
  facts,
  online,
  selector,
  inventory,
  statsAndSkills,
  progression,
  analytics,
}: {
  facts: CharacterFacts;
  online: boolean;
  /** The name, which doubles as the character picker — owned by the shell, not by either view. */
  selector: ReactNode;
  inventory: ReactNode;
  statsAndSkills: ReactNode;
  progression: ReactNode;
  analytics: ReactNode;
}) {
  const names = useGameNames();
  const className = names.characterClass(facts.charClass);
  const areaName = AREA_NAMES[facts.area];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        {selector}
        <span className="text-sm text-zinc-400">
          Level {facts.level}
          {className ? ` ${className}` : ""}
        </span>
        {DIFFICULTY_NAMES[facts.difficulty] && (
          <span className="text-xs text-zinc-500">
            {DIFFICULTY_NAMES[facts.difficulty]}
          </span>
        )}
        {areaName && (
          <span className="text-xs text-zinc-400">
            {areaName}
            {/* The timer only renders when areaEnteredAt is set; the backend clears it on load
                and stamps it only on a real game/area entry, so it never counts from a stale,
                previous-session value. */}
            {online && <AreaTimer since={facts.areaEnteredAt} />}
          </span>
        )}
        <ModeBadges facts={facts} />
        {(facts.account || facts.realm) && (
          <span className="text-xs text-zinc-500">
            {[facts.account, facts.realm].filter(Boolean).join(" · ")}
          </span>
        )}
        <span className="ml-auto text-xs text-zinc-500">
          {formatLastSeen(facts.updatedAt, online)}
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
          {/* Kept mounted so the weapon-set toggle selection survives tab switches. */}
          <TabPanel unmount={false}>{inventory}</TabPanel>
          <TabPanel>{statsAndSkills}</TabPanel>
          <TabPanel>
            <Card>
              <CardContent>{progression}</CardContent>
            </Card>
          </TabPanel>
          <TabPanel>
            <Card>
              <CardContent>{analytics}</CardContent>
            </Card>
          </TabPanel>
        </TabPanels>
      </TabGroup>
    </div>
  );
}
