/**
 * The wire schema v2 view: a character read back from the capture store. See `contracts.ts` for
 * why the two stacks have a view each rather than one converting into the other.
 *
 * v2 stores the raw `D2UnitStrc` capture rather than the presentation the game resolved from it,
 * so three things the renderer needs are not in the message and are answered here from the game's
 * own tables via `d2itemtoolkit`: the sprite name, the palette shift, and the transform table.
 * Everything else is projected straight out of the capture.
 */

import { useMemo, useState, type ReactNode } from "react";
import type { TooltipEngine } from "d2itemtoolkit";
import { ConfirmationDialog } from "@/components/ui";
import {
  GameFlavor,
  GameVariant,
  StashTabKind,
  StashTabType,
  type Character as CapturedCharacter,
  type CharacterKey,
} from "@/generated/captures_pb";
import { SpriteStyle } from "@/generated/settings_pb";
import { ForcedSpriteStyle } from "@/lib/rendering";
import {
  useForgetCapturedCharacter,
  useResetCapturedAreaTime,
  useResetCapturedKills,
} from "@/hooks/useCapturedResets";
import { captureMapKey } from "@/stores/event-store";
import { CtrlBreakdownHint } from "@/features/items";
import { CharacterChrome } from "./CharacterChrome";
import { realmLabel } from "../realms";
import { CharacterVariant } from "./gameNames";
import { InventoryTab, type LabeledContainer } from "./InventoryTab";
import { StatsSkillsTab } from "./StatsSkillsTab";
import { ProgressionPanel } from "./ProgressionPanel";
import { AnalyticsPanel } from "./AnalyticsPanel";
import { toDisplayContainer, toDisplayItem } from "./capturedItem";
import {
  CONTAINER_LABELS,
  STAT_GOLD,
  STAT_LEVEL,
  STAT_STASH_GOLD,
  STORAGE_IDS,
  STAT_EXPERIENCE,
  levelProgress,
  mercThresholds,
  statOf,
  withStashGoldFallback,
  type CharacterFacts,
  type DifficultyProgress,
  type DisplayIdentity,
  type DisplayMercInfo,
  type DisplayStashPage,
  type SkillLevels,
  type StashPageType,
} from "./contracts";

/** .d2s status byte bit, on `Identity.char_flags`. */
const HARDCORE_FLAG = 0x04;

/** What the grids are until the tables arrive. Hoisted so it is one reference: `InventoryTab` is
 *  memoised, and a fresh literal here would defeat that for as long as the engine is loading. */
const NO_STORAGE: LabeledContainer[] = [];
const NO_STASH: DisplayStashPage[] = [];

