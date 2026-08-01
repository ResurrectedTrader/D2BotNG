/**
 * Event Store - Central Zustand store for all event-driven state
 *
 * Manages all real-time state from the EventService StreamEvents endpoint.
 * All state changes come via events, ensuring consistent UI state across clients.
 */

import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";
import { equals } from "@bufbuild/protobuf";
import type { DescMessage, MessageShape } from "@bufbuild/protobuf";
import { ProfileSchema, ProfileStateSchema } from "@/generated/profiles_pb";
import type { Profile, ProfileState } from "@/generated/profiles_pb";
import { KeyListSchema } from "@/generated/keys_pb";
import type { KeyList } from "@/generated/keys_pb";
import { ScheduleSchema } from "@/generated/schedules_pb";
import type { Schedule } from "@/generated/schedules_pb";
import type { Item } from "@/generated/items_pb";
import { SettingsSchema } from "@/generated/settings_pb";
import type { Settings } from "@/generated/settings_pb";
import type { UpdateStatus } from "@/generated/updates_pb";
import { KeyUsageSchema } from "@/generated/events_pb";
import type {
  Event,
  KeyUsage,
  MessageColor,
  ServerInfo,
} from "@/generated/events_pb";
import type { LogLevelEntry } from "@/generated/logging_pb";
import { ProxySchema } from "@/generated/proxies_pb";
import type { Proxy } from "@/generated/proxies_pb";
import { FrameworkSchema } from "@/generated/frameworks_pb";
import type { Framework } from "@/generated/frameworks_pb";
import type { Character } from "@/generated/characters_pb";

const MAX_MESSAGES = 10_000;

// Snapshots are re-broadcast whenever runtime usage changes (e.g. a profile starts
// or stops), decoding brand-new message objects each time even when the underlying
// data is untouched. The helpers below preserve the PREVIOUS instance whenever the
// new one is content-equal, so consumers keyed on object identity — form-seeding
// effects, memos — don't spuriously reset (wiping in-progress edits) on snapshots
// that carry no actual change for them.

/** The previous message when the new one is content-equal, else the new one. */
function stable<Desc extends DescMessage>(
  schema: Desc,
  prev: MessageShape<Desc> | undefined,
  next: MessageShape<Desc>,
): MessageShape<Desc> {
  return prev !== undefined && equals(schema, prev, next) ? prev : next;
}

function sameStrings(a: string[], b: string[]): boolean {
  return a.length === b.length && a.every((v, i) => v === b[i]);
}

function sameMessages<Desc extends DescMessage>(
  schema: Desc,
  a: MessageShape<Desc>[],
  b: MessageShape<Desc>[],
): boolean {
  return a.length === b.length && a.every((v, i) => equals(schema, v, b[i]));
}

/** Stable empty array reference for selectors */
const EMPTY_MESSAGES: MessageEntry[] = [];

/** Console message with unique ID for React key (wraps proto Message) */
export interface MessageEntry {
  id: string;
  source: string;
  content: string;
  timestamp: Date;
  color: MessageColor;
  item?: Item;
}

/** Profile data combined with status */
export interface ProfileWithStatusData {
  profile: Profile;
  status: ProfileState | undefined;
}

/** Key list data combined with usage */
export interface KeyListWithUsageData {
  keyList: KeyList;
  usage: KeyUsage[];
}

/** Proxy combined with usage (profiles configured with it vs currently running with it) */
export interface ProxyWithUsageData {
  proxy: Proxy;
  configuredProfiles: string[];
  activeProfiles: string[];
}

/** Framework combined with usage (profiles configured with it vs currently running with it) */
export interface FrameworkWithUsageData {
  framework: Framework;
  configuredProfiles: string[];
  activeProfiles: string[];
}

interface EventState {
  // Connection status
  isConnected: boolean;
  setConnected: (connected: boolean) => void;

