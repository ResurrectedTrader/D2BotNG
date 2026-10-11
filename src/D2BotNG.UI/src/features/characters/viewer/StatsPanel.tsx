/**
 * StatsPanel - renders the curated character stats grouped into sensible
 * sections (attributes, life/mana, resistances, faster-cast/run/hit + MF,
 * progress). Unknown stat ids fall into an "Other" group so nothing is dropped.
 */

import { STAT_LABELS } from "./stats";
import {
  DIFFICULTY_NAMES,
  STAT_EXPERIENCE,
  STAT_LAST_EXPERIENCE,
  STAT_LEVEL,
  STAT_NEXT_EXPERIENCE,
  levelProgress,
  type LevelProgress,
  type StatValue,
} from "./contracts";
import { useGameNames } from "./gameNames";

const STAT_GROUPS: { label: string; ids: number[] }[] = [
  { label: "Attributes", ids: [0, 2, 3, 1, 4] }, // Strength, Dexterity, Vitality, Energy, unspent
  { label: "Life, Mana & Stamina", ids: [7, 9, 11] },
  { label: "Resistances", ids: [39, 43, 41, 45] }, // Fire, Cold, Lightning, Poison
  { label: "Speed", ids: [105, 96, 99, 93, 102] }, // FCR, FRW, FHR, IAS, FBR
  { label: "Combat", ids: [19, 31, 127] }, // Attack Rating, Defense, +All Skills
  { label: "Find", ids: [80, 79] }, // Magic Find, Gold Find
  { label: "Progress", ids: [12, 13, 14, 15] }, // Level, Experience, Gold, Stash Gold
];

// The engine reports raw resistances (gear/skills/charms); the game subtracts a flat
// difficulty penalty to get the effective value (Difficultylevels.txt ResistPenalty):
// Normal 0, Nightmare -40, Hell -100. See the Resistances (Diablo II) wiki.
const RESIST_PENALTY: Record<number, number> = { 0: 0, 1: 40, 2: 100 };
const RESIST_IDS = new Set([39, 41, 43, 45]); // fire, lightning, cold, poison
// Resist id -> its "+max resist" stat id (40/42/44/46). The engine reports these as the
// bonus above the 75% base (0 with no max-res gear); the cap is 75 + bonus, hard-capped at
// 95%. We fold them into the resist display rather than showing them as their own rows.
const MAX_RESIST_STAT: Record<number, number> = {
  39: 40,
  41: 42,
  43: 44,
  45: 46,
};
/** Unspent stat and skill points: drawn in amber while there are any, as the game flags them. */
export const STAT_POINTS = 4;
export const SKILL_POINTS = 5;
const UNSPENT_IDS = new Set([STAT_POINTS, SKILL_POINTS]);
/**
 * A player's level thresholds come from experience.txt, not the unit: stats 29/30 are a
 * hireling's (the game keeps them at 0 on a player), so they are only hidden here, never read.
 */
const HIDDEN_THRESHOLD_IDS = [STAT_LAST_EXPERIENCE, STAT_NEXT_EXPERIENCE];

/** The progress row: the percentage, a bar like the game's experience bar, the numbers on hover. */
function LevelProgressRow({
  progress,
  level,
}: {
  progress: LevelProgress;
  level: number | undefined;
}) {
  const percent = progress.fraction * 100;
  const title =
    `${progress.remaining.toLocaleString()} to ` +
    `${level !== undefined ? `level ${level + 1}` : "the next level"} ` +
    `(at ${progress.next.toLocaleString()})`;
  return (
    <div className="text-sm" title={title}>
      <div className="flex min-w-0 items-center justify-between gap-2">
        <span className="truncate text-zinc-400">Level Progress</span>
        <span className="flex-shrink-0 font-medium text-zinc-300">
          {percent.toFixed(1)}%
        </span>
      </div>
      <div className="mt-0.5 h-1 overflow-hidden rounded-full bg-zinc-800">
        <div
          className="h-full rounded-full bg-d2-gold"
          style={{ width: `${percent}%` }}
        />
      </div>
    </div>
  );
}

const RESIST_BASE_MAX = 75; // base resistance cap before any +max-resist bonus
const RESIST_HARD_CAP = 95; // absolute ceiling no amount of +max resist can exceed

