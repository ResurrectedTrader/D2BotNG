/**
 * Schedule service hooks using TanStack Query
 *
 * Provides hooks for Schedule mutations.
 * Note: State comes from event store, mutations return Empty.
 */

import { useMutation } from "@tanstack/react-query";
import { create, type MessageInitShape } from "@bufbuild/protobuf";
import { scheduleClient } from "@/lib/grpc-client";
import { toast } from "@/stores/toast-store";
import {
  ScheduleSchema,
  UpdateScheduleRequestSchema,
} from "@/generated/schedules_pb";
import { ScheduleNameSchema } from "@/generated/common_pb";

export type ScheduleInput = MessageInitShape<typeof ScheduleSchema>;
export type UpdateScheduleInput = MessageInitShape<
  typeof UpdateScheduleRequestSchema
>;

/**
 * Mutation to create a new schedule
 * Note: Returns Empty, data arrives via event stream
 */
export function useCreateSchedule() {
  return useMutation({
    mutationFn: async (schedule: ScheduleInput) => {
      const request = create(ScheduleSchema, schedule);
      await scheduleClient.create(request);
    },
    onSuccess: () => {
      toast.success("Schedule created");
    },
    onError: (error) => {
      toast.error("Failed to create schedule", error.message);
    },
  });
}

/**
 * Mutation to update an existing schedule
 * Note: Returns Empty, data arrives via event stream
 */
export function useUpdateSchedule() {
  return useMutation({
    mutationFn: async (input: UpdateScheduleInput) => {
      const request = create(UpdateScheduleRequestSchema, input);
      await scheduleClient.update(request);
    },
    onSuccess: () => {
      toast.success("Schedule updated");
    },
    onError: (error) => {
      toast.error("Failed to update schedule", error.message);
    },
  });
}

/**
 * Mutation to delete a schedule
 * Note: Returns Empty, state change arrives via event stream
 */
export function useDeleteSchedule() {
  return useMutation({
    mutationFn: async (name: string) => {
      const request = create(ScheduleNameSchema, { name });
      await scheduleClient.delete(request);
    },
    onSuccess: () => {
      toast.success("Schedule deleted");
    },
    onError: (error) => {
      toast.error("Failed to delete schedule", error.message);
    },
  });
}
