/**
 * Settings hooks
 *
 * Provides mutation hook for updating server settings.
 * Note: Settings data comes from the event store.
 */

import { useMutation } from "@tanstack/react-query";
import { create, type MessageInitShape } from "@bufbuild/protobuf";
import { settingsClient } from "@/lib/grpc-client";
import { toast } from "@/stores/toast-store";
import { SettingsSchema, DiscordSettingsSchema } from "@/generated/settings_pb";

/** Input type for updating settings */
export type SettingsInput = MessageInitShape<typeof SettingsSchema>;

/**
 * Mutation to update server settings
 * Note: Returns Empty, updated settings arrive via event stream
 */
export function useUpdateSettings() {
  return useMutation({
    mutationFn: async (settings: SettingsInput) => {
      const request = create(SettingsSchema, settings);
      await settingsClient.update(request);
    },
    onSuccess: () => {
      toast.success("Settings saved", "Your settings have been updated.");
    },
    onError: (error) => {
      toast.error("Failed to save settings", error.message);
    },
  });
}

/** Input type for testing Discord connection */
export type TestDiscordInput = MessageInitShape<typeof DiscordSettingsSchema>;

/**
 * Mutation to test Discord connection
 * Tests the bot token and server ID without saving settings
 */
export function useTestDiscord() {
  return useMutation({
    mutationFn: async (input: TestDiscordInput) => {
      const request = create(DiscordSettingsSchema, input);
      const response = await settingsClient.testDiscord(request);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response;
    },
    onSuccess: (response) => {
      toast.success("Discord test successful", response.message);
    },
    onError: (error) => {
      toast.error("Discord test failed", error.message);
    },
  });
}
