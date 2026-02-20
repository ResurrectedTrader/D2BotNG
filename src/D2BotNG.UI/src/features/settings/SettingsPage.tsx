/**
 * SettingsPage component
 *
 * Main settings page with all settings cards and a save button.
 */

import { useState, useCallback, useEffect } from "react";
import { Button, LoadingSpinner } from "@/components/ui";
import { useUpdateSettings, type SettingsInput } from "@/hooks/useSettings";
import { useSettings, useIsLoading } from "@/stores/event-store";
import type {
  ServerSettings as ServerSettingsType,
  DiscordSettings as DiscordSettingsType,
  DisplaySettings as DisplaySettingsType,
  GameSettings as GameSettingsType,
} from "@/generated/settings_pb";
import { CloseAction, ItemFont } from "@/generated/settings_pb";
import { GeneralSettings } from "./GeneralSettings";
import {
  DiscordSettings,
  type DiscordValidationErrors,
} from "./DiscordSettings";
import { DevSettings } from "./AppSettings";
import { LoggingSettings } from "./LoggingSettings";
import { CheckIcon, ArrowPathIcon } from "@heroicons/react/24/outline";
import { Tab, TabGroup, TabList, TabPanel, TabPanels } from "@headlessui/react";

export function SettingsPage() {
  const isLoading = useIsLoading();
  const settings = useSettings();
  const updateSettings = useUpdateSettings();

  // Local state for form
  const [localSettings, setLocalSettings] = useState<SettingsInput>({});
  const [isDirty, setIsDirty] = useState(false);
  const [discordErrors, setDiscordErrors] = useState<DiscordValidationErrors>(
    {},
  );

  // Initialize local state when settings load
  useEffect(() => {
    if (settings) {
      setLocalSettings({
        startMinimized: settings.startMinimized,
        closeAction: settings.closeAction,
        basePath: settings.basePath,
        server: settings.server ? { ...settings.server } : undefined,
        discord: settings.discord ? { ...settings.discord } : undefined,
        display: settings.display
          ? { ...settings.display }
          : { itemFont: ItemFont.EXOCET },
        game: settings.game ? { ...settings.game } : undefined,
      });
      setIsDirty(false);
    }
  }, [settings]);

  // Handler for server settings changes
  const handleServerChange = useCallback(
    (updates: Partial<ServerSettingsType>) => {
      setLocalSettings((prev) => ({
        ...prev,
        server: { ...prev.server, ...updates } as ServerSettingsType,
      }));
      setIsDirty(true);
    },
    [],
  );

  // Handler for discord settings changes
  const handleDiscordChange = useCallback(
    (updates: Partial<DiscordSettingsType>) => {
      setLocalSettings((prev) => ({
        ...prev,
        discord: { ...prev.discord, ...updates } as DiscordSettingsType,
      }));
      setIsDirty(true);
      // Clear errors for fields being edited
      if (
        updates.token !== undefined ||
        updates.serverId !== undefined ||
        updates.enabled === false
      ) {
        setDiscordErrors((prev) => ({
          ...prev,
          ...(updates.token !== undefined ? { token: undefined } : {}),
          ...(updates.serverId !== undefined ? { serverId: undefined } : {}),
          ...(updates.enabled === false
            ? { token: undefined, serverId: undefined }
            : {}),
        }));
      }
    },
    [],
  );

  // Handler for display settings changes
  const handleDisplayChange = useCallback(
    (updates: Partial<DisplaySettingsType>) => {
      setLocalSettings((prev) => ({
        ...prev,
        display: { ...prev.display, ...updates } as DisplaySettingsType,
      }));
      setIsDirty(true);
    },
    [],
  );

  // Handler for game settings changes
  const handleGameChange = useCallback((updates: Partial<GameSettingsType>) => {
    setLocalSettings((prev) => ({
      ...prev,
      game: { ...prev.game, ...updates } as GameSettingsType,
    }));
    setIsDirty(true);
  }, []);

  // Handler for start minimized changes
  const handleStartMinimizedChange = useCallback((value: boolean) => {
    setLocalSettings((prev) => ({ ...prev, startMinimized: value }));
    setIsDirty(true);
  }, []);

  // Handler for close action changes
  const handleCloseActionChange = useCallback((value: CloseAction) => {
    setLocalSettings((prev) => ({ ...prev, closeAction: value }));
    setIsDirty(true);
  }, []);

  // Handler for base path changes
  const handleBasePathChange = useCallback((value: string) => {
    setLocalSettings((prev) => ({ ...prev, basePath: value }));
    setIsDirty(true);
  }, []);

  // Validate Discord settings
  const validateDiscord = useCallback((): DiscordValidationErrors => {
    const errors: DiscordValidationErrors = {};
    const discord = localSettings.discord;

    if (discord?.enabled) {
      if (!discord.token?.trim()) {
        errors.token = "Bot token is required when Discord is enabled";
      }
      if (!discord.serverId?.trim()) {
        errors.serverId = "Server ID is required when Discord is enabled";
      }
    }

    return errors;
  }, [localSettings.discord]);

  // Save handler
  const handleSave = useCallback(() => {
    // Validate Discord settings
    const errors = validateDiscord();
    if (Object.keys(errors).length > 0) {
      setDiscordErrors(errors);
      return;
    }

    updateSettings.mutate(localSettings, {
      onSuccess: () => {
        setIsDirty(false);
        setDiscordErrors({});
      },
    });
  }, [localSettings, updateSettings, validateDiscord]);

  // Loading state - waiting for initial data from event stream
  // DevSettings is always shown so users can change backend URL if needed
  if (isLoading) {
    return (
      <div className="space-y-4">
        <DevSettings />
        <LoadingSpinner fullPage />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold text-zinc-100">Settings</h1>
          <p className="mt-1 text-sm text-zinc-400">
            Configure your D2Bot server and application settings.
          </p>
        </div>
      </div>

      <TabGroup>
        <TabList className="flex gap-1 border-b border-zinc-700">
          <Tab className="px-4 py-2 text-sm font-medium text-zinc-400 outline-none transition-colors hover:text-zinc-200 data-[selected]:border-b-2 data-[selected]:border-d2-gold data-[selected]:text-zinc-100">
            General
          </Tab>
          <Tab className="px-4 py-2 text-sm font-medium text-zinc-400 outline-none transition-colors hover:text-zinc-200 data-[selected]:border-b-2 data-[selected]:border-d2-gold data-[selected]:text-zinc-100">
            Logging
          </Tab>
        </TabList>

        <TabPanels className="mt-4">
          <TabPanel>
            <div className="space-y-4">
              <div className="flex justify-end">
                <Button
                  onClick={handleSave}
                  disabled={!isDirty || updateSettings.isPending}
                >
                  {updateSettings.isPending ? (
                    <ArrowPathIcon className="h-4 w-4 animate-spin" />
                  ) : (
                    <CheckIcon className="h-4 w-4" />
                  )}
                  Save Changes
                </Button>
              </div>

              <DevSettings />

              <GeneralSettings
                server={localSettings.server}
                game={localSettings.game}
                display={localSettings.display}
                startMinimized={localSettings.startMinimized || false}
                closeAction={localSettings.closeAction || CloseAction.ASK}
                basePath={localSettings.basePath || ""}
                onServerChange={handleServerChange}
                onGameChange={handleGameChange}
                onDisplayChange={handleDisplayChange}
                onStartMinimizedChange={handleStartMinimizedChange}
                onCloseActionChange={handleCloseActionChange}
                onBasePathChange={handleBasePathChange}
              />

              <DiscordSettings
                discord={localSettings.discord}
                onChange={handleDiscordChange}
                errors={discordErrors}
              />
            </div>

            {/* Dirty indicator */}
            {isDirty && (
              <div className="fixed bottom-4 right-4 rounded-lg bg-zinc-800 px-4 py-2 text-sm text-zinc-300 shadow-lg ring-1 ring-zinc-700">
                You have unsaved changes
              </div>
            )}
          </TabPanel>

          <TabPanel>
            <LoggingSettings />
          </TabPanel>
        </TabPanels>
      </TabGroup>
    </div>
  );
}
