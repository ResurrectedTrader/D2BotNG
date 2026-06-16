/**
 * AnalyticsPanel - the "Analytics" tab. Per-difficulty game analytics for the character:
 * lifetime time spent per area, and lifetime monster kills (regular monsters by class
 * with a by-rarity summary, plus super-uniques by name). A shared difficulty selector
 * drives both sections; each has its own reset. Ids resolve via the generated name
 * tables; the manager accumulates both from the engine's per-game reports.
 */

import { useState } from "react";
import clsx from "clsx";
import type {
  Character,
  MonsterKills,
  DifficultyKills,
  AreaTime,
} from "@/generated/characters_pb";
import { ConfirmationDialog } from "@/components/ui";
import { useResetKills } from "@/hooks/useResetKills";
import { useResetAreaTime } from "@/hooks/useResetAreaTime";
import { MONSTER_NAMES } from "./data/monsterNames";
import { SUPER_UNIQUE_NAMES } from "./data/superUniqueNames";
import { AREA_NAMES } from "./data/areaNames";

const DIFFICULTIES = [
  { id: 0, name: "Normal" },
  { id: 1, name: "Nightmare" },
  { id: 2, name: "Hell" },
] as const;

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

/** ms -> compact "1h 23m" / "4m 12s" / "9s". */
function formatMs(ms: number): string {
  const totalSeconds = Math.floor(ms / 1000);
  const s = totalSeconds % 60;
  const m = Math.floor(totalSeconds / 60) % 60;
  const h = Math.floor(totalSeconds / 3600);
  if (h > 0) return `${h}h ${m}m`;
  if (m > 0) return `${m}m ${s}s`;
  return `${s}s`;
}

type KillRow = {
  id: number;
  name: string;
  count: number;
  breakdown?: { uniq: number; champ: number; minion: number };
};
type AreaRow = { id: number; name: string; ms: number };

function killsForDifficulty(
  kills: MonsterKills | undefined,
  difficulty: number,
): DifficultyKills | undefined {
  if (!kills) return undefined;
  return difficulty === 1
    ? kills.nightmare
    : difficulty === 2
      ? kills.hell
      : kills.normal;
}

function areaForDifficulty(
  areaTime: AreaTime | undefined,
  difficulty: number,
): Record<number, bigint> | undefined {
  if (!areaTime) return undefined;
  return difficulty === 1
    ? areaTime.nightmare
    : difficulty === 2
      ? areaTime.hell
      : areaTime.normal;
}

function areaRows(map: Record<number, bigint> | undefined): AreaRow[] {
  if (!map) return [];
  return Object.entries(map)
    .flatMap(([id, ms]): AreaRow[] => {
      const nid = Number(id);
      const name = AREA_NAMES[nid];
      const m = Number(ms);
      return name && m > 0 ? [{ id: nid, name, ms: m }] : []; // skip ids we can't name
    })
    .sort((a, b) => b.ms - a.ms);
}

function classRows(dk: DifficultyKills | undefined): KillRow[] {
  if (!dk) return [];
  return Object.entries(dk.byClass)
    .flatMap(([id, cls]): KillRow[] => {
      const nid = Number(id);
      const name = MONSTER_NAMES[nid];
      if (!name) return []; // skip classes we can't name (internal dummies)
      // Aggregate the class total plus a by-rarity breakdown (SpecType is a bitfield).
      let count = 0,
        uniq = 0,
        champ = 0,
        minion = 0;
      for (const [s, c] of Object.entries(cls.bySpec)) {
        const n = Number(c);
        count += n;
        const rarity = rarityOf(Number(s)); // one bucket per kill (champion != unique)
        if (rarity === "Champion") champ += n;
        else if (rarity === "Unique") uniq += n;
        else if (rarity === "Minion") minion += n;
      }
      return count > 0
        ? [{ id: nid, name, count, breakdown: { uniq, champ, minion } }]
        : [];
    })
    .sort((a, b) => b.count - a.count);
}

function superRows(dk: DifficultyKills | undefined): KillRow[] {
  if (!dk) return [];
  return Object.entries(dk.bySuperUnique)
    .flatMap(([id, count]): KillRow[] => {
      const nid = Number(id);
      const name = SUPER_UNIQUE_NAMES[nid];
      const c = Number(count);
      return name && c > 0 ? [{ id: nid, name, count: c }] : []; // skip unnamed
    })
    .sort((a, b) => b.count - a.count);
}

