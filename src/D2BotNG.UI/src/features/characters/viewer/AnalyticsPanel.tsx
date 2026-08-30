/**
 * AnalyticsPanel - the "Analytics" tab. Per-difficulty game analytics for the character:
 * lifetime time spent per area, and lifetime monster kills (regular monsters by class
 * with a by-rarity summary, plus super-uniques by name). A shared difficulty selector
 * drives both sections; each has its own reset. Ids resolve via the generated name
 * tables; the manager accumulates both from the engine's per-game reports.
 */

import { useCallback, useMemo, useState } from "react";
import { ConfirmationDialog } from "@/components/ui";
import { DifficultySelector, formatDuration } from "./CharacterChrome";
import type { AreaDuration, KillCount } from "./contracts";
import { useGameNames, type GameNames } from "./gameNames";
import { SUPER_UNIQUE_NAMES } from "./data/superUniqueNames";
import { AREA_NAMES } from "./data/areaNames";

// SpecType bitfield (engine SPECTYPE_* in Unit.cpp). A champion also carries the boss
// (unique) bit, so a value can be 6 = champion+boss. Classify each kill into ONE rarity
// by priority (champion wins over the bare unique bit) rather than OR-ing the flags into
// misleading combos like "Champion + Unique".
const RARITY_ORDER = [
  "Normal",
  "Champion",
  "Unique",
  "Minion",
  "Super Unique",
] as const;

function rarityOf(spec: number): (typeof RARITY_ORDER)[number] {
  if (spec & 0x01) return "Super Unique";
  if (spec & 0x02) return "Champion"; // champions also have the boss/unique bit set
  if (spec & 0x04) return "Unique";
  if (spec & 0x08) return "Minion";
  return "Normal";
}

/** Area time is stored in milliseconds; the viewer's duration format takes seconds. */
const formatMs = (ms: number) => formatDuration(Math.floor(ms / 1000));

type KillRow = {
  id: number;
  name: string;
  count: number;
  breakdown?: { uniq: number; champ: number; minion: number };
};
type AreaRow = { id: number; name: string; ms: number };

function areaRows(areaTime: AreaDuration[], difficulty: number): AreaRow[] {
  const byArea = new Map<number, number>();
  for (const a of areaTime) {
    if (a.difficulty !== difficulty) continue;
    if (!AREA_NAMES[a.area]) continue; // skip ids we can't name
    byArea.set(a.area, (byArea.get(a.area) ?? 0) + Number(a.milliseconds));
  }
  return [...byArea]
    .filter(([, ms]) => ms > 0)
    .map(([id, ms]) => ({ id, name: AREA_NAMES[id], ms }))
    .sort((a, b) => b.ms - a.ms);
}

function classRows(
  kills: KillCount[],
  difficulty: number,
  names: GameNames,
): KillRow[] {
  const byClass = new Map<number, KillRow>();
  for (const k of kills) {
    if (k.difficulty !== difficulty || k.superUnique) continue;
    const name = names.monster(k.id);
    if (!name) continue; // skip classes we can't name (internal dummies)
    const row = byClass.get(k.id) ?? {
      id: k.id,
      name,
      count: 0,
      breakdown: { uniq: 0, champ: 0, minion: 0 },
    };
    // Aggregate the class total plus a by-rarity breakdown (SpecType is a bitfield).
    const n = Number(k.count);
    row.count += n;
    const rarity = rarityOf(k.spec); // one bucket per kill (champion != unique)
    if (rarity === "Champion") row.breakdown!.champ += n;
    else if (rarity === "Unique") row.breakdown!.uniq += n;
    else if (rarity === "Minion") row.breakdown!.minion += n;
    byClass.set(k.id, row);
  }
  return [...byClass.values()]
    .filter((r) => r.count > 0)
    .sort((a, b) => b.count - a.count);
}

function superRows(kills: KillCount[], difficulty: number): KillRow[] {
  const bySuper = new Map<number, number>();
  for (const k of kills) {
    if (k.difficulty !== difficulty || !k.superUnique) continue;
    if (!SUPER_UNIQUE_NAMES[k.id]) continue; // skip unnamed
    bySuper.set(k.id, (bySuper.get(k.id) ?? 0) + Number(k.count));
  }
  return [...bySuper]
    .filter(([, count]) => count > 0)
    .map(([id, count]) => ({ id, name: SUPER_UNIQUE_NAMES[id], count }))
    .sort((a, b) => b.count - a.count);
}

/** Total kills per rarity (normal/champion/unique/minion) across all named classes. */
function rarityRows(
  kills: KillCount[],
  difficulty: number,
  names: GameNames,
): { name: string; count: number }[] {
  const totals: Record<string, number> = {};
  for (const k of kills) {
    if (k.difficulty !== difficulty || k.superUnique) continue;
    if (!names.monster(k.id)) continue; // skip unnamed, consistent with the list
    const r = rarityOf(k.spec);
    totals[r] = (totals[r] ?? 0) + Number(k.count);
  }
  return RARITY_ORDER.filter((r) => (totals[r] ?? 0) > 0).map((r) => ({
    name: r,
    count: totals[r],
  }));
}

