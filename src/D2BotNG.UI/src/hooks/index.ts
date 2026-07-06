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

// Proxy hooks (mutations only - data comes from event store)
export {
  useCreateProxy,
  useUpdateProxy,
  useDeleteProxy,
  useImportProxies,
  useProxyTester,
} from "./useProxies";
export type { UpdateProxyInput, ProxyTestResult } from "./useProxies";

// Framework hooks (mutations only - data comes from event store)
export {
  useCreateFramework,
  useUpdateFramework,
  useDeleteFramework,
} from "./useFrameworks";
export type { FrameworkInput, UpdateFrameworkInput } from "./useFrameworks";

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

// Item hooks
export { useRemoveItem } from "./useRemoveItem";
export type { RemoveItemInput } from "./useRemoveItem";

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

// Entry script options hook
export { useEntryScripts } from "./useEntryScripts";
export type { EntryScriptOption } from "./useEntryScripts";

// Scroll restoration hook
export { useScrollRestoration } from "./useScrollRestoration";
