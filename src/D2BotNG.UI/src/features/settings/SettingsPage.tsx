/**
 * SettingsPage component
 *
 * Main settings page with tabbed sections and a save button.
 */

import { useState, useCallback, useEffect } from "react";
import { clone, create, equals } from "@bufbuild/protobuf";
import { FrameworkSchema } from "@/generated/frameworks_pb";
import { Button, LoadingSpinner } from "@/components/ui";
import { useUpdateSettings } from "@/hooks/useSettings";
import { useCreateFramework, useUpdateFramework } from "@/hooks";
import type { FrameworkInput } from "@/hooks/useFrameworks";
import { useSettings, useIsLoading, useFramework } from "@/stores/event-store";
import { toast } from "@/stores/toast-store";
import type {
  Settings,
  ServerSettings as ServerSettingsType,
  DiscordSettings as DiscordSettingsType,
  DisplaySettings as DisplaySettingsType,
  LegacyApiSettings as LegacyApiSettingsType,
  StartupSettings as StartupSettingsType,
} from "@/generated/settings_pb";
import {
  CloseAction,
  LegacyApiSettingsSchema,
  SettingsSchema,
} from "@/generated/settings_pb";
import { GeneralSettings } from "./GeneralSettings";
import {
  DiscordSettings,
  type DiscordValidationErrors,
} from "./DiscordSettings";
import { DevSettings } from "./AppSettings";
import { LoggingSettings } from "./LoggingSettings";
import { LegacyApiSettings } from "./LegacyApiSettings";
import { CheckIcon, ArrowPathIcon } from "@heroicons/react/24/outline";
import { Tab, TabGroup, TabList, TabPanel, TabPanels } from "@headlessui/react";

const tabClass =
  "px-4 py-2 text-sm font-medium text-zinc-400 outline-none transition-colors hover:text-zinc-200 data-[selected]:border-b-2 data-[selected]:border-d2-gold data-[selected]:text-zinc-100";

/** A blank "Default" framework, used before the real one arrives on the stream. */
function blankDefaultFramework(): FrameworkInput {
  return {
    name: "Default",
    gameDirectory: "",
    d2bsPath: "",
    dllPaths: ["D2BS.dll"],
    gameVersion: "1.14d",
    screenshotRetentionDays: 0,
    crashLogRetentionDays: 0,
    // Health thresholds deliberately absent: blank inputs = the server's
    // built-in defaults, not values pinned at creation time.
    environment: {},
    usesIni: true,
  };
}