  // Loading state - tracks if we've received initial snapshots
  hasReceivedInitialData: boolean;

  // Profiles (Map by name)
  profiles: Map<string, ProfileWithStatusData>;

  // Key Lists (Map by ID)
  keyLists: Map<string, KeyListWithUsageData>;

  // Proxies (Map by address)
  proxies: Map<string, ProxyWithUsageData>;

  // Frameworks (Map by name)
  frameworks: Map<string, FrameworkWithUsageData>;

  // Schedules (Map by ID)
  schedules: Map<string, Schedule>;

  // Entity version - increments when item entities change (for cache invalidation)
  entitiesVersion: number;

  // Settings
  settings: Settings | null;

  // Update status
  updateStatus: UpdateStatus | null;

  // Console messages (chronological order, newest last)
  messages: MessageEntry[];
  messagesBySource: Map<string, MessageEntry[]>;

  // Log levels (session-only)
  logLevels: LogLevelEntry[];

  // Build-time facts about the connected server (version, compiled-in capabilities)
  serverInfo: ServerInfo | null;

  // Characters (Map by profile name) - live state from the bot engine
  characters: Map<string, Character>;

  // Actions
  handleEvents: (events: Event[]) => void;
  clearMessages: (source: string) => void;
  reset: () => void;
}

let messageCounter = 0;