/** The wire enum to the viewer's word. An unknown value (a newer producer) reads as plain. */
function stashPageType(type: StashTabType): StashPageType {
  switch (type) {
    case StashTabType.ADVANCED_STASH:
      return "advanced";
    case StashTabType.CHRONICLE:
      return "chronicle";
    default:
      return "normal";
  }
}

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
  const forget = useForgetCapturedCharacter();
  const [forgetOpen, setForgetOpen] = useState(false);

  // The server always sets the key; the fallback only satisfies the type.
  const key: CharacterKey = captured.key ?? {
    $typeName: "d2bot.captures.CharacterKey",
    profile: "",
    name: "",
  };
  // One primitive per capture, for the panels that remount per character.
  const identityKey = captureMapKey(key);

  const player = captured.player;
  const identity = captured.identity;
  const d2r = captured.game?.flavor === GameFlavor.D2R;
  // The server's reading of the game version: the reported game type, else the expansion bit of
  // char_flags — both of which the game sets correctly.
  const expansion = (captured.game?.version ?? 2) >= 2;

  const facts: CharacterFacts = {
    profile: key.profile,
    charName: player?.name ?? "",
    account: identity?.account ?? "",
    // None for D2R: its characters log into any realm, so the last one says nothing about them.
    realm: d2r || !identity?.realm ? "" : realmLabel(identity.realm),
    // Level is stat 12, not a field — a capture stores what the game held, and the game holds
    // level as a stat. There is no derived copy to read.
    level: statOf(player?.stats, STAT_LEVEL),
    charClass: player?.classId ?? 0,
    difficulty: identity?.difficulty ?? 0,
    area: player?.area ?? 0,
    areaEnteredAt: captured.areaEnteredAt,
    hand: player?.hand === 1 ? 1 : 0,
    hardcore: ((identity?.charFlags ?? 0) & HARDCORE_FLAG) !== 0,
    ladder: identity?.ladder ?? false,
    expansion,
    updatedAt: captured.updatedAt,
  };

  // The viewer's expansion rules follow the character's game — char_flags' expansion bit, or the
  // game version a newer producer reports — rather than whatever its unit `flagsEx` held (see
  // `asViewer`). Memoised so the gear memo below keeps its identity across renders.
  const viewerGame = useMemo(() => ({ expansion }), [expansion]);

  const gear = useMemo(() => {
    if (!engine || !player) return undefined;
    const containers = player.containers;
    const storage: LabeledContainer[] = [];
    for (const id of STORAGE_IDS) {
      const display = toDisplayContainer(id, containers?.[id], engine, {
        wearer: player,
        ...viewerGame,
      });
      if (display)
        storage.push({ label: CONTAINER_LABELS[id], container: display });
    }
    // Personal pages before shared, each kind in page order — the order the game lists them.
    const pages = [...(containers?.stash?.pages ?? [])].sort(
      (a, b) => a.kind - b.kind || a.index - b.index,
    );
    const stash = withStashGoldFallback(
      pages.map(
        (page): DisplayStashPage => ({
          kind: page.kind === StashTabKind.SHARED ? "shared" : "personal",
          index: page.index,
          name: page.name,
          type: stashPageType(page.type),
          gold: page.gold,
          removeOnly: page.removeOnly,
          season: page.season,
          container: {
            // A React key: every page is a "stash", and an index repeats across kinds.
            id: `stash-${page.kind}-${page.index}`,
            width: page.width,
            height: page.height,
            items: page.items.map((i) =>
              toDisplayItem(i, engine, { wearer: player, ...viewerGame }),
            ),
          },
        }),
      ),
      statOf(player.stats, STAT_STASH_GOLD),
    );
    return {
      stash,
      equipped: toDisplayContainer("equipped", containers?.equipped, engine, {
        wearer: player,
        ...viewerGame,
      }),
      // The mercenary is its own wearer in v2, with its own equipped container — and its own
      // viewer for requirement purposes, which is why it is rendered against itself.
      // Both units: the merc wears it, but the game times a merc's weapon against the CHARACTER.
      // A mercenary is reported only while alive, and one wearing nothing is still there.
      merc: captured.merc
        ? (toDisplayContainer(
            "merc",
            captured.merc.containers?.equipped,
            engine,
            { wearer: captured.merc, clientPlayer: player, ...viewerGame },
          ) ?? { id: "merc", width: 0, height: 0, items: [] })
        : undefined,
      storage,
    };
  }, [engine, player, captured.merc, viewerGame]);

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

  const displayName = facts.charName || key.profile;

  // Memoised: InventoryTab is memo'd, and these ride beside the gear on every capture change.
  const panelIdentity = useMemo<DisplayIdentity>(
    () => ({
      name: displayName,
      level: facts.level,
      charClass: facts.charClass,
      experience: statOf(player?.stats, STAT_EXPERIENCE),
    }),
    [displayName, facts.level, facts.charClass, player?.stats],
  );
  const merc = captured.merc;
  const mercInfo = useMemo<DisplayMercInfo | undefined>(() => {
    if (!merc) return undefined;
    const level = statOf(merc.stats, STAT_LEVEL);
    const { last, next } = mercThresholds(merc.stats, level);
    return {
      name: merc.name ?? "",
      level,
      classId: merc.classId,
      progress: levelProgress(statOf(merc.stats, STAT_EXPERIENCE), last, next),
    };
  }, [merc]);

  return (
    // A D2R character is drawn in D2R's art whatever the app-wide preference says: some of its
    // items have no classic art at all, and the rest should look the way the game that owns them
    // draws them. Null leaves the preference in charge.
    <ForcedSpriteStyle.Provider value={d2r ? SpriteStyle.D2R : null}>
      {/* Skill, monster and class names come from the character's own game's tables. */}
      <CharacterVariant.Provider
        value={captured.game?.variant ?? GameVariant.LOD_114D}
      >
        {/* Only this view and item search render it: the breakdown needs the item's stat lists, which
          only a capture carries. */}
        <CtrlBreakdownHint />
        <CharacterChrome
          facts={facts}
          online={online}
          selector={selector}
          inventory={
            <InventoryTab
              profileKey={identityKey}
              expansion={facts.expansion}
              activeSet={facts.hand}
              gold={statOf(player?.stats, STAT_GOLD)}
              equipped={gear?.equipped}
              merc={gear?.merc}
              storage={gear?.storage ?? NO_STORAGE}
              stash={gear?.stash ?? NO_STASH}
              identity={panelIdentity}
              mercInfo={mercInfo}
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
          actions={
            <button
              type="button"
              onClick={() => setForgetOpen(true)}
              title="Drop this capture — every item, kill count and time in area recorded for it"
              className="rounded px-2 py-1 text-xs font-medium text-zinc-500 hover:bg-zinc-800 hover:text-red-400"
            >
              Forget
            </button>
          }
          progression={
            <ProgressionPanel
              key={identityKey}
              progression={progression}
              activeDifficulty={facts.difficulty}
            />
          }
          analytics={
            <AnalyticsPanel
              key={identityKey}
              displayName={displayName}
              activeDifficulty={facts.difficulty}
              // Already the flat tuples the panel counts — v2 never nested them.
              kills={captured.kills}
              areaTime={captured.areaTime}
              killsReset={{
                onReset: () => resetKills.mutateAsync(key),
                isPending: resetKills.isPending,
              }}
              areaTimeReset={{
                onReset: () => resetAreaTime.mutateAsync(key),
                isPending: resetAreaTime.isPending,
              }}
            />
          }
        />
        {/* Forgetting is the one way a capture leaves the store: deleting the profile does not
          take its characters with it, since the items still exist on the account. */}
        <ConfirmationDialog
          open={forgetOpen}
          title="Forget character"
          description={`Forget the capture of "${displayName}"?`}
          message="This drops every item, kill count and time in area recorded for it. The character itself is untouched; if it reports again, a fresh capture starts."
          confirmLabel="Forget"
          isPending={forget.isPending}
          onConfirm={async () => {
            try {
              await forget.mutateAsync(key);
            } finally {
              setForgetOpen(false);
            }
          }}
          onCancel={() => setForgetOpen(false)}
        />
      </CharacterVariant.Provider>
    </ForcedSpriteStyle.Provider>
  );
}