export function SettingsPage() {
  const isLoading = useIsLoading();
  const settings = useSettings();
  const updateSettings = useUpdateSettings();

  // The "Default" framework is edited inline as the Game section in basic mode.
  const defaultFrameworkData = useFramework("Default");
  // Mirror from the framework message, NOT the usage wrapper: the wrapper changes
  // whenever a profile starts/stops (active_profiles), while the store keeps the
  // framework message identity-stable unless its content actually changed — so the
  // effect below can't wipe in-progress Game-card edits on unrelated re-broadcasts.
  const defaultFramework = defaultFrameworkData?.framework;
  const updateFramework = useUpdateFramework();
  const createFramework = useCreateFramework();

  // Local state for form — full Settings message, deep-cloned from the store
  const [localSettings, setLocalSettings] = useState<Settings | null>(null);
  const [localFramework, setLocalFramework] = useState<FrameworkInput | null>(
    null,
  );
  const [isDirty, setIsDirty] = useState(false);
  const [discordErrors, setDiscordErrors] = useState<DiscordValidationErrors>(
    {},
  );

  // Initialize local state when settings load
  useEffect(() => {
    if (settings) {
      // Intentionally do NOT reset isDirty here. A save persists settings and then (in
      // basic mode) the Default framework; the settings broadcast would otherwise clear
      // the dirty flag before the framework mutation resolves — disabling Save and losing
      // the Game-card edits if that second mutation fails. isDirty is cleared only in
      // handleSave, after every mutation has succeeded.
      setLocalSettings(clone(SettingsSchema, settings));
    }
  }, [settings]);

  // Mirror the Default framework into editable local state. Copy ALL fields —
  // basic mode only edits game/health/cleanup, but saving must not wipe the
  // advanced-only DLLs, environment, or usesIni.
  useEffect(() => {
    // Deep-clone the whole message: basic mode only edits game/health/cleanup,
    // but saving must not wipe the advanced-only DLLs, environment, or usesIni.
    setLocalFramework(
      defaultFramework
        ? clone(FrameworkSchema, defaultFramework)
        : blankDefaultFramework(),
    );
  }, [defaultFramework]);

  // Handler for server settings changes
  const handleServerChange = useCallback(
    (updates: Partial<ServerSettingsType>) => {
      setLocalSettings((prev) => {
        if (!prev) return prev;
        return {
          ...prev,
          server: { ...prev.server, ...updates } as ServerSettingsType,
        };
      });
      setIsDirty(true);
    },
    [],
  );

  // Handler for discord settings changes
  const handleDiscordChange = useCallback(
    (updates: Partial<DiscordSettingsType>) => {
      setLocalSettings((prev) => {
        if (!prev) return prev;
        return {
          ...prev,
          discord: { ...prev.discord, ...updates } as DiscordSettingsType,
        };
      });
      setIsDirty(true);
      // Clear errors for fields being edited
      if (
        updates.token !== undefined ||
        updates.serverId !== undefined ||
        updates.webhooks !== undefined ||
        updates.enabled === false
      ) {
        setDiscordErrors((prev) => ({
          ...prev,
          ...(updates.token !== undefined ? { token: undefined } : {}),
          ...(updates.serverId !== undefined ? { serverId: undefined } : {}),
          ...(updates.webhooks !== undefined ? { webhookUrls: undefined } : {}),
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
      setLocalSettings((prev) => {
        if (!prev) return prev;
        return {
          ...prev,
          display: { ...prev.display, ...updates } as DisplaySettingsType,
        };
      });
      setIsDirty(true);
    },
    [],
  );

  // Handler for startup pacing changes
  const handleStartupChange = useCallback(
    (updates: Partial<StartupSettingsType>) => {
      setLocalSettings((prev) => {
        if (!prev) return prev;
        return {
          ...prev,
          startup: { ...prev.startup, ...updates } as StartupSettingsType,
        };
      });
      setIsDirty(true);
    },
    [],
  );

  // Handler for start minimized changes
  const handleStartMinimizedChange = useCallback((value: boolean) => {
    setLocalSettings((prev) => {
      if (!prev) return prev;
      return { ...prev, startMinimized: value };
    });
    setIsDirty(true);
  }, []);

  // Handler for minimize-to-tray changes
  const handleMinimizeToTrayChange = useCallback((value: boolean) => {
    setLocalSettings((prev) => {
      if (!prev) return prev;
      return { ...prev, minimizeToTray: value };
    });
    setIsDirty(true);
  }, []);

  // Handler for close action changes
  const handleCloseActionChange = useCallback((value: CloseAction) => {
    setLocalSettings((prev) => {
      if (!prev) return prev;
      return { ...prev, closeAction: value };
    });
    setIsDirty(true);
  }, []);

  // Handler for base path changes
  const handleBasePathChange = useCallback((value: string) => {
    setLocalSettings((prev) => {
      if (!prev) return prev;
      return { ...prev, basePath: value };
    });
    setIsDirty(true);
  }, []);

  // Handler for advanced mode toggle
  const handleAdvancedModeChange = useCallback((value: boolean) => {
    setLocalSettings((prev) =>
      prev ? { ...prev, advancedMode: value } : prev,
    );
    setIsDirty(true);
  }, []);

  // Handler for Default framework (Game section) changes
  const handleFrameworkChange = useCallback(
    (partial: Partial<FrameworkInput>) => {
      // Spreading the MessageInit union widens $typeName; cast back.
      setLocalFramework((prev) =>
        prev ? ({ ...prev, ...partial } as FrameworkInput) : prev,
      );
      setIsDirty(true);
    },
    [],
  );

  // Handler for legacy API settings changes
  const handleLegacyApiChange = useCallback(
    (updates: Partial<LegacyApiSettingsType>) => {
      setLocalSettings((prev) => {
        if (!prev) return prev;
        const base = prev.legacyApi
          ? clone(LegacyApiSettingsSchema, prev.legacyApi)
          : create(LegacyApiSettingsSchema);
        Object.assign(base, updates);
        return { ...prev, legacyApi: base };
      });
      setIsDirty(true);
    },
    [],
  );

  // Validate Discord settings
  const validateDiscord = useCallback((): DiscordValidationErrors => {
    const errors: DiscordValidationErrors = {};
    const discord = localSettings?.discord;

    if (discord?.enabled) {
      if (!discord.token?.trim()) {
        errors.token = "Bot token is required when Discord is enabled";
      }
      if (!discord.serverId?.trim()) {
        errors.serverId = "Server ID is required when Discord is enabled";
      }
    }

    const webhooks = discord?.webhooks ?? [];
    const webhookUrls = webhooks.map((w) =>
      w.url.trim() === "" ? "Webhook URL is required" : undefined,
    );
    if (webhookUrls.some((e) => e !== undefined)) {
      errors.webhookUrls = webhookUrls;
    }

    return errors;
  }, [localSettings?.discord]);

  // Save handler
  const handleSave = useCallback(async () => {
    if (!localSettings) return;

    // Validate Discord settings
    const errors = validateDiscord();
    if (Object.keys(errors).length > 0) {
      setDiscordErrors(errors);
      return;
    }

    // Snapshot before mutate; the store may update before the mutation resolves.
    const serverChanged =
      settings?.server?.host !== localSettings.server?.host ||
      settings?.server?.port !== localSettings.server?.port;

    // The Game card edits the Default framework inline, so persist it too — but
    // only when its content actually differs from the store: a plain settings
    // save (e.g. server port) must not needlessly rewrite every d2bs.ini and
    // re-scan all mule directories.
    const frameworkChanged =
      localFramework != null &&
      !equals(
        FrameworkSchema,
        create(FrameworkSchema, localFramework),
        defaultFramework ?? create(FrameworkSchema, blankDefaultFramework()),
      );

    try {
      await updateSettings.mutateAsync(localSettings);

      if (localFramework && frameworkChanged) {
        if (defaultFrameworkData) {
          await updateFramework.mutateAsync({
            framework: localFramework,
            originalName: "Default",
          });
        } else {
          await createFramework.mutateAsync(localFramework);
        }
      }

      setIsDirty(false);
      setDiscordErrors({});
      if (serverChanged) {
        toast.warning(
          "Restart required",
          "Server host/port changes take effect after restarting D2BotNG.",
        );
      }
    } catch {
      // Mutation hooks already surface errors via toast.
    }
  }, [
    localSettings,
    localFramework,
    defaultFramework,
    defaultFrameworkData,
    updateSettings,
    updateFramework,
    createFramework,
    validateDiscord,
    settings,
  ]);

  // Loading state - waiting for initial data from event stream
  // DevSettings is always shown so users can change backend URL if needed
  if (isLoading || !localSettings) {
    return (
      <div className="space-y-4">
        <DevSettings />
        <LoadingSpinner fullPage />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Sticky header with save button and tabs */}
      <div className="sticky top-0 z-20 bg-zinc-950 -mx-4 px-4 sm:-mx-6 sm:px-6 lg:-mx-8 lg:px-8 pt-4 pb-0 border-b border-zinc-800/50">
        <div className="flex items-center justify-between gap-3 mb-2">
          <h1 className="text-lg font-bold text-zinc-100">Settings</h1>
          <Button
            onClick={handleSave}
            size="sm"
            disabled={
              !isDirty ||
              updateSettings.isPending ||
              updateFramework.isPending ||
              createFramework.isPending
            }
          >
            {updateSettings.isPending ||
            updateFramework.isPending ||
            createFramework.isPending ? (
              <ArrowPathIcon className="h-4 w-4 animate-spin" />
            ) : (
              <CheckIcon className="h-4 w-4" />
            )}
            Save Changes
          </Button>
        </div>
      </div>

      <DevSettings />

      <TabGroup>
        <TabList className="flex gap-1 border-b border-zinc-700">
          <Tab className={tabClass}>General</Tab>
          <Tab className={tabClass}>Discord</Tab>
          <Tab className={tabClass}>Legacy API</Tab>
          <Tab className={tabClass}>Logging</Tab>
        </TabList>

        <TabPanels className="mt-4">
          <TabPanel>
            <GeneralSettings
              server={localSettings.server}
              display={localSettings.display}
              startup={localSettings.startup}
              startMinimized={localSettings.startMinimized}
              minimizeToTray={localSettings.minimizeToTray ?? true}
              closeAction={localSettings.closeAction}
              basePath={localSettings.basePath}
              advancedMode={localSettings.advancedMode ?? false}
              defaultFramework={localFramework ?? blankDefaultFramework()}
              onServerChange={handleServerChange}
              onDisplayChange={handleDisplayChange}
              onStartupChange={handleStartupChange}
              onStartMinimizedChange={handleStartMinimizedChange}
              onMinimizeToTrayChange={handleMinimizeToTrayChange}
              onCloseActionChange={handleCloseActionChange}
              onBasePathChange={handleBasePathChange}
              onAdvancedModeChange={handleAdvancedModeChange}
              onFrameworkChange={handleFrameworkChange}
            />
          </TabPanel>

          <TabPanel>
            <DiscordSettings
              discord={localSettings.discord}
              onChange={handleDiscordChange}
              errors={discordErrors}
            />
          </TabPanel>

          <TabPanel>
            <LegacyApiSettings
              legacyApi={localSettings.legacyApi}
              onChange={handleLegacyApiChange}
            />
          </TabPanel>

          <TabPanel>
            <LoggingSettings />
          </TabPanel>
        </TabPanels>
      </TabGroup>

      {/* Dirty indicator */}
      {isDirty && (
        <div className="fixed bottom-4 right-4 rounded-lg bg-zinc-800 px-4 py-2 text-sm text-zinc-300 shadow-lg ring-1 ring-zinc-700">
          You have unsaved changes
        </div>
      )}
    </div>
  );
}