export const useEventStore = create<EventState>((set, get) => ({
  // Initial state
  isConnected: false,
  hasReceivedInitialData: false,
  profiles: new Map(),
  keyLists: new Map(),
  proxies: new Map(),
  frameworks: new Map(),
  schedules: new Map(),
  entitiesVersion: 0,
  settings: null,
  updateStatus: null,
  messages: [],
  messagesBySource: new Map(),
  logLevels: [],
  serverInfo: null,
  characters: new Map(),

  setConnected: (connected) => set({ isConnected: connected }),

  handleEvents: (events) => {
    if (events.length === 0) return;

    const state = get();

    // Working copies — only cloned on first write of the batch.
    let profiles = state.profiles;
    let keyLists = state.keyLists;
    let proxies = state.proxies;
    let frameworks = state.frameworks;
    let schedules = state.schedules;
    let settings = state.settings;
    let updateStatus = state.updateStatus;
    let messages = state.messages;
    let messagesBySource = state.messagesBySource;
    let logLevels = state.logLevels;
    let serverInfo = state.serverInfo;
    let characters = state.characters;
    let entitiesVersion = state.entitiesVersion;
    let hasReceivedInitialData = state.hasReceivedInitialData;

    let profilesDirty = false;
    let keyListsDirty = false;
    let proxiesDirty = false;
    let frameworksDirty = false;
    let schedulesDirty = false;
    let settingsDirty = false;
    let updateStatusDirty = false;
    let messagesDirty = false;
    let logLevelsDirty = false;
    let serverInfoDirty = false;
    let charactersDirty = false;
    let entitiesVersionDirty = false;
    let hasReceivedInitialDataDirty = false;

    // Per-source lists are reference-shared with subscribers; clone each
    // affected source list at most once per batch, then mutate in place.
    const clonedSources = new Set<string>();

    for (const event of events) {
      const eventCase = event.event.case;
      if (!eventCase) continue;

      switch (eventCase) {
        case "profilesSnapshot": {
          const snapshot = event.event.value;
          const m = new Map<string, ProfileWithStatusData>();
          for (const p of snapshot.profiles) {
            if (p.profile) {
              const prev = profiles.get(p.profile.name);
              const profile = stable(ProfileSchema, prev?.profile, p.profile);
              const status =
                prev?.status !== undefined &&
                equals(ProfileStateSchema, prev.status, p)
                  ? prev.status
                  : p;
              m.set(
                p.profile.name,
                prev && profile === prev.profile && status === prev.status
                  ? prev
                  : { profile, status },
              );
            }
          }
          profiles = m;
          profilesDirty = true;
          hasReceivedInitialData = true;
          hasReceivedInitialDataDirty = true;
          break;
        }

        case "keyListsSnapshot": {
          const snapshot = event.event.value;
          const m = new Map<string, KeyListWithUsageData>();
          for (const k of snapshot.keyLists) {
            if (k.keyList) {
              const prev = keyLists.get(k.keyList.name);
              const keyList = stable(KeyListSchema, prev?.keyList, k.keyList);
              const usage =
                prev && sameMessages(KeyUsageSchema, prev.usage, k.usage)
                  ? prev.usage
                  : k.usage;
              m.set(
                k.keyList.name,
                prev && keyList === prev.keyList && usage === prev.usage
                  ? prev
                  : { keyList, usage },
              );
            }
          }
          keyLists = m;
          keyListsDirty = true;
          break;
        }

        case "proxiesSnapshot": {
          const snapshot = event.event.value;
          const m = new Map<string, ProxyWithUsageData>();
          for (const p of snapshot.proxies) {
            if (p.proxy) {
              const prev = proxies.get(p.proxy.address);
              const proxy = stable(ProxySchema, prev?.proxy, p.proxy);
              const configuredProfiles =
                prev &&
                sameStrings(prev.configuredProfiles, p.configuredProfiles)
                  ? prev.configuredProfiles
                  : p.configuredProfiles;
              const activeProfiles =
                prev && sameStrings(prev.activeProfiles, p.activeProfiles)
                  ? prev.activeProfiles
                  : p.activeProfiles;
              m.set(
                p.proxy.address,
                prev &&
                  proxy === prev.proxy &&
                  configuredProfiles === prev.configuredProfiles &&
                  activeProfiles === prev.activeProfiles
                  ? prev
                  : { proxy, configuredProfiles, activeProfiles },
              );
            }
          }
          proxies = m;
          proxiesDirty = true;
          break;
        }

        case "frameworksSnapshot": {
          const snapshot = event.event.value;
          const m = new Map<string, FrameworkWithUsageData>();
          for (const f of snapshot.frameworks) {
            if (f.framework) {
              const prev = frameworks.get(f.framework.name);
              const framework = stable(
                FrameworkSchema,
                prev?.framework,
                f.framework,
              );
              const configuredProfiles =
                prev &&
                sameStrings(prev.configuredProfiles, f.configuredProfiles)
                  ? prev.configuredProfiles
                  : f.configuredProfiles;
              const activeProfiles =
                prev && sameStrings(prev.activeProfiles, f.activeProfiles)
                  ? prev.activeProfiles
                  : f.activeProfiles;
              m.set(
                f.framework.name,
                prev &&
                  framework === prev.framework &&
                  configuredProfiles === prev.configuredProfiles &&
                  activeProfiles === prev.activeProfiles
                  ? prev
                  : { framework, configuredProfiles, activeProfiles },
              );
            }
          }
          frameworks = m;
          frameworksDirty = true;
          break;
        }

        case "schedulesSnapshot": {
          const snapshot = event.event.value;
          const m = new Map<string, Schedule>();
          for (const s of snapshot.schedules) {
            m.set(s.name, stable(ScheduleSchema, schedules.get(s.name), s));
          }
          schedules = m;
          schedulesDirty = true;
          break;
        }

        case "profileState": {
          const stateVal = event.event.value;
          if (!profilesDirty) {
            profiles = new Map(profiles);
            profilesDirty = true;
          }
          const existing = profiles.get(stateVal.profileName);
          if (existing) {
            profiles.set(stateVal.profileName, {
              profile: stateVal.profile
                ? stable(ProfileSchema, existing.profile, stateVal.profile)
                : existing.profile,
              status: stateVal,
            });
          }
          break;
        }

        case "message": {
          const msg = event.event.value;
          const entry: MessageEntry = {
            id: `msg-${messageCounter++}`,
            source: msg.source,
            content: msg.content,
            timestamp: msg.timestamp
              ? new Date(Number(msg.timestamp.seconds) * 1000)
              : new Date(),
            color: msg.color,
            item: msg.item,
          };

          if (!messagesDirty) {
            messages = messages.slice();
            messagesBySource = new Map(messagesBySource);
            messagesDirty = true;
          }
          messages.push(entry);

          let sourceList = messagesBySource.get(entry.source);
          if (!clonedSources.has(entry.source)) {
            sourceList = sourceList ? sourceList.slice() : [];
            messagesBySource.set(entry.source, sourceList);
            clonedSources.add(entry.source);
          }
          sourceList!.push(entry);
          break;
        }

        case "settings": {
          settings = stable(
            SettingsSchema,
            settings ?? undefined,
            event.event.value,
          );
          settingsDirty = true;
          break;
        }

        case "updateStatus": {
          updateStatus = event.event.value;
          updateStatusDirty = true;
          break;
        }

        case "entitiesChanged": {
          entitiesVersion = entitiesVersion + 1;
          entitiesVersionDirty = true;
          break;
        }

        case "logLevelsSnapshot": {
          logLevels = [...event.event.value.levels];
          logLevelsDirty = true;
          break;
        }

        case "serverInfo": {
          serverInfo = event.event.value;
          serverInfoDirty = true;
          break;
        }

        case "charactersSnapshot": {
          const m = new Map<string, Character>();
          for (const c of event.event.value.characters) {
            m.set(c.profile, c);
          }
          characters = m;
          charactersDirty = true;
          break;
        }

        case "characterState": {
          if (!charactersDirty) {
            characters = new Map(characters);
            charactersDirty = true;
          }
          characters.set(event.event.value.profile, event.event.value);
          break;
        }
      }
    }

    // Trim/rebuild once per batch instead of once per message.
    if (messagesDirty && messages.length > MAX_MESSAGES) {
      messages = messages.slice(-MAX_MESSAGES);
      const rebuilt = new Map<string, MessageEntry[]>();
      for (const m of messages) {
        let list = rebuilt.get(m.source);
        if (!list) {
          list = [];
          rebuilt.set(m.source, list);
        }
        list.push(m);
      }
      messagesBySource = rebuilt;
    }

    const update: Partial<EventState> = {};
    if (profilesDirty) update.profiles = profiles;
    if (hasReceivedInitialDataDirty)
      update.hasReceivedInitialData = hasReceivedInitialData;
    if (keyListsDirty) update.keyLists = keyLists;
    if (proxiesDirty) update.proxies = proxies;
    if (frameworksDirty) update.frameworks = frameworks;
    if (schedulesDirty) update.schedules = schedules;
    if (settingsDirty) update.settings = settings;
    if (updateStatusDirty) update.updateStatus = updateStatus;
    if (messagesDirty) {
      update.messages = messages;
      update.messagesBySource = messagesBySource;
    }
    if (logLevelsDirty) update.logLevels = logLevels;
    if (serverInfoDirty) update.serverInfo = serverInfo;
    if (charactersDirty) update.characters = characters;
    if (entitiesVersionDirty) update.entitiesVersion = entitiesVersion;

    if (Object.keys(update).length > 0) {
      set(update);
    }
  },

  clearMessages: (source) => {
    const messagesBySource = new Map(get().messagesBySource);
    messagesBySource.delete(source);
    set({
      messages: get().messages.filter((m) => m.source !== source),
      messagesBySource,
    });
  },

  reset: () =>
    set({
      isConnected: false,
      hasReceivedInitialData: false,
      profiles: new Map(),
      keyLists: new Map(),
      proxies: new Map(),
      frameworks: new Map(),
      schedules: new Map(),
      entitiesVersion: 0,
      settings: null,
      updateStatus: null,
      messages: [],
      messagesBySource: new Map(),
      logLevels: [],
      serverInfo: null,
      characters: new Map(),
    }),
}));

