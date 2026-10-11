/**
 * CharacterViewer — the "Character" tab.
 *
 * There are two views because there are two stacks: `StreamedCharacterView` for wire schema v1
 * (pushed on the event stream) and `CapturedCharacterView` for v2 (pulled from the capture store).
 * See `contracts.ts` for why neither converts into the other. This shell owns what genuinely is
 * common — the character list, the selection, and liveness — and renders whichever view the
 * selected character's schema calls for.
 *
 * Online status is derived live from the owning profile's run state rather than from anything the
 * character sent, so the dot is accurate even for a stopped profile. Neither message carries an
 * `online` field for exactly that reason. A v2 capture is keyed by profile AND character name —
 * a mule profile holds one per mule — so for those "online" also means "the character this
 * profile reported most recently": the mules it logged into earlier are offline even while it
 * runs.
 */

import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import clsx from "clsx";
import { UserIcon, ChevronUpDownIcon } from "@heroicons/react/24/outline";
import { EmptyState } from "@/components/ui";
import {
  GameFlavor,
  GameVariant,
  type CharacterKey,
  type CharacterSummary,
} from "@/generated/captures_pb";
import { FlavorBadge } from "../FlavorBadge";
import { realmLabel, realmMatters } from "../realms";
import { isActive } from "@/features/profiles/profile-states";
import {
  captureMapKey,
  useCapturedSummaries,
  useCharacters,
  useProfiles,
} from "@/stores/event-store";
import { useCapturedCharacter } from "@/hooks/useCaptures";
import { useToolkit } from "@/hooks/useToolkit";
import { StreamedCharacterView } from "./StreamedCharacterView";
import { CapturedCharacterView } from "./CapturedCharacterView";

/**
 * A row in the picker. Deliberately the little that both stacks agree on: enough to search,
 * label and choose, and nothing that would need either schema interpreted to fill in.
 */
interface ListEntry {
  /**
   * React key and selection identity. The store's own map key for a capture; the profile name
   * for a v1 character, which the stream keys by profile alone. Never shown and never sent.
   */
  id: string;
  /** The capture's key. Absent for a v1 character, which has no key beyond its profile. */
  key?: CharacterKey;
  profile: string;
  charName: string;
  account: string;
  realm: string;
  schema: 1 | 2;
  /**
   * The character its profile reported most recently. Always true for v1 and for a profile with
   * one capture; false for the earlier mules of a mule profile, which are offline even while the
   * profile runs.
   */
  latest: boolean;
  /** The profile holds several captures, so the name alone does not say which profile this is. */
  ambiguous: boolean;
  /**
   * Which game, as the server read it (see `Game` in captures.proto). A v1 character is LOD: the
   * v1 stack predates D2R support, and nothing on its wire says otherwise.
   */
  flavor: GameFlavor;
  /** The tables the character's items are read with. LOD 1.14d for v1. */
  variant: GameVariant;
}

/** Later of two summaries by their last report; ties go to the first argument. */
function laterOf(a: CharacterSummary, b: CharacterSummary): CharacterSummary {
  const as = a.updatedAt?.seconds ?? 0n;
  const bs = b.updatedAt?.seconds ?? 0n;
  if (as !== bs) return as > bs ? a : b;
  return (a.updatedAt?.nanos ?? 0) >= (b.updatedAt?.nanos ?? 0) ? a : b;
}

/**
 * The character name, doubling as the character selector: clicking it opens a searchable dropdown
 * of all known characters. Embedded in the header line so it doesn't cost a separate row.
 */
