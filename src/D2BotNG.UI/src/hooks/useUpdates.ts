/**
 * Update hooks for auto-update functionality
 *
 * Provides mutation hooks for checking and starting updates.
 * Note: Update status data comes from the event store (UpdateStatusChanged events).
 */

import { useMutation } from "@tanstack/react-query";
import { updateClient } from "@/lib/grpc-client";
import { toast } from "@/stores/toast-store";
import type { UpdateStatus } from "@/generated/updates_pb";
import { UpdateState } from "@/generated/updates_pb";

/**
 * Mutation to check for updates manually.
 * Results arrive via UpdateStatusChanged event.
 */
export function useCheckForUpdate() {
  return useMutation({
    mutationFn: async () => {
      await updateClient.checkForUpdate({});
    },
  });
}

/**
 * Mutation to start the update process.
 * Progress and status arrive via UpdateStatusChanged events.
 */
export function useStartUpdate() {
  return useMutation({
    mutationFn: async () => {
      await updateClient.startUpdate({});
    },
    onError: (error) => {
      toast.error("Update failed", error.message);
    },
  });
}

/**
 * Helper function to get human-readable state description
 */
export function getUpdateStateLabel(state: UpdateState): string {
  switch (state) {
    case UpdateState.UNKNOWN:
      return "Unknown";
    case UpdateState.CHECKING:
      return "Checking for updates...";
    case UpdateState.UP_TO_DATE:
      return "Up to date";
    case UpdateState.UPDATE_AVAILABLE:
      return "Update available";
    case UpdateState.DOWNLOADING:
      return "Downloading...";
    case UpdateState.READY_TO_INSTALL:
      return "Ready to install";
    case UpdateState.INSTALLING:
      return "Installing...";
    case UpdateState.ERROR:
      return "Error";
    default:
      return "Unknown";
  }
}

/**
 * Hook that determines if update UI should be visible
 */
export function useUpdateVisibility(
  status: UpdateStatus | null | undefined,
): boolean {
  if (!status) return false;

  // Show notification for these states (not ERROR - only show when update is available)
  return [
    UpdateState.UPDATE_AVAILABLE,
    UpdateState.DOWNLOADING,
    UpdateState.READY_TO_INSTALL,
    UpdateState.INSTALLING,
  ].includes(status.state);
}

// Re-export UpdateState for convenience
export { UpdateState };