/**
 * Which difficulties hold anything, as a set.
 *
 * Answered once per list rather than per question, because the questions are many — the selector
 * asks for each of the three difficulties and the two reset buttons ask again — and each answer is
 * a walk over the character's whole lifetime history. A `.some` short-circuits only where there IS
 * something to find, so the EMPTY difficulty, the one whose answer is least interesting, is the one
 * that scans every row.
 */
function difficultiesWithData<T extends { difficulty: number }>(
  rows: T[],
  nonEmpty: (row: T) => boolean,
): Set<number> {
  const out = new Set<number>();
  for (const row of rows) if (nonEmpty(row)) out.add(row.difficulty);
  return out;
}

/** Section heading with an inline total and an optional right-aligned reset button. */
function SectionHeader({
  title,
  summary,
  canReset,
  onReset,
}: {
  title: string;
  summary?: string;
  canReset: boolean;
  onReset: () => void;
}) {
  return (
    <div className="mb-2 flex items-center justify-between gap-2">
      <h3 className="text-xs font-semibold uppercase tracking-wide text-zinc-100">
        {title}
        {summary !== undefined && (
          <span className="ml-1.5 font-normal normal-case text-zinc-500">
            {summary}
          </span>
        )}
      </h3>
      {canReset && (
        <button
          type="button"
          onClick={onReset}
          className="rounded px-2 py-1 text-xs font-medium text-zinc-500 hover:bg-zinc-800 hover:text-red-400"
        >
          Reset
        </button>
      )}
    </div>
  );
}