/** Get entities version (for cache invalidation when entities change) */
export function useEntitiesVersion(): number {
  return useEventStore((state) => state.entitiesVersion);
}

// Selector hooks for common access patterns
// Using useShallow for array/object selectors to prevent unnecessary re-renders

/** Get all profiles as an array (memoized with shallow equality) */
export function useProfiles(): ProfileWithStatusData[] {
  return useEventStore(
    useShallow((state) => Array.from(state.profiles.values())),
  );
}

/** Get a single profile by name */
export function useProfile(
  profileName: string,
): ProfileWithStatusData | undefined {
  return useEventStore((state) => state.profiles.get(profileName));
}

/** Get profile status by name */
export function useProfileState(profileName: string): ProfileState | undefined {
  return useEventStore((state) => state.profiles.get(profileName)?.status);
}

/** Get all profile statuses as a map (memoized with shallow equality) */
export function useAllProfileStates(): Record<string, ProfileState> {
  return useEventStore(
    useShallow((state) => {
      const statuses: Record<string, ProfileState> = {};
      for (const [id, data] of state.profiles) {
        if (data.status) {
          statuses[id] = data.status;
        }
      }
      return statuses;
    }),
  );
}

/** Get all key lists as an array (memoized with shallow equality) */
export function useKeyLists(): KeyListWithUsageData[] {
  return useEventStore(
    useShallow((state) => Array.from(state.keyLists.values())),
  );
}

