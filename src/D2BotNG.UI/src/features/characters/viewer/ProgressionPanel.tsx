/**
 * ProgressionPanel - renders quest completion and acquired waypoints for the
 * character's current difficulty (the d2bs sender only reports the active
 * difficulty's records). Quests/waypoints present in the progression arrays are
 * "done"; the rest are shown dimmed so the full set is always visible.
 */

import { useState } from "react";
import clsx from "clsx";
import type { Character } from "@/generated/characters_pb";
import {
  QUEST_ACTS,
  WAYPOINT_ACTS,
  type Act,
  type NamedId,
} from "./progression";

const DIFFICULTIES = [
  { id: 0, name: "Normal" },
  { id: 1, name: "Nightmare" },
  { id: 2, name: "Hell" },
] as const;

function ProgressionGroup({
  title,
  acts,
  owned,
  dotClass,
}: {
  title: string;
  acts: Act<NamedId>[];
  owned: Set<number>;
  dotClass: string;
}) {
  return (
    <div>
      <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-zinc-100">
        {title}
      </h3>
      <div className="grid grid-cols-1 gap-x-4 gap-y-3 sm:grid-cols-2 lg:grid-cols-3">
        {acts.map((act) => (
          <div key={act.act}>
            <div className="mb-1 text-[11px] font-medium text-zinc-500">
              Act {act.act}
            </div>
            <ul className="space-y-0.5">
              {act.entries.map((entry) => {
                const has = owned.has(entry.id);
                return (
                  <li
                    key={entry.id}
                    className="flex items-center gap-1.5 text-xs"
                  >
                    <span
                      className={clsx(
                        "h-1.5 w-1.5 flex-shrink-0 rounded-full",
                        has ? dotClass : "bg-zinc-700",
                      )}
                    />
                    <span
                      className={clsx(
                        "truncate",
                        has ? "text-zinc-300" : "text-zinc-600",
                      )}
                      title={entry.name}
                    >
                      {entry.name}
                    </span>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </div>
    </div>
  );
}

export function ProgressionPanel({ character }: { character: Character }) {
  const activeDifficulty = character.difficulty;
  // Default to the active difficulty; the user can switch to inspect the others.
  const [selectedDifficulty, setSelectedDifficulty] =
    useState(activeDifficulty);

  const byDiff = character.progression;
  const progFor = (id: number) =>
    id === 1 ? byDiff?.nightmare : id === 2 ? byDiff?.hell : byDiff?.normal;

  const prog = progFor(selectedDifficulty);
  const completedQuests = new Set(prog?.quests ?? []);
  const ownedWaypoints = new Set(prog?.waypoints ?? []);

  return (
    <div className="space-y-4">
      <div className="inline-flex gap-0.5 rounded-md bg-zinc-800/60 p-0.5 text-xs">
        {DIFFICULTIES.map((d) => {
          // Disable difficulties we've never received progression for (the active
          // one stays enabled even before its first update arrives).
          const enabled = d.id === activeDifficulty || !!progFor(d.id);
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

      <div className="grid gap-6 lg:grid-cols-2">
        <ProgressionGroup
          title="Quests"
          acts={QUEST_ACTS}
          owned={completedQuests}
          dotClass="bg-green-500"
        />
        <ProgressionGroup
          title="Waypoints"
          acts={WAYPOINT_ACTS}
          owned={ownedWaypoints}
          dotClass="bg-d2-gold"
        />
      </div>
    </div>
  );
}
