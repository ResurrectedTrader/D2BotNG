/**
 * The wire schema v2 view: a character read back from the capture store. See `contracts.ts` for
 * why the two stacks have a view each rather than one converting into the other.
 *
 * v2 stores the raw `D2UnitStrc` capture rather than the presentation the game resolved from it,
 * so three things the renderer needs are not in the message and are answered here from the game's
 * own tables via `d2itemtoolkit`: the sprite name, the palette shift, and the transform table.
 * Everything else is projected straight out of the capture.
 */

import { useMemo, type ReactNode } from "react";
import type { TooltipEngine } from "d2itemtoolkit";
import type { Character as CapturedCharacter } from "@/generated/captures_pb";
import {
  useResetCapturedAreaTime,
  useResetCapturedKills,
} from "@/hooks/useCapturedResets";
import { CtrlBreakdownHint } from "@/features/items";
import { CharacterChrome } from "./CharacterChrome";
import { InventoryTab, type LabeledContainer } from "./InventoryTab";
import { StatsSkillsTab } from "./StatsSkillsTab";
import { ProgressionPanel } from "./ProgressionPanel";
import { AnalyticsPanel } from "./AnalyticsPanel";
import { toDisplayContainer, toDisplayItem } from "./capturedItem";
import {
  CONTAINER_LABELS,
  STORAGE_IDS,
  stashPageLabel,
  type CharacterFacts,
  type DifficultyProgress,
  type SkillLevels,
} from "./contracts";

/** .d2s status byte bits, on `Identity.char_flags`. */
const HARDCORE_FLAG = 0x04;
const EXPANSION_FLAG = 0x20;
/** ItemStatCost row 12. A capture holds level as a stat because the game does. */
const STAT_LEVEL = 12;

/** What the grids are until the tables arrive. Hoisted so it is one reference: `InventoryTab` is
 *  memoised, and a fresh literal here would defeat that for as long as the engine is loading. */
const NO_STORAGE: LabeledContainer[] = [];

export function CapturedCharacterView({
  captured,
  engine,
  online,
  selector,
}: {
  captured: CapturedCharacter;
  /** Null until the lazily loaded tables arrive; the gear waits, nothing else does. */
  engine: TooltipEngine | null;
  online: boolean;
  selector: ReactNode;
}) {
  const resetKills = useResetCapturedKills();
  const resetAreaTime = useResetCapturedAreaTime();

  const player = captured.player;
  const identity = captured.identity;

  const facts: CharacterFacts = {
    profile: captured.profile,
    charName: player?.name ?? "",
    account: identity?.account ?? "",
    realm: identity?.realm ?? "",
    // Level is stat 12, not a field — a capture stores what the game held, and the game holds
    // level as a stat. There is no derived copy to read.
    level: Number(player?.stats.find((s) => s.id === STAT_LEVEL)?.value ?? 0n),
    charClass: player?.classId ?? 0,
    difficulty: identity?.difficulty ?? 0,
    area: player?.area ?? 0,
    areaEnteredAt: captured.areaEnteredAt,
    hand: player?.hand === 1 ? 1 : 0,
    hardcore: ((identity?.charFlags ?? 0) & HARDCORE_FLAG) !== 0,
    ladder: identity?.ladder ?? false,
    expansion: ((identity?.charFlags ?? 0) & EXPANSION_FLAG) !== 0,
    updatedAt: captured.updatedAt,
  };

  const gear = useMemo(() => {
    if (!engine || !player) return undefined;
    const containers = player.containers;
    const storage: LabeledContainer[] = [];
    for (const id of STORAGE_IDS) {
      const display = toDisplayContainer(id, containers?.[id], engine, {
        wearer: player,
      });
      if (display)
        storage.push({ label: CONTAINER_LABELS[id], container: display });
    }
    const pages = [...(containers?.stash?.pages ?? [])].sort(
      (a, b) => a.index - b.index,
    );
    for (const page of pages) {
      storage.push({
        label: stashPageLabel(page.name, page.index, pages.length),
        container: {
          // A React key, and every page is a "stash", so it is page-qualified.
          id: `stash-${page.index}`,
          width: page.width,
          height: page.height,
          items: page.items.map((i) =>
            toDisplayItem(i, engine, { wearer: player }),
          ),
        },
      });
    }
    return {
      equipped: toDisplayContainer("equipped", containers?.equipped, engine, {
        wearer: player,
      }),
      // The mercenary is its own wearer in v2, with its own equipped container — and its own
      // viewer for requirement purposes, which is why it is rendered against itself.
      // Both units: the merc wears it, but the game times a merc's weapon against the CHARACTER.
      merc: captured.merc
        ? toDisplayContainer(
            "merc",
            captured.merc.containers?.equipped,
            engine,
            { wearer: captured.merc, clientPlayer: player },
          )
        : undefined,
      storage,
    };
  }, [engine, player, captured.merc]);

  const skills = useMemo<SkillLevels[]>(
    () =>
      (player?.skills ?? []).map((s) => ({
        skillId: s.skillId,
        invested: s.hardPoints,
        // v2 sends the bonused level outright, so this is it — no reconstruction from a gear share.
        total: s.level,
      })),
    [player?.skills],
  );

  const progression = useMemo(() => {
    const out: Partial<Record<number, DifficultyProgress>> = {};
    for (const p of captured.progression) {
      out[p.difficulty] = { quests: p.quests, waypoints: p.waypoints };
    }
    return out;
  }, [captured.progression]);

  const displayName = facts.charName || captured.profile;

  return (
    <>
      {/* Only this view and item search render it: the breakdown needs the item's stat lists, which
          only a capture carries. */}
      <CtrlBreakdownHint />
      <CharacterChrome
        facts={facts}
        online={online}
        selector={selector}
        inventory={
          <InventoryTab
            profileKey={captured.profile}
            expansion={facts.expansion}
            activeSet={facts.hand}
            equipped={gear?.equipped}
            merc={gear?.merc}
            storage={gear?.storage ?? NO_STORAGE}
          />
        }
        statsAndSkills={
          <StatsSkillsTab
            stats={player?.stats ?? []}
            difficulty={facts.difficulty}
            skills={skills}
            charClass={facts.charClass}
          />
        }
        progression={
          <ProgressionPanel
            key={captured.profile}
            progression={progression}
            activeDifficulty={facts.difficulty}
          />
        }
        analytics={
          <AnalyticsPanel
            key={captured.profile}
            displayName={displayName}
            activeDifficulty={facts.difficulty}
            // Already the flat tuples the panel counts — v2 never nested them.
            kills={captured.kills}
            areaTime={captured.areaTime}
            killsReset={{
              onReset: () => resetKills.mutateAsync(captured.profile),
              isPending: resetKills.isPending,
            }}
            areaTimeReset={{
              onReset: () => resetAreaTime.mutateAsync(captured.profile),
              isPending: resetAreaTime.isPending,
            }}
          />
        }
      />
    </>
  );
}