/** Get all proxies as an array (already sorted by the backend; memoized with shallow equality) */
export function useProxies(): ProxyWithUsageData[] {
  return useEventStore(
    useShallow((state) => Array.from(state.proxies.values())),
  );
}

/** Get all frameworks as an array (already sorted by the backend; memoized with shallow equality) */
export function useFrameworks(): FrameworkWithUsageData[] {
  return useEventStore(
    useShallow((state) => Array.from(state.frameworks.values())),
  );
}

/** Get a single framework by name */
export function useFramework(name: string): FrameworkWithUsageData | undefined {
  return useEventStore((state) => state.frameworks.get(name));
}

/** Get all schedules as an array (memoized with shallow equality) */
export function useSchedules(): Schedule[] {
  return useEventStore(
    useShallow((state) => Array.from(state.schedules.values())),
  );
}

/** Get server settings */
export function useSettings(): Settings | null {
  return useEventStore((state) => state.settings);
}

/** Get update status */
export function useUpdateStatus(): UpdateStatus | null {
  return useEventStore((state) => state.updateStatus);
}

/** Get messages for a specific source (O(1) map lookup) */
export function useMessages(source: string): MessageEntry[] {
  return useEventStore(
    (state) => state.messagesBySource.get(source) ?? EMPTY_MESSAGES,
  );
}

/** Get all messages */
export function useAllMessages(): MessageEntry[] {
  return useEventStore((state) => state.messages);
}

/** Get connection status */
export function useIsConnected(): boolean {
  return useEventStore((state) => state.isConnected);
}

/** Check if initial data has been loaded */
export function useIsLoading(): boolean {
  return useEventStore((state) => !state.hasReceivedInitialData);
}

/** Get all log level entries (memoized with shallow equality) */
export function useLogLevels(): LogLevelEntry[] {
  return useEventStore(useShallow((state) => state.logLevels));
}

/** Build-time facts about the connected server (null until the stream delivers them) */
export function useServerInfo(): ServerInfo | null {
  return useEventStore((state) => state.serverInfo);
}

/** Get all characters as an array (memoized with shallow equality) */
export function useCharacters(): Character[] {
  return useEventStore(
    useShallow((state) => Array.from(state.characters.values())),
  );
}

/** Get a single character by owning profile name */
export function useCharacter(profile: string): Character | undefined {
  return useEventStore((state) => state.characters.get(profile));
}