function KillList({ title, rows }: { title: string; rows: KillRow[] }) {
  const total = rows.reduce((sum, r) => sum + r.count, 0);
  return (
    <div>
      <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-zinc-300">
        {title}
        <span className="ml-1.5 font-normal text-zinc-500">
          {total.toLocaleString()}
        </span>
      </h4>
      {rows.length === 0 ? (
        <p className="text-xs text-zinc-600">None</p>
      ) : (
        <ul className="max-h-80 space-y-0.5 overflow-y-auto pr-1">
          {rows.map((r) => (
            <li
              key={r.id}
              className="flex items-center justify-between gap-3 text-xs"
            >
              <span className="truncate text-zinc-400">{r.name}</span>
              <span className="flex flex-shrink-0 items-center gap-2 tabular-nums">
                {r.breakdown &&
                  (r.breakdown.uniq > 0 ||
                    r.breakdown.champ > 0 ||
                    r.breakdown.minion > 0) && (
                    <span className="flex gap-1 text-[10px]">
                      {r.breakdown.uniq > 0 && (
                        <span className="text-amber-500/90" title="Unique">
                          {r.breakdown.uniq}u
                        </span>
                      )}
                      {r.breakdown.champ > 0 && (
                        <span className="text-sky-400/90" title="Champions">
                          {r.breakdown.champ}c
                        </span>
                      )}
                      {r.breakdown.minion > 0 && (
                        <span className="text-zinc-500" title="Minions">
                          {r.breakdown.minion}m
                        </span>
                      )}
                    </span>
                  )}
                <span className="w-14 text-right text-zinc-300">
                  {r.count.toLocaleString()}
                </span>
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * The two resets are a mutation each stack owns: v1 clears through CharacterService, v2 through
 * CaptureService. Passed in rather than called here, since the panel has no business knowing
 * which stack it is showing.
 */
export interface AnalyticsResets {
  /**
   * Settle when the reset has finished — the dialog stays up, showing pending, until it does.
   * A rejection is swallowed here because the mutation already reports its own failure; this
   * only decides when to close.
   */
  onReset: () => Promise<unknown>;
  isPending: boolean;
}

/** Run a reset, then close its dialog either way. */
function settle(reset: AnalyticsResets, close: () => void) {
  void reset
    .onReset()
    .catch(() => {})
    .finally(close);
}

export function AnalyticsPanel({
  displayName,
  activeDifficulty,
  kills,
  areaTime,
  killsReset,
  areaTimeReset,
}: {
  displayName: string;
  activeDifficulty: number;
  kills: KillCount[];
  areaTime: AreaDuration[];
  killsReset: AnalyticsResets;
  areaTimeReset: AnalyticsResets;
}) {
  // Default to the active difficulty; keyed by profile in the parent to re-default per char.
  const [selectedDifficulty, setSelectedDifficulty] =
    useState(activeDifficulty);
  const [killsConfirmOpen, setKillsConfirmOpen] = useState(false);
  const [areaConfirmOpen, setAreaConfirmOpen] = useState(false);
  const names = useGameNames();

  const killsPresent = useMemo(
    () => difficultiesWithData(kills, (k) => k.count > 0n),
    [kills],
  );
  const areaPresent = useMemo(
    () => difficultiesWithData(areaTime, (a) => a.milliseconds > 0n),
    [areaTime],
  );
  // Stable, because it is a prop of the selector rather than something read here.
  const hasData = useCallback(
    (difficulty: number) =>
      killsPresent.has(difficulty) || areaPresent.has(difficulty),
    [killsPresent, areaPresent],
  );

  const anyKills = killsPresent.size > 0;
  const anyArea = areaPresent.size > 0;

  // A live v2 character re-fetches its whole capture every time anything about it changes — gold
  // and experience churn constantly — so every one of these aggregations would otherwise be run
  // again over the character's entire lifetime kill and area history several times a minute, for
  // a panel whose contents did not move.
  const monsters = useMemo(
    () => classRows(kills, selectedDifficulty, names),
    [kills, selectedDifficulty, names],
  );
  const superUniques = useMemo(
    () => superRows(kills, selectedDifficulty),
    [kills, selectedDifficulty],
  );
  const rarity = useMemo(
    () => rarityRows(kills, selectedDifficulty, names),
    [kills, selectedDifficulty, names],
  );
  const areas = useMemo(
    () => areaRows(areaTime, selectedDifficulty),
    [areaTime, selectedDifficulty],
  );

  const totalAreaMs = areas.reduce((sum, r) => sum + r.ms, 0);
  // Grand total across all difficulties. Counted with the same "named areas only" rule each
  // difficulty's section total uses, so it equals the sum of the per-difficulty "Time in Area"
  // totals rather than quietly exceeding them by whatever could not be named.
  const totalPlayedMs = useMemo(
    () =>
      areaTime.reduce(
        (sum, a) => (AREA_NAMES[a.area] ? sum + Number(a.milliseconds) : sum),
        0,
      ),
    [areaTime],
  );
  const totalKills =
    monsters.reduce((sum, r) => sum + r.count, 0) +
    superUniques.reduce((sum, r) => sum + r.count, 0);

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <DifficultySelector
          selected={selectedDifficulty}
          onSelect={setSelectedDifficulty}
          activeDifficulty={activeDifficulty}
          hasData={hasData}
        />
        {totalPlayedMs > 0 && (
          <span
            className="text-xs text-zinc-400"
            title="Time in area summed across all difficulties"
          >
            Total played:{" "}
            <span className="tabular-nums text-zinc-200">
              {formatMs(totalPlayedMs)}
            </span>
          </span>
        )}
      </div>

      {/* Time in Area */}
      <section>
        <SectionHeader
          title="Time in Area"
          summary={areas.length > 0 ? formatMs(totalAreaMs) : undefined}
          canReset={anyArea}
          onReset={() => setAreaConfirmOpen(true)}
        />
        {areas.length === 0 ? (
          <p className="text-xs text-zinc-600">
            No time recorded for this difficulty yet.
          </p>
        ) : (
          <ul className="grid max-h-80 grid-cols-2 gap-x-6 gap-y-0.5 overflow-y-auto pr-1 lg:grid-cols-3">
            {areas.map((r) => (
              <li
                key={r.id}
                className="flex items-center justify-between gap-3 text-xs"
              >
                <span className="truncate text-zinc-400">{r.name}</span>
                <span className="flex-shrink-0 tabular-nums text-zinc-300">
                  {formatMs(r.ms)}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      {/* Kills */}
      <section className="space-y-4">
        <div>
          <SectionHeader
            title="Kills"
            summary={totalKills > 0 ? totalKills.toLocaleString() : undefined}
            canReset={anyKills}
            onReset={() => setKillsConfirmOpen(true)}
          />
          {rarity.length > 0 && (
            <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-zinc-400">
              {rarity.map((r) => (
                <span key={r.name}>
                  {r.name}:{" "}
                  <span className="tabular-nums text-zinc-300">
                    {r.count.toLocaleString()}
                  </span>
                </span>
              ))}
            </div>
          )}
        </div>
        {monsters.length === 0 && superUniques.length === 0 ? (
          <p className="text-xs text-zinc-600">
            No kills recorded for this difficulty yet.
          </p>
        ) : (
          <div className="grid gap-6 lg:grid-cols-2">
            <KillList title="Monsters" rows={monsters} />
            <KillList title="Super Uniques" rows={superUniques} />
          </div>
        )}
      </section>

      <ConfirmationDialog
        open={areaConfirmOpen}
        title="Reset area stats"
        description={`Clear all recorded time-in-area for "${displayName}"?`}
        message="This clears time-in-area for every difficulty and cannot be undone."
        confirmLabel="Reset"
        isPending={areaTimeReset.isPending}
        onConfirm={() => settle(areaTimeReset, () => setAreaConfirmOpen(false))}
        onCancel={() => setAreaConfirmOpen(false)}
      />

      <ConfirmationDialog
        open={killsConfirmOpen}
        title="Reset kills"
        description={`Clear all recorded kills for "${displayName}"?`}
        message="This clears the lifetime kill counts for every difficulty and cannot be undone."
        confirmLabel="Reset"
        isPending={killsReset.isPending}
        onConfirm={() => settle(killsReset, () => setKillsConfirmOpen(false))}
        onCancel={() => setKillsConfirmOpen(false)}
      />
    </div>
  );
}
