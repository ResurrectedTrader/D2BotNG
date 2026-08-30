/**
 * Clears a v2 capture's accumulated kill counts or time-in-area.
 *
 * The v1 pair (`useResetKills` / `useResetAreaTime`) does the same through CharacterService and
 * needs no invalidation, because the backend re-broadcasts the cleared character on the event
 * stream. Captures are pulled rather than streamed, so nothing arrives on its own — these
 * invalidate the character query instead, which is the whole reason they are separate hooks and
 * not a `schema` argument to the v1 ones.
 */

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { captureClient } from "@/lib/grpc-client";
import { toast } from "@/stores/toast-store";
import { captureKeys } from "./useCaptures";

function useCapturedReset(
  action: (profile: string) => Promise<unknown>,
  successMessage: string,
  errorMessage: string,
) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (profile: string) => {
      await action(profile);
      return profile;
    },
    onSuccess: async (profile) => {
      await queryClient.invalidateQueries({
        queryKey: captureKeys.character(profile),
      });
      toast.success(successMessage);
    },
    onError: (error) => {
      toast.error(errorMessage, error.message);
    },
  });
}

export function useResetCapturedKills() {
  return useCapturedReset(
    (profile) => captureClient.resetKills({ profile }),
    "Kills reset",
    "Failed to reset kills",
  );
}

export function useResetCapturedAreaTime() {
  return useCapturedReset(
    (profile) => captureClient.resetAreaTime({ profile }),
    "Area stats reset",
    "Failed to reset area stats",
  );
}
