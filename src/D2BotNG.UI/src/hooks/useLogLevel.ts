/**
 * Log level hooks
 *
 * Provides mutation hook for setting per-logger log levels.
 * Note: Log level data comes from the event store.
 */

import { useMutation } from "@tanstack/react-query";
import { create } from "@bufbuild/protobuf";
import { loggingClient } from "@/lib/grpc-client";
import { toast } from "@/stores/toast-store";
import { SetLogLevelRequestSchema } from "@/generated/logging_pb";
import type { SinkLogLevel } from "@/generated/logging_pb";

/**
 * Mutation to set a logger's level for the UI console sink.
 * Changes take effect immediately (no save button).
 */
export function useSetLogLevel() {
  return useMutation({
    mutationFn: async ({
      category,
      level,
    }: {
      category: string;
      level: SinkLogLevel;
    }) => {
      const request = create(SetLogLevelRequestSchema, { category, level });
      await loggingClient.setLogLevel(request);
    },
    onError: (error) => {
      toast.error("Failed to set log level", error.message);
    },
  });
}