function CharacterSelector({
  entries,
  selected,
  onSelect,
  isOnline,
  showFlavor,
}: {
  entries: ListEntry[];
  selected: ListEntry;
  onSelect: (id: string) => void;
  isOnline: (entry: ListEntry) => boolean;
  /** More than one game is in the list, so each row says which it is. */
  showFlavor: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (
        containerRef.current &&
        !containerRef.current.contains(e.target as Node)
      ) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return entries;
    return entries.filter((c) =>
      [c.charName, c.profile, c.account, shownRealm(c)].some((s) =>
        s.toLowerCase().includes(q),
      ),
    );
  }, [entries, search]);

  // By profile, in the order the list already has (online first), so a mule profile's characters
  // sit together under one header instead of each restating which profile it is.
  const groups = useMemo(() => {
    const byProfile = new Map<string, ListEntry[]>();
    for (const c of filtered) {
      const group = byProfile.get(c.profile) ?? [];
      group.push(c);
      byProfile.set(c.profile, group);
    }
    return [...byProfile.entries()];
  }, [filtered]);

  function handleSelect(id: string) {
    onSelect(id);
    setOpen(false);
    setSearch("");
  }

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="group -mx-1 flex items-center gap-2 rounded px-1 hover:bg-zinc-800/60 focus:outline-none"
      >
        <span
          className={clsx(
            "h-2.5 w-2.5 flex-shrink-0 rounded-full",
            isOnline(selected) ? "bg-green-500" : "bg-zinc-600",
          )}
        />
        <h2 className="text-xl font-bold text-zinc-100">
          {selected.charName || selected.profile}
        </h2>
        {showFlavor && <FlavorBadge flavor={selected.flavor} />}
        {/* Named only when the name alone would not say which profile this came through. */}
        {selected.ambiguous && (
          <span className="text-sm text-zinc-500">{selected.profile}</span>
        )}
        <ChevronUpDownIcon className="h-5 w-5 flex-shrink-0 text-zinc-500 group-hover:text-zinc-300" />
      </button>

      {open && (
        <div className="absolute left-0 z-50 mt-1 max-h-80 w-80 overflow-hidden rounded-lg bg-zinc-800 shadow-lg ring-1 ring-zinc-700">
          <div className="border-b border-zinc-700 p-2">
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search characters..."
              className="block w-full rounded border-0 bg-zinc-900 px-2 py-1.5 text-sm text-zinc-100 placeholder:text-zinc-500 focus:outline-none focus:ring-1 focus:ring-d2-gold"
              autoFocus
            />
          </div>
          <div className="max-h-60 overflow-y-auto p-1">
            {groups.map(([profile, members]) => {
              // Every profile is a header over its characters, even a profile with one: the tags
              // then sit on one column of headers rather than on every row, which reads more easily.
              // They move to the header when every character under it shares them — the normal
              // case, since a profile runs one engine against one game — and stay on the rows that
              // differ otherwise.
              const flavorShared = members.every(
                (m) => m.flavor === members[0].flavor,
              );
              const schemaShared = members.every(
                (m) => m.schema === members[0].schema,
              );
              return (
                <div key={profile}>
                  {/* A header, not an option: what is chosen here is a character. */}
                  <div className="flex items-center gap-2 px-2 pb-0.5 pt-1.5 text-xs">
                    <span className="truncate font-medium text-zinc-300">
                      {profile}
                    </span>
                    <span className="text-zinc-500">
                      ({members.length}{" "}
                      {members.length === 1 ? "character" : "characters"})
                    </span>
                    <span className="ml-auto flex items-center gap-1">
                      {showFlavor && flavorShared && (
                        <FlavorBadge flavor={members[0].flavor} />
                      )}
                      {schemaShared && members[0].schema === 1 && (
                        <SchemaBadge />
                      )}
                    </span>
                  </div>
                  {members.map((c) => (
                    <EntryRow
                      key={c.id}
                      entry={c}
                      selected={c.id === selected.id}
                      online={isOnline(c)}
                      onSelect={handleSelect}
                      indent
                      label={c.charName || profile}
                      badges={
                        <Badges
                          entry={c}
                          showFlavor={showFlavor && !flavorShared}
                          showSchema={!schemaShared}
                        />
                      }
                    />
                  ))}
                </div>
              );
            })}
            {filtered.length === 0 && (
              <p className="px-2 py-3 text-center text-sm text-zinc-500">
                No matches found
              </p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * The realm a row names: a LOD character's, and none for D2R, whose characters are not bound to a
 * realm — they can log into any of them, so the last one played on says nothing about the
 * character.
 */
function shownRealm(entry: ListEntry): string {
  return realmMatters(entry.flavor) && entry.realm
    ? realmLabel(entry.realm)
    : "";
}

/** One selectable character in the dropdown. */
function EntryRow({
  entry,
  selected,
  online,
  onSelect,
  label,
  badges,
  indent = false,
}: {
  entry: ListEntry;
  selected: boolean;
  online: boolean;
  onSelect: (id: string) => void;
  label: ReactNode;
  badges: ReactNode;
  indent?: boolean;
}) {
  const detail = [entry.account, shownRealm(entry)].filter(Boolean).join(" · ");
  return (
    <button
      type="button"
      onClick={() => onSelect(entry.id)}
      className={clsx(
        "flex w-full items-center gap-2 rounded py-1.5 pr-2 text-left text-sm transition-colors",
        indent ? "pl-5" : "pl-2",
        selected
          ? "bg-d2-gold/20 text-d2-gold"
          : "text-zinc-300 hover:bg-zinc-700",
      )}
    >
      <span
        className={clsx(
          "h-2 w-2 flex-shrink-0 rounded-full",
          online ? "bg-green-500" : "bg-zinc-600",
        )}
      />
      <span className="flex-1 truncate">{label}</span>
      {badges}
      {detail && (
        <span className="truncate text-xs text-zinc-500">{detail}</span>
      )}
    </button>
  );
}

/** A row's own tags, for the ones a group header did not already carry. */
function Badges({
  entry,
  showFlavor,
  showSchema = true,
}: {
  entry: ListEntry;
  showFlavor: boolean;
  showSchema?: boolean;
}) {
  return (
    <>
      {showFlavor && <FlavorBadge flavor={entry.flavor} />}
      {showSchema && entry.schema === 1 && <SchemaBadge />}
    </>
  );
}

/**
 * Marks a v1 character. Only v1 is marked: v2 is the normal case now, and what the tag explains is
 * the exception — a v1 character's items carry no stat lists, so it cannot be searched.
 */
function SchemaBadge() {
  return (
    <span
      className="rounded bg-zinc-700/60 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-zinc-400"
      title="Wire schema v1 — streamed live, presentation resolved by the game engine; not searchable"
    >
      v1
    </span>
  );
}

/**
 * Nothing to show: no character, of either schema, has ever reported.
 *
 * It is also the v1 fallback for a selection the stream lookup cannot resolve — a state that does
 * not arise, since the list and the lookup are both built from the same render's `streamed`. That
 * branch stays because it is what narrows the selected character to a non-optional one.
 */
function NoCharacters() {
  return (
    <EmptyState
      icon={UserIcon}
      title="No live characters yet"
      description="Character state appears here once a running profile reports it. Start a profile to begin tracking."
    />
  );
}

export function CharacterViewer() {
  const streamed = useCharacters();
  const profiles = useProfiles();

  // A profile fills exactly one stack — decided by the engine DLL it runs — so the two lists are
  // normally disjoint. Where they do overlap (a profile that changed engines) v2 wins: it is the
  // richer capture and the one still being written.
  const capturedSummaries = useCapturedSummaries();

  const entries = useMemo<ListEntry[]>(() => {
    // Per profile: how many captures it holds, and which reported last. Both are what the row
    // needs to say beyond its own summary — whether its name is enough, and whether it is live.
    const perProfile = new Map<
      string,
      { count: number; latest: CharacterSummary }
    >();
    for (const s of capturedSummaries ?? []) {
      if (!s.key) continue;
      const seen = perProfile.get(s.key.profile);
      perProfile.set(s.key.profile, {
        count: (seen?.count ?? 0) + 1,
        latest: seen ? laterOf(seen.latest, s) : s,
      });
    }

    const captured = (capturedSummaries ?? []).flatMap<ListEntry>((s) => {
      if (!s.key) return [];
      const group = perProfile.get(s.key.profile)!;
      return [
        {
          id: captureMapKey(s.key),
          key: s.key,
          profile: s.key.profile,
          charName: s.key.name,
          account: s.identity?.account ?? "",
          realm: s.identity?.realm ?? "",
          schema: 2,
          latest: group.latest === s,
          ambiguous: group.count > 1,
          flavor: s.game?.flavor ?? GameFlavor.LOD,
          variant: s.game?.variant ?? GameVariant.LOD_114D,
        },
      ];
    });
    return [
      ...captured,
      ...streamed
        .filter((c) => !perProfile.has(c.profile))
        .map<ListEntry>((c) => ({
          id: c.profile,
          profile: c.profile,
          charName: c.charName,
          account: c.account,
          realm: c.realm,
          schema: 1,
          latest: true,
          ambiguous: false,
          flavor: GameFlavor.LOD,
          variant: GameVariant.LOD_114D,
        })),
    ];
  }, [streamed, capturedSummaries]);

  // Online is derived from the owning profile's run state (keyed by name), so it tracks
  // start/stop live and never goes stale on a persisted character.
  const onlineProfiles = useMemo(
    () =>
      new Set(
        profiles
          .filter((p) => isActive(p.status?.state))
          .map((p) => p.profile.name),
      ),
    [profiles],
  );
  const isOnline = (entry: ListEntry) =>
    entry.latest && onlineProfiles.has(entry.profile);

  // Online characters first (stable within each group, so the natural order is otherwise
  // preserved). Drives both the dropdown order and the default selection below.
  const sorted = useMemo(
    () =>
      [...entries].sort(
        (a, b) =>
          (a.latest && onlineProfiles.has(a.profile) ? 0 : 1) -
          (b.latest && onlineProfiles.has(b.profile) ? 0 : 1),
      ),
    [entries, onlineProfiles],
  );

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = useMemo(() => {
    if (sorted.length === 0) return undefined;
    return sorted.find((c) => c.id === selectedId) ?? sorted[0];
  }, [sorted, selectedId]);

  // A v2 entry is only a summary. The whole capture is a separate pull and the tables it needs to
  // resolve sprites are a lazy chunk, so both arrive after the list does — and only for v2, so
  // selecting a v1 character costs neither request.
  const isCaptured = selected?.schema === 2;
  const { data: capturedDetail } = useCapturedCharacter(
    isCaptured ? selected.key : null,
  );
  // The selected character's own game tables: a D2R capture's ids index D2R's, which number the
  // same items differently. Read off the summary, so it is known before the capture arrives.
  const engine = useToolkit(!!isCaptured, selected?.variant);

  // A badge only says something when its absence would too — a list of one game needs none.
  const showFlavor = useMemo(
    () => new Set(entries.map((e) => e.flavor)).size > 1,
    [entries],
  );

  // On entering the view, default to the first online character (falling back to the first
  // character when none are online). We commit that pick to state once — rather than leaving the
  // selection implicit — so it stays put as profiles start/stop and the list re-sorts underneath;
  // the user's later picks win.
  //
  // The same commit covers a selection that stops existing — a v1 character can drop out of the
  // stream, a capture can be forgotten. `selected` falls back to the first entry for the render,
  // and without writing that back the state would go on naming the vanished one, so the view
  // would jump back to it if it ever returned.
  useEffect(() => {
    if (sorted.length === 0) return;
    const stillThere = sorted.some((c) => c.id === selectedId);
    if (selectedId !== null && stillThere) return;
    setSelectedId(sorted[0].id);
  }, [selectedId, sorted]);

  const streamedSelected = useMemo(
    () =>
      selected && !isCaptured
        ? streamed.find((c) => c.profile === selected.profile)
        : undefined,
    [selected, isCaptured, streamed],
  );

  if (!selected) {
    return <NoCharacters />;
  }

  const selector = (
    <CharacterSelector
      entries={sorted}
      selected={selected}
      onSelect={setSelectedId}
      isOnline={isOnline}
      showFlavor={showFlavor}
    />
  );
  const online = isOnline(selected);

  // Neither view below is keyed on the character. The tab strip lives inside them, so remounting
  // one per switch would throw a reader on Progression or Analytics back to Inventory; the panels
  // that genuinely have to re-default per character carry keys of their own instead.
  if (isCaptured) {
    // The list knows this character exists before the capture arrives — it is a separate pull, and
    // the cache is keyed per capture, so EVERY switch to a v2 character passes through here.
    // Rendering the chrome against a half-filled message would show a level 0 nobody, so the body
    // waits for the pull; the picker does not, or the reader would lose the only way back out.
    if (!capturedDetail) {
      return (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            {selector}
          </div>
          <EmptyState
            icon={UserIcon}
            title={`Loading ${selected.charName || selected.profile}`}
            description="Reading the character's capture."
          />
        </div>
      );
    }
    return (
      <CapturedCharacterView
        captured={capturedDetail}
        engine={engine}
        online={online}
        selector={selector}
      />
    );
  }

  if (!streamedSelected) {
    return <NoCharacters />;
  }

  return (
    <StreamedCharacterView
      character={streamedSelected}
      online={online}
      selector={selector}
    />
  );
}
