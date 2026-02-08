/**
 * Event Store - Central Zustand store for all event-driven state
 *
 * Manages all real-time state from the EventService StreamEvents endpoint.
 * All state changes come via events, ensuring consistent UI state across clients.
 */

import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";
import type { Profile, ProfileStatus } from "@/generated/profiles_pb";
import type { KeyList } from "@/generated/keys_pb";
import type { Schedule } from "@/generated/schedules_pb";
import type { Item } from "@/generated/items_pb";
import type { Settings } from "@/generated/settings_pb";
import type { UpdateStatus } from "@/generated/updates_pb";
import type { Event, KeyUsage, MessageColor } from "@/generated/events_pb";

const MAX_MESSAGES = 100_000;

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
  status: ProfileStatus | undefined;
}

/** Key list data combined with usage */
export interface KeyListWithUsageData {
  keyList: KeyList;
  usage: KeyUsage[];
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

  // Schedules (Map by ID)
  schedules: Map<string, Schedule>;

  // Items (array)
  items: Item[];

  // Entity version - increments when item entities change (for cache invalidation)
  entitiesVersion: number;

  // Settings
  settings: Settings | null;

  // Update status
  updateStatus: UpdateStatus | null;

  // Console messages - unified
  messages: MessageEntry[];

  // Actions
  handleEvent: (event: Event) => void;
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
  schedules: new Map(),
  items: [],
  entitiesVersion: 0,
  settings: null,
  updateStatus: null,
  messages: [],

  setConnected: (connected) => set({ isConnected: connected }),

  handleEvent: (event) => {
    const eventCase = event.event.case;
    if (!eventCase) return;

    switch (eventCase) {
      // Snapshot events (on connect)
      case "profilesSnapshot": {
        const snapshot = event.event.value;
        const profiles = new Map<string, ProfileWithStatusData>();
        for (const p of snapshot.profiles) {
          if (p.profile) {
            profiles.set(p.profile.name, {
              profile: p.profile,
              status: p.status,
            });
          }
        }
        // Mark as loaded after receiving profiles snapshot (always first)
        set({ profiles, hasReceivedInitialData: true });
        break;
      }

      case "keyListsSnapshot": {
        const snapshot = event.event.value;
        const keyLists = new Map<string, KeyListWithUsageData>();
        for (const k of snapshot.keyLists) {
          if (k.keyList) {
            keyLists.set(k.keyList.name, {
              keyList: k.keyList,
              usage: k.usage,
            });
          }
        }
        set({ keyLists });
        break;
      }

      case "schedulesSnapshot": {
        const snapshot = event.event.value;
        const schedules = new Map<string, Schedule>();
        for (const s of snapshot.schedules) {
          schedules.set(s.name, s);
        }
        set({ schedules });
        break;
      }

      // Profile events
      case "profileStatus": {
        const status = event.event.value;
        const profiles = new Map(get().profiles);
        const existing = profiles.get(status.profileName);
        if (existing) {
          profiles.set(status.profileName, {
            profile: existing.profile,
            status: status,
          });
          set({ profiles });
        }
        break;
      }

      // Console messages
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
        const newMessages = [entry, ...get().messages];
        set({ messages: newMessages.slice(0, MAX_MESSAGES) });
        break;
      }

      // Settings & Updates
      case "settings": {
        set({ settings: event.event.value });
        break;
      }

      case "updateStatus": {
        set({ updateStatus: event.event.value });
        break;
      }

      case "entitiesChanged": {
        // Increment version to trigger refetch in CharactersPage
        set({ entitiesVersion: get().entitiesVersion + 1 });
        break;
      }
    }
  },

  clearMessages: (source) => {
    set({
      messages: get().messages.filter((m) => m.source !== source),
    });
  },

  reset: () =>
    set({
      isConnected: false,
      hasReceivedInitialData: false,
      profiles: new Map(),
      keyLists: new Map(),
      schedules: new Map(),
      items: [],
      entitiesVersion: 0,
      settings: null,
      updateStatus: null,
      messages: [],
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
export function useProfileStatus(
  profileName: string,
): ProfileStatus | undefined {
  return useEventStore((state) => state.profiles.get(profileName)?.status);
}

/** Get all profile statuses as a map (memoized with shallow equality) */
export function useAllProfileStatuses(): Record<string, ProfileStatus> {
  return useEventStore(
    useShallow((state) => {
      const statuses: Record<string, ProfileStatus> = {};
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

/** Get messages for a specific source (memoized with shallow equality) */
export function useMessages(source: string): MessageEntry[] {
  return useEventStore(
    useShallow((state) => state.messages.filter((m) => m.source === source)),
  );
}

/** Get all messages (memoized with shallow equality) */
export function useAllMessages(): MessageEntry[] {
  return useEventStore(useShallow((state) => state.messages));
}

/** Get connection status */
export function useIsConnected(): boolean {
  return useEventStore((state) => state.isConnected);
}

/** Check if initial data has been loaded */
export function useIsLoading(): boolean {
  return useEventStore((state) => !state.hasReceivedInitialData);
}

/** Get all items (memoized with shallow equality) */
export function useItems(): Item[] {
  return useEventStore(useShallow((state) => state.items));
}
