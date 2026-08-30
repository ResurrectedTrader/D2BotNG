/**
 * ProgressionPanel - renders quest completion and acquired waypoints for the
 * character's current difficulty (the d2bs sender only reports the active
 * difficulty's records). Quests/waypoints present in the progression arrays are
 * "done"; the rest are shown dimmed so the full set is always visible.
 */

import { useState } from "react";
import clsx from "clsx";
import { QUEST_ACTS, WAYPOINT_ACTS, type Act } from "./progression";
import { DifficultySelector } from "./CharacterChrome";
import type { DifficultyProgress } from "./contracts";

function ProgressionGroup({
  title,
  acts,
  owned,
  dotClass,
}: {
  title: string;
  acts: Act[];
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

export function ProgressionPanel({
  progression,
  activeDifficulty,
}: {
  /** Keyed by difficulty; a missing key means nothing has been reported for it. */
  progression: Partial<Record<number, DifficultyProgress>>;
  activeDifficulty: number;
}) {
  // Default to the active difficulty; the user can switch to inspect the others.
  const [selectedDifficulty, setSelectedDifficulty] =
    useState(activeDifficulty);

  const prog = progression[selectedDifficulty];
  const completedQuests = new Set(prog?.quests ?? []);
  const ownedWaypoints = new Set(prog?.waypoints ?? []);

  return (
    <div className="space-y-4">
      <DifficultySelector
        selected={selectedDifficulty}
        onSelect={setSelectedDifficulty}
        activeDifficulty={activeDifficulty}
        // Anything reported at all counts: the panel shows the full quest and waypoint sets, so a
        // difficulty with a record has something to say even when nothing in it is done.
        hasData={(id) => !!progression[id]}
      />

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
