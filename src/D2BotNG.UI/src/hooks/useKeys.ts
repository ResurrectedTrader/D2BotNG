/**
 * Key service hooks using TanStack Query
 *
 * Provides hooks for KeyList mutations.
 * Note: State comes from event store, mutations return Empty.
 */

import { useMutation } from "@tanstack/react-query";
import { create, type MessageInitShape } from "@bufbuild/protobuf";
import { keyClient } from "@/lib/grpc-client";
import { toast } from "@/stores/toast-store";
import {
  KeyListSchema,
  KeyIdentitySchema,
  UpdateKeyListRequestSchema,
} from "@/generated/keys_pb";
import { KeyListNameSchema } from "@/generated/common_pb";

export type KeyListInput = MessageInitShape<typeof KeyListSchema>;
export type UpdateKeyListInput = MessageInitShape<
  typeof UpdateKeyListRequestSchema
>;

/**
 * Mutation to create a new key list
 * Note: Returns Empty, data arrives via event stream
 */
export function useCreateKeyList() {
  return useMutation({
    mutationFn: async (keyList: KeyListInput) => {
      const request = create(KeyListSchema, keyList);
      await keyClient.createKeyList(request);
    },
    onSuccess: () => {
      toast.success("Key list created");
    },
    onError: (error) => {
      toast.error("Failed to create key list", error.message);
    },
  });
}

/**
 * Mutation to update an existing key list
 * Note: Returns Empty, data arrives via event stream
 */
export function useUpdateKeyList() {
  return useMutation({
    mutationFn: async (input: UpdateKeyListInput) => {
      const request = create(UpdateKeyListRequestSchema, input);
      await keyClient.updateKeyList(request);
    },
    onSuccess: () => {
      toast.success("Key list updated");
    },
    onError: (error) => {
      toast.error("Failed to update key list", error.message);
    },
  });
}

/**
 * Mutation to delete a key list
 * Note: Returns Empty, state change arrives via event stream
 */
export function useDeleteKeyList() {
  return useMutation({
    mutationFn: async (name: string) => {
      const request = create(KeyListNameSchema, { name });
      await keyClient.deleteKeyList(request);
    },
    onSuccess: () => {
      toast.success("Key list deleted");
    },
    onError: (error) => {
      toast.error("Failed to delete key list", error.message);
    },
  });
}

/**
 * Mutation to hold a key (prevent it from being used by profiles)
 */
export function useHoldKey() {
  return useMutation({
    mutationFn: async ({
      keyListName,
      keyName,
    }: {
      keyListName: string;
      keyName: string;
    }) => {
      const request = create(KeyIdentitySchema, { keyListName, keyName });
      await keyClient.holdKey(request);
    },
    onError: (error) => {
      toast.error("Failed to hold key", error.message);
    },
  });
}

/**
 * Mutation to release a held key (allow it to be used by profiles again)
 */
export function useReleaseKey() {
  return useMutation({
    mutationFn: async ({
      keyListName,
      keyName,
    }: {
      keyListName: string;
      keyName: string;
    }) => {
      const request = create(KeyIdentitySchema, { keyListName, keyName });
      await keyClient.releaseHeldKey(request);
    },
    onError: (error) => {
      toast.error("Failed to release key", error.message);
    },
  });
}