/** A resistance adjusted for the current difficulty's penalty, shown as "effective / cap".
 *  The effective value is NOT clamped, so an over-capped resist reads e.g. "111% / 75%" and
 *  the over-cap amount (headroom vs. -resist effects like Conviction) is visible at a glance.
 *  The cap is 75 + maxBonus (<= 95); maxBonus is the character's "+max resist" for this element. */
function resistDisplay(
  raw: number,
  difficulty: number,
  maxBonus: number,
): { text: string; className: string; title: string } {
  const penalty = RESIST_PENALTY[difficulty] ?? 0;
  const effective = raw - penalty;
  const cap = Math.min(RESIST_BASE_MAX + maxBonus, RESIST_HARD_CAP);
  const className =
    effective < 0
      ? "text-red-400"
      : effective >= cap
        ? "text-emerald-400"
        : "text-zinc-300";
  const penaltyNote =
    penalty > 0
      ? `, ${DIFFICULTY_NAMES[difficulty] ?? "?"} penalty −${penalty}%`
      : "";
  const maxNote = maxBonus > 0 ? ` (+${maxBonus}% max)` : "";
  return {
    text: `${effective}% / ${cap}%`,
    className,
    title: `Base ${raw}%${penaltyNote}${maxNote}`,
  };
}

export function StatsPanel({
  stats,
  difficulty,
}: {
  stats: StatValue[];
  difficulty: number;
}) {
  const names = useGameNames();
  if (stats.length === 0) {
    return <p className="text-sm text-zinc-500">No stats reported.</p>;
  }

  const byId = new Map<number, bigint>();
  for (const s of stats) byId.set(s.id, s.value);

  // Max-resist stats are consumed by the resistance display and the level thresholds are not a
  // player's, so neither is shown as rows of its own; skill points sit on the Skills panel.
  const known = new Set([
    ...STAT_GROUPS.flatMap((g) => g.ids),
    ...Object.values(MAX_RESIST_STAT),
    ...HIDDEN_THRESHOLD_IDS,
    SKILL_POINTS,
  ]);
  const level = byId.has(STAT_LEVEL) ? Number(byId.get(STAT_LEVEL)) : undefined;
  const exp = byId.get(STAT_EXPERIENCE);
  const progress =
    level !== undefined && exp !== undefined
      ? levelProgress(
          Number(exp),
          names.levelExperience(level),
          names.levelExperience(level + 1),
        )
      : null;
  const otherIds = [...byId.keys()].filter((id) => !known.has(id));
  const groups = [
    ...STAT_GROUPS.map((g) => ({
      label: g.label,
      ids: g.ids.filter((id) => byId.has(id)),
    })),
    ...(otherIds.length ? [{ label: "Other", ids: otherIds }] : []),
  ].filter((g) => g.ids.length > 0);

  return (
    <div className="grid gap-x-8 gap-y-4 sm:grid-cols-2 lg:grid-cols-3">
      {groups.map((group) => (
        <div key={group.label}>
          <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-zinc-100">
            {group.label}
          </h3>
          <div className="space-y-0.5">
            {group.ids.map((id) => {
              const label = STAT_LABELS[id] ?? `Stat ${id}`;
              const value = byId.get(id)!;
              const resist = RESIST_IDS.has(id)
                ? resistDisplay(
                    Number(value),
                    difficulty,
                    Number(byId.get(MAX_RESIST_STAT[id]) ?? 0n),
                  )
                : null;
              const unspent = UNSPENT_IDS.has(id) && value > 0n;
              const valueClass = resist
                ? resist.className
                : unspent
                  ? "text-amber-400"
                  : "text-zinc-400";
              return (
                <div key={id}>
                  <div className="flex min-w-0 items-center justify-between gap-2 text-sm">
                    <span className="truncate text-zinc-400" title={label}>
                      {label}
                    </span>
                    <span
                      className={`flex-shrink-0 font-medium ${valueClass}`}
                      title={resist?.title}
                    >
                      {resist ? resist.text : value.toLocaleString()}
                    </span>
                  </div>
                  {/* Under Experience: how far through the level that is. */}
                  {id === STAT_EXPERIENCE && progress && (
                    <div className="mt-0.5">
                      <LevelProgressRow progress={progress} level={level} />
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}
