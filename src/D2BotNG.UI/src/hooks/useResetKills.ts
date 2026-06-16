/**
 * useResetKills hook
 *
 * Clears a character's accumulated kill counts. The backend broadcasts the updated
 * (cleared) character over the event stream, so the UI refreshes automatically —
 * no manual invalidation needed.
 */

import { useMutation } from "@tanstack/react-query";
import { create } from "@bufbuild/protobuf";
import { characterClient } from "@/lib/grpc-client";
import { toast } from "@/stores/toast-store";
import { ResetCharacterRequestSchema } from "@/generated/characters_pb";

export function useResetKills() {
  return useMutation({
    mutationFn: async (profile: string) => {
      await characterClient.resetKills(
        create(ResetCharacterRequestSchema, { profile }),
      );
    },
    onSuccess: () => {
      toast.success("Kills reset");
    },
    onError: (error) => {
      toast.error("Failed to reset kills", error.message);
    },
  });
}
