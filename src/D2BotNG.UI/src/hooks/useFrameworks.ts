/**
 * Framework service hooks using TanStack Query
 *
 * Mutations for the Frameworks tab. State comes from the event store; mutations
 * return Empty, with updates arriving via the event stream.
 */

import { useMutation } from "@tanstack/react-query";
import { create, type MessageInitShape } from "@bufbuild/protobuf";
import { frameworkClient } from "@/lib/grpc-client";
import { toast } from "@/stores/toast-store";
import {
  FrameworkSchema,
  UpdateFrameworkRequestSchema,
  FrameworkNameSchema,
} from "@/generated/frameworks_pb";

export type FrameworkInput = MessageInitShape<typeof FrameworkSchema>;
export type UpdateFrameworkInput = MessageInitShape<
  typeof UpdateFrameworkRequestSchema
>;

/** Create a framework (name, game/d2bs/dll paths, version). */
export function useCreateFramework() {
  return useMutation({
    mutationFn: async (input: FrameworkInput) => {
      await frameworkClient.createFramework(create(FrameworkSchema, input));
    },
    onSuccess: () => toast.success("Framework added"),
    onError: (error) => toast.error("Failed to add framework", error.message),
  });
}

/** Update a framework (pass originalName to rename an existing one). */
export function useUpdateFramework() {
  return useMutation({
    mutationFn: async (input: UpdateFrameworkInput) => {
      await frameworkClient.updateFramework(
        create(UpdateFrameworkRequestSchema, input),
      );
    },
    onSuccess: () => toast.success("Framework updated"),
    onError: (error) =>
      toast.error("Failed to update framework", error.message),
  });
}

export function useDeleteFramework() {
  return useMutation({
    mutationFn: async (name: string) => {
      await frameworkClient.deleteFramework(
        create(FrameworkNameSchema, { name }),
      );
    },
    onSuccess: () => toast.success("Framework deleted"),
    onError: (error) =>
      toast.error("Failed to delete framework", error.message),
  });
}
