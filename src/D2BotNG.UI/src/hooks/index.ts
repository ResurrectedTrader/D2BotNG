/**
 * Hooks barrel export
 *
 * Re-exports all hooks for convenient imports.
 * Note: Data hooks (useProfiles, useProfileStatus, etc.) now come from event-store.ts
 */

// Profile CRUD and action hooks (mutations only - data comes from event store)
export {
  useCreateProfile,
  useUpdateProfile,
  useDeleteProfile,
  useProfileActions,
} from "./useProfiles";
export type { ProfileInput } from "./useProfiles";

// Key list hooks (mutations only - data comes from event store)
export {
  useCreateKeyList,
  useUpdateKeyList,
  useDeleteKeyList,
  useHoldKey,
  useReleaseKey,
} from "./useKeys";
export type { KeyListInput } from "./useKeys";

// Schedule hooks (mutations only - data comes from event store)
export {
  useCreateSchedule,
  useUpdateSchedule,
  useDeleteSchedule,
} from "./useSchedules";
export type { ScheduleInput } from "./useSchedules";

// Settings hooks
export { useUpdateSettings, useTestDiscord } from "./useSettings";
export type { SettingsInput, TestDiscordInput } from "./useSettings";

// Update hooks
export {
  useCheckForUpdate,
  useStartUpdate,
  useUpdateVisibility,
  getUpdateStateLabel,
  UpdateState,
} from "./useUpdates";

// Event stream hook
export { useEventStream } from "./useEventStream";

// Delete dialog hook
export { useDeleteDialog } from "./useDeleteDialog";

// Console hooks
export { useClearConsole } from "./useClearConsole";

// Directory listing hook
export { useDirectoryListing } from "./useDirectoryListing";

// Profile table columns hook
export {
  useProfileTableColumns,
  PROFILE_COLUMNS,
} from "./useProfileTableColumns";
export type { ProfileColumnKey, ProfileColumn } from "./useProfileTableColumns";

// Localhost check hook
export { useIsLocalhost } from "./useIsLocalhost";

// Hover capability hook
export { useHasHover } from "./useHasHover";
