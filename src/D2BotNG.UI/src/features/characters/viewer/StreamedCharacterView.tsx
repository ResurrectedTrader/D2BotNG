/**
 * The wire schema v1 view: a character streamed live on the event stream. See `contracts.ts` for
 * why the two stacks have a view each rather than one converting into the other.
 *
 * v1 arrives with its presentation already resolved by the game engine — sprite name, palette
 * shift, sockets — so this view needs no tables and no async anything. Nearly all of it is field
 * passthrough; the shapes that genuinely differ from v2 (the flat container list, the nested kill
 * maps, the three-field progression) are flattened here rather than in the shared components.
 */

import { useMemo, type ReactNode } from "react";
import type { Character, Container } from "@/generated/characters_pb";
import { useResetKills } from "@/hooks/useResetKills";
import { useResetAreaTime } from "@/hooks/useResetAreaTime";
import { CharacterChrome } from "./CharacterChrome";
import { InventoryTab, type LabeledContainer } from "./InventoryTab";
import { StatsSkillsTab } from "./StatsSkillsTab";
import { ProgressionPanel } from "./ProgressionPanel";
import { AnalyticsPanel } from "./AnalyticsPanel";
import {
  CONTAINER_LABELS,
  STORAGE_IDS,
  stashPageLabel,
  type AreaDuration,
  type CharacterFacts,
  type DifficultyProgress,
  type KillCount,
  type SkillLevels,
} from "./contracts";

/** v1 keys its containers by an id string, in one flat list; stash pages all share "stash". */
function containerById(
  containers: Container[],
  id: string,
): Container | undefined {
  return containers.find((c) => c.id === id);
}

/**
 * v1 splits everything per-difficulty into three named fields, where the shared shapes carry the
 * difficulty as the number both stacks send. Paired up in one place so the three unpackers below
 * cannot disagree about which field is difficulty 1, and absent difficulties drop out here rather
 * than in each of them.
 */
function byDifficulty<T>(
  fields: { normal?: T; nightmare?: T; hell?: T } | undefined,
): [number, T][] {
  const entries: [number, T | undefined][] = [
    [0, fields?.normal],
    [1, fields?.nightmare],
    [2, fields?.hell],
  ];
  return entries.filter((e): e is [number, T] => e[1] !== undefined);
}

export function StreamedCharacterView({
  character,
  online,
  selector,
}: {
  character: Character;
  online: boolean;
  selector: ReactNode;
}) {
  const resetKills = useResetKills();
  const resetAreaTime = useResetAreaTime();

  const facts: CharacterFacts = {
    profile: character.profile,
    charName: character.charName,
    account: character.account,
    realm: character.realm,
    level: character.level,
    charClass: character.charClass,
    difficulty: character.difficulty,
    area: character.area,
    areaEnteredAt: character.areaEnteredAt,
    // The WeaponSwitch char flag is lobby-only, so the snapshot's top-level `hand` is the live
    // in-game source for which set is active.
    hand: character.hand === 1 ? 1 : 0,
    hardcore: character.mode?.hardcore ?? false,
    ladder: character.mode?.ladder ?? false,
    expansion: character.mode?.expansion ?? false,
    updatedAt: character.updatedAt,
  };

  const equipped = containerById(character.containers, "equipped");
  const merc = containerById(character.containers, "merc");

  const storage = useMemo(() => {
    const containers = character.containers;
    const stashPages = containers
      .filter((c) => c.id === "stash")
      .sort((a, b) => a.page - b.page);
    const out: LabeledContainer[] = [];
    for (const id of STORAGE_IDS) {
      const container = containerById(containers, id);
      if (container) out.push({ label: CONTAINER_LABELS[id], container });
    }
    for (const page of stashPages) {
      out.push({
        label: stashPageLabel(page.name, page.page, stashPages.length),
        // The id is a React key and every page shares "stash", so page-qualify it.
        container: { ...page, id: `stash-${page.page}` },
      });
    }
    return out;
  }, [character.containers]);

  const skills = useMemo<SkillLevels[]>(
    () =>
      character.skills.map((s) => ({
        skillId: s.skillId,
        invested: s.hardPoints,
        // v1 sends the gear share separately, so the effective level is the sum.
        total: s.hardPoints + s.softPoints,
      })),
    [character.skills],
  );

  const progression = useMemo(() => {
    const out: Partial<Record<number, DifficultyProgress>> = {};
    for (const [difficulty, entry] of byDifficulty(character.progression)) {
      out[difficulty] = { quests: entry.quests, waypoints: entry.waypoints };
    }
    return out;
  }, [character.progression]);

  // v1 nests kills difficulty -> class -> spec, and super-uniques difficulty -> id. Flattened to
  // the tuples the panel counts; the two buckets stay disjoint, which is why `superUnique` is a
  // field rather than a special class id.
  const kills = useMemo(() => {
    const out: KillCount[] = [];
    for (const [difficulty, dk] of byDifficulty(character.kills)) {
      for (const [id, cls] of Object.entries(dk.byClass)) {
        for (const [spec, count] of Object.entries(cls.bySpec)) {
          out.push({
            difficulty,
            superUnique: false,
            id: Number(id),
            spec: Number(spec),
            count,
          });
        }
      }
      for (const [id, count] of Object.entries(dk.bySuperUnique)) {
        out.push({
          difficulty,
          superUnique: true,
          id: Number(id),
          spec: 0,
          count,
        });
      }
    }
    return out;
  }, [character.kills]);

  const areaTime = useMemo(() => {
    const out: AreaDuration[] = [];
    for (const [difficulty, map] of byDifficulty(character.areaTime)) {
      for (const [area, milliseconds] of Object.entries(map)) {
        out.push({ difficulty, area: Number(area), milliseconds });
      }
    }
    return out;
  }, [character.areaTime]);

  const displayName = character.charName || character.profile;

  return (
    <CharacterChrome
      facts={facts}
      online={online}
      selector={selector}
      inventory={
        <InventoryTab
          profileKey={character.profile}
          expansion={facts.expansion}
          activeSet={facts.hand}
          equipped={equipped}
          merc={merc}
          storage={storage}
        />
      }
      statsAndSkills={
        <StatsSkillsTab
          stats={character.stats}
          difficulty={character.difficulty}
          skills={skills}
          charClass={character.charClass}
        />
      }
      progression={
        <ProgressionPanel
          key={character.profile}
          progression={progression}
          activeDifficulty={character.difficulty}
        />
      }
      analytics={
        <AnalyticsPanel
          key={character.profile}
          displayName={displayName}
          activeDifficulty={character.difficulty}
          kills={kills}
          areaTime={areaTime}
          killsReset={{
            onReset: () => resetKills.mutateAsync(character.profile),
            isPending: resetKills.isPending,
          }}
          areaTimeReset={{
            onReset: () => resetAreaTime.mutateAsync(character.profile),
            isPending: resetAreaTime.isPending,
          }}
        />
      }
    />
  );
}