/** Total kills per rarity (normal/champion/unique/minion) across all named classes. */
function rarityRows(
  dk: DifficultyKills | undefined,
): { name: string; count: number }[] {
  if (!dk) return [];
  const totals: Record<string, number> = {};
  for (const [id, cls] of Object.entries(dk.byClass)) {
    if (!MONSTER_NAMES[Number(id)]) continue; // skip unnamed, consistent with the list
    for (const [spec, count] of Object.entries(cls.bySpec)) {
      const r = rarityOf(Number(spec));
      totals[r] = (totals[r] ?? 0) + Number(count);
    }
  }
  return RARITY_ORDER.filter((r) => (totals[r] ?? 0) > 0).map((r) => ({
    name: r,
    count: totals[r],
  }));
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

export function AnalyticsPanel({ character }: { character: Character }) {
  const activeDifficulty = character.difficulty;
  // Default to the active difficulty; keyed by profile in the parent to re-default per char.
  const [selectedDifficulty, setSelectedDifficulty] =
    useState(activeDifficulty);
  const [killsConfirmOpen, setKillsConfirmOpen] = useState(false);
  const [areaConfirmOpen, setAreaConfirmOpen] = useState(false);
  const resetKills = useResetKills();
  const resetAreaTime = useResetAreaTime();

  const kills = character.kills;
  const areaTime = character.areaTime;

  const killsHasData = (difficulty: number) => {
    const dk = killsForDifficulty(kills, difficulty);
    return (
      !!dk &&
      (Object.keys(dk.byClass).length > 0 ||
        Object.keys(dk.bySuperUnique).length > 0)
    );
  };
  const areaHasData = (difficulty: number) => {
    const m = areaForDifficulty(areaTime, difficulty);
    return !!m && Object.keys(m).length > 0;
  };
  const hasData = (difficulty: number) =>
    killsHasData(difficulty) || areaHasData(difficulty);

  const anyKills = killsHasData(0) || killsHasData(1) || killsHasData(2);
  const anyArea = areaHasData(0) || areaHasData(1) || areaHasData(2);

  const dk = killsForDifficulty(kills, selectedDifficulty);
  const monsters = classRows(dk);
  const superUniques = superRows(dk);
  const rarity = rarityRows(dk);
  const areas = areaRows(areaForDifficulty(areaTime, selectedDifficulty));

  const totalAreaMs = areas.reduce((sum, r) => sum + r.ms, 0);
  const totalKills =
    monsters.reduce((sum, r) => sum + r.count, 0) +
    superUniques.reduce((sum, r) => sum + r.count, 0);

  return (
    <div className="space-y-6">
      <div className="inline-flex gap-0.5 rounded-md bg-zinc-800/60 p-0.5 text-xs">
        {DIFFICULTIES.map((d) => {
          // Disable difficulties with no data (the active one stays selectable).
          const enabled = d.id === activeDifficulty || hasData(d.id);
          return (
            <button
              key={d.id}
              type="button"
              disabled={!enabled}
              onClick={() => setSelectedDifficulty(d.id)}
              title={
                d.id === activeDifficulty
                  ? "Last seen difficulty"
                  : enabled
                    ? undefined
                    : "No data"
              }
              className={clsx(
                "relative rounded px-3 py-1 font-medium",
                !enabled
                  ? "cursor-not-allowed text-zinc-600"
                  : selectedDifficulty === d.id
                    ? "bg-zinc-700 text-zinc-100"
                    : "text-zinc-400 hover:text-zinc-200",
              )}
            >
              {d.name}
              {d.id === activeDifficulty && (
                <span className="absolute right-0.5 top-0.5 h-1.5 w-1.5 rounded-full bg-green-500" />
              )}
            </button>
          );
        })}
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
        description={`Clear all recorded time-in-area for "${
          character.charName || character.profile
        }"?`}
        message="This clears time-in-area for every difficulty and cannot be undone."
        confirmLabel="Reset"
        isPending={resetAreaTime.isPending}
        onConfirm={() =>
          resetAreaTime.mutate(character.profile, {
            onSettled: () => setAreaConfirmOpen(false),
          })
        }
        onCancel={() => setAreaConfirmOpen(false)}
      />

      <ConfirmationDialog
        open={killsConfirmOpen}
        title="Reset kills"
        description={`Clear all recorded kills for "${
          character.charName || character.profile
        }"?`}
        message="This clears the lifetime kill counts for every difficulty and cannot be undone."
        confirmLabel="Reset"
        isPending={resetKills.isPending}
        onConfirm={() =>
          resetKills.mutate(character.profile, {
            onSettled: () => setKillsConfirmOpen(false),
          })
        }
        onCancel={() => setKillsConfirmOpen(false)}
      />
    </div>
  );
}
