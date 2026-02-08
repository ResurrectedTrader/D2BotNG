/**
 * Profile service hooks using TanStack Query
 *
 * Provides hooks for all profile CRUD operations and actions.
 */

import { useMutation } from "@tanstack/react-query";
import { create, type MessageInitShape } from "@bufbuild/protobuf";
import { profileClient } from "@/lib/grpc-client";
import { toast } from "@/stores/toast-store";
import {
  ProfileSchema,
  ReorderProfileRequestSchema,
  UpdateProfileRequestSchema,
} from "@/generated/profiles_pb";
import { ProfileNameSchema, ProfileNamesSchema } from "@/generated/common_pb";

export type ProfileInput = MessageInitShape<typeof ProfileSchema>;
export type UpdateProfileInput = MessageInitShape<
  typeof UpdateProfileRequestSchema
>;

/**
 * Mutation to create a new profile
 * Note: Returns Empty, profile data arrives via event stream
 */
export function useCreateProfile() {
  return useMutation({
    mutationFn: async (profile: ProfileInput) => {
      const request = create(ProfileSchema, profile);
      await profileClient.create(request);
    },
    onSuccess: () => {
      // Data arrives via event stream - no need to invalidate queries
      toast.success("Profile created");
    },
    onError: (error) => {
      toast.error("Failed to create profile", error.message);
    },
  });
}

/**
 * Mutation to update an existing profile
 * Note: Returns Empty, profile data arrives via event stream
 */
export function useUpdateProfile() {
  return useMutation({
    mutationFn: async (input: UpdateProfileInput) => {
      const request = create(UpdateProfileRequestSchema, input);
      await profileClient.update(request);
    },
    onSuccess: () => {
      // Data arrives via event stream - no need to invalidate queries
      toast.success("Profile updated");
    },
    onError: (error) => {
      toast.error("Failed to update profile", error.message);
    },
  });
}

/**
 * Mutation to delete a profile
 * Note: Returns Empty, state change arrives via event stream
 */
export function useDeleteProfile() {
  return useMutation({
    mutationFn: async (name: string) => {
      const request = create(ProfileNameSchema, { name });
      await profileClient.delete(request);
    },
    onSuccess: () => {
      // Data arrives via event stream - no need to invalidate queries
      toast.success("Profile deleted");
    },
    onError: (error) => {
      toast.error("Failed to delete profile", error.message);
    },
  });
}

/**
 * Object containing mutation hooks for profile actions
 * Note: All state changes arrive via event stream
 */
export function useProfileActions() {
  const start = useMutation({
    mutationFn: async (names: string[]) => {
      const request = create(ProfileNamesSchema, { names });
      await profileClient.start(request);
    },
    onSuccess: (_, names) => {
      toast.success(
        names.length === 1
          ? "Profile started"
          : `${names.length} profiles started`,
      );
    },
    onError: (error) => {
      toast.error("Failed to start profile", error.message);
    },
  });

  const stop = useMutation({
    mutationFn: async (names: string[]) => {
      const request = create(ProfileNamesSchema, { names });
      await profileClient.stop(request);
    },
    onSuccess: (_, names) => {
      toast.success(
        names.length === 1
          ? "Profile stopped"
          : `${names.length} profiles stopped`,
      );
    },
    onError: (error) => {
      toast.error("Failed to stop profile", error.message);
    },
  });

  const showWindow = useMutation({
    mutationFn: async (names: string[]) => {
      const request = create(ProfileNamesSchema, { names });
      await profileClient.showWindow(request);
    },
    onSuccess: (_, names) => {
      toast.success(
        names.length === 1 ? "Window shown" : `${names.length} windows shown`,
      );
    },
    onError: (error) => {
      toast.error("Failed to show window", error.message);
    },
  });

  const hideWindow = useMutation({
    mutationFn: async (names: string[]) => {
      const request = create(ProfileNamesSchema, { names });
      await profileClient.hideWindow(request);
    },
    onSuccess: (_, names) => {
      toast.success(
        names.length === 1 ? "Window hidden" : `${names.length} windows hidden`,
      );
    },
    onError: (error) => {
      toast.error("Failed to hide window", error.message);
    },
  });

  const resetStats = useMutation({
    mutationFn: async (name: string) => {
      const request = create(ProfileNameSchema, { name });
      await profileClient.resetStats(request);
    },
    onSuccess: () => {
      toast.success("Stats reset");
    },
    onError: (error) => {
      toast.error("Failed to reset stats", error.message);
    },
  });

  const triggerMule = useMutation({
    mutationFn: async (name: string) => {
      const request = create(ProfileNameSchema, { name });
      await profileClient.triggerMule(request);
    },
    onSuccess: () => {
      toast.success("Mule triggered");
    },
    onError: (error) => {
      toast.error("Failed to trigger mule", error.message);
    },
  });

  const rotateKey = useMutation({
    mutationFn: async (name: string) => {
      const request = create(ProfileNameSchema, { name });
      await profileClient.rotateKey(request);
    },
    onSuccess: () => {
      toast.success("Key rotated");
    },
    onError: (error) => {
      toast.error("Failed to rotate key", error.message);
    },
  });

  const releaseKey = useMutation({
    mutationFn: async (name: string) => {
      const request = create(ProfileNameSchema, { name });
      await profileClient.releaseKey(request);
    },
    onSuccess: () => {
      toast.success("Key released");
    },
    onError: (error) => {
      toast.error("Failed to release key", error.message);
    },
  });

  const setScheduleEnabled = useMutation({
    mutationFn: async ({
      profileName,
      enabled,
    }: {
      profileName: string;
      enabled: boolean;
    }) => {
      await profileClient.setScheduleEnabled({ profileName, enabled });
    },
    onSuccess: (_, { enabled }) => {
      toast.success(enabled ? "Schedule enabled" : "Schedule disabled");
    },
    onError: (error) => {
      toast.error("Failed to update schedule", error.message);
    },
  });

  const reorder = useMutation({
    mutationFn: async ({
      profileName,
      newIndex,
      newGroup,
    }: {
      profileName: string;
      newIndex: number;
      newGroup?: string;
    }) => {
      const request = create(ReorderProfileRequestSchema, {
        profileName,
        newIndex,
        newGroup,
      });
      await profileClient.reorder(request);
    },
    onError: (error) => {
      toast.error("Failed to reorder profile", error.message);
    },
  });

  return {
    start,
    stop,
    showWindow,
    hideWindow,
    resetStats,
    triggerMule,
    rotateKey,
    releaseKey,
    setScheduleEnabled,
    reorder,
  };
}
