/**
 * Game vocabulary, read from the game's own tables rather than transcribed into this repo.
 *
 * A capture identifies things by ID — skill 54, monster 156, class 1 — and the UI has to name them.
 * Two ways to do that: keep a hand-copied map here, or ask the tables the game itself ships. The
 * tables win on both counts that matter. They cannot drift from the game (a hand-copied map is a
 * snapshot of one patch), and every name they return comes out of the LOCALE string table, so the
 * day this app is localised these names follow the game's own language file with no work here.
 *
 * `d2itemtoolkit` embeds the tables, so this needs no game install — but it is a ~735KB lazy chunk,
 * which is why every lookup degrades to null rather than blocking: the character view renders
 * immediately and the names fill in when the chunk lands.
 *
 * WHAT IS NOT HERE, and why it is still hand-written elsewhere in this folder:
 *
 *   * AREA names (`data/areaNames.ts`) — levels.txt is not among the shipped tables.
 *   * SUPER UNIQUE names (`data/superUniqueNames.ts`) — nor is SuperUniques.txt.
 *   * QUEST and WAYPOINT names (`progression.ts`) — same missing table, plus quests.
 *   * ATTRIBUTE labels (`stats.ts`) — ItemStatCost has no noun for a stat, only the phrase the
 *     game composes a modifier from ("+# to Strength", index 3473). Trimming that back to a noun
 *     is an English rule, so deriving one would be a localisation bug rather than a fix.
 *   * DIFFICULTY names and item QUALITY names — the string table has no keys for them; the game
 *     shows quality as a text COLOUR rather than as a word.
 */

import { useMemo } from "react";
import type { TooltipEngine } from "d2itemtoolkit";
import { useToolkit } from "@/hooks/useToolkit";
import { MISSING_STRING } from "../search/statCatalog";

export interface GameNames {
  /** skills.txt via skilldesc.txt. Null for an id the tables do not describe. */
  skill(id: number): string | null;
  /** monstats.txt `NameStr`. Null for the internal dummies, which have no player-facing name. */
  monster(id: number): string | null;
  /** charstats.txt, whose class column doubles as the string-table key. */
  characterClass(id: number): string | null;
}

const UNKNOWN: GameNames = {
  skill: () => null,
  monster: () => null,
  characterClass: () => null,
};

/**
 * Cached per engine, because these are pure table reads and a panel asks for the same handful of
 * names on every render — a kill list re-resolves one string per row per keystroke otherwise.
 */
function namesFor(engine: TooltipEngine): GameNames {
  const strings = engine.data.strings;
  const monsters = engine.data.monsterStats;
  const classes = engine.data.charStats;

  // The string table answers an unknown key with its own placeholder rather than with nothing, and
  // that placeholder is a real string — so every lookup has to reject it explicitly or the UI
  // renders "an evil force" as a skill name. The internal skills (156+: monster attacks, maggot
  // animations) are all in that state.
  const named = (text: string | null | undefined): string | null =>
    text && text !== MISSING_STRING ? text : null;

  const resolve = (key: string | null | undefined): string | null =>
    key ? named(strings.getByIndex(strings.getIndexByKey(key))) : null;

  const cache = new Map<string, string | null>();
  const once = (key: string, lookup: () => string | null): string | null => {
    if (!cache.has(key)) cache.set(key, lookup());
    return cache.get(key)!;
  };

  return {
    skill: (id) =>
      once(`s${id}`, () => named(engine.data.skills.getSkillName(id))),
    monster: (id) =>
      once(`m${id}`, () =>
        monsters && id >= 0 && id < monsters.rowCount
          ? resolve(monsters.getString(id, "NameStr"))
          : null,
      ),
    characterClass: (id) =>
      once(`c${id}`, () =>
        classes && id >= 0 && id < classes.rowCount
          ? resolve(classes.getString(id, "class"))
          : null,
      ),
  };
}

/**
 * Names for the character views. Loads the tables — both schemas need them, since a skill id is a
 * skill id whichever stack reported it.
 */
export function useGameNames(): GameNames {
  const engine = useToolkit(true);
  return useMemo(() => (engine ? namesFor(engine) : UNKNOWN), [engine]);
}
