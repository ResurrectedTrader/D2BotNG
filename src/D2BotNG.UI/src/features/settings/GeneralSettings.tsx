/**
 * GeneralSettings component
 *
 * The General settings tab: a "General Configuration" card (server, application
 * behavior, data folder, startup pacing) plus, in basic mode, a separate "Game"
 * card that edits the Default framework inline.
 */

import { useState } from "react";
import {
  Card,
  CardHeader,
  CardContent,
  Input,
  PasswordInput,
  PathInput,
  Select,
  PathSelectorDialog,
  HelpTooltip,
} from "@/components/ui";
import { CloseAction, ItemFont } from "@/generated/settings_pb";
import type { ServerSettings as ServerSettingsType } from "@/generated/settings_pb";
import type { DisplaySettings as DisplaySettingsType } from "@/generated/settings_pb";
import type { StartupSettings as StartupSettingsType } from "@/generated/settings_pb";
import type { FrameworkInput } from "@/hooks/useFrameworks";
import {
  FrameworkPathFields,
  FrameworkVersionField,
  HealthThresholdFields,
  CleanupRetentionFields,
} from "@/features/frameworks/FrameworkFields";

interface GeneralSettingsProps {
  /** Current server settings */
  server?: Partial<ServerSettingsType>;
  /** Current display settings */
  display?: Partial<DisplaySettingsType>;
  /** Current startup pacing settings */
  startup?: Partial<StartupSettingsType>;
  /** Whether to start minimized */
  startMinimized: boolean;
  /** Whether the minimize button hides to the system tray (vs taskbar) */
  minimizeToTray: boolean;
  /** What action to take on close */
  closeAction: CloseAction;
  /** Application base directory path */
  basePath: string;
  /** Whether advanced mode is enabled (shows Frameworks tab & per-profile framework) */
  advancedMode: boolean;
  /** The Default framework, edited inline as the "Game" card in basic mode */
  defaultFramework: FrameworkInput;
  /** Callback when server settings change */
  onServerChange: (server: Partial<ServerSettingsType>) => void;
  /** Callback when display settings change */
  onDisplayChange: (display: Partial<DisplaySettingsType>) => void;
  /** Callback when startup pacing changes */
  onStartupChange: (startup: Partial<StartupSettingsType>) => void;
  /** Callback when start minimized changes */
  onStartMinimizedChange: (value: boolean) => void;
  /** Callback when minimize-to-tray changes */
  onMinimizeToTrayChange: (value: boolean) => void;
  /** Callback when close action changes */
  onCloseActionChange: (value: CloseAction) => void;
  /** Callback when base path changes */
  onBasePathChange: (value: string) => void;
  /** Callback when advanced mode changes */
  onAdvancedModeChange: (value: boolean) => void;
  /** Callback when a Default framework field changes */
  onFrameworkChange: (partial: Partial<FrameworkInput>) => void;
}

const closeActionOptions = [
  { value: CloseAction.ASK.toString(), label: "Ask" },
  { value: CloseAction.MINIMIZE_TO_TRAY.toString(), label: "Minimize to Tray" },
  { value: CloseAction.EXIT.toString(), label: "Exit" },
];

const fontOptions = [
  { value: ItemFont.EXOCET.toString(), label: "Exocet" },
  { value: ItemFont.CONSOLAS.toString(), label: "Consolas (monospace)" },
  { value: ItemFont.SYSTEM.toString(), label: "System Default" },
];

const checkboxClass =
  "h-4 w-4 rounded border-zinc-600 bg-zinc-800 text-d2-gold focus:ring-d2-gold focus:ring-offset-zinc-900";

export function GeneralSettings({
  server,
  display,
  startup,
  startMinimized,
  minimizeToTray,
  closeAction,
  basePath,
  advancedMode,
  defaultFramework,
  onServerChange,
  onDisplayChange,
  onStartupChange,
  onStartMinimizedChange,
  onMinimizeToTrayChange,
  onCloseActionChange,
  onBasePathChange,
  onAdvancedModeChange,
  onFrameworkChange,
}: GeneralSettingsProps) {
  const [showBasePathPicker, setShowBasePathPicker] = useState(false);

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader
          title="General Configuration"
          description="Server connection, paths, and application settings."
        />
        <CardContent className="space-y-3">
          {/* Server settings */}
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <Input
              id="server-host"
              label="Host"
              tooltip="Address to listen on. Use 0.0.0.0 to allow remote connections."
              placeholder="localhost"
              autoComplete="off"
              value={server?.host || ""}
              onChange={(e) => onServerChange({ host: e.target.value })}
            />

            <Input
              id="server-port"
              label="Port"
              tooltip="Port for the web UI and gRPC connections."
              type="number"
              placeholder="50051"
              autoComplete="one-time-code"
              min={1}
              max={65535}
              value={server?.port?.toString() || ""}
              onChange={(e) => {
                const value = parseInt(e.target.value, 10);
                const port = isNaN(value)
                  ? 0
                  : Math.max(1, Math.min(65535, value));
                onServerChange({ port });
              }}
            />

            <PasswordInput
              id="server-password"
              label="Password"
              tooltip="Protects the web UI. Clients must authenticate to access controls."
              placeholder="Optional"
              value={server?.password || ""}
              onChange={(e) => onServerChange({ password: e.target.value })}
            />
          </div>

          {/* App behavior & display */}
          <div className="grid grid-cols-1 items-end gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <div className="flex flex-col gap-2 pb-2">
              <label className="flex cursor-pointer items-center gap-3">
                <input
                  type="checkbox"
                  checked={startMinimized}
                  onChange={(e) => onStartMinimizedChange(e.target.checked)}
                  className={checkboxClass}
                />
                <span className="text-sm text-zinc-300">Start Minimized</span>
              </label>

              <label
                className="flex cursor-pointer items-center gap-3"
                title="When minimizing, hide to the system tray instead of the taskbar."
              >
                <input
                  type="checkbox"
                  checked={minimizeToTray}
                  onChange={(e) => onMinimizeToTrayChange(e.target.checked)}
                  className={checkboxClass}
                />
                <span className="text-sm text-zinc-300">Minimize to Tray</span>
              </label>
            </div>

            <Select
              id="close-action"
              label="On Close"
              tooltip="What happens when you click the close button on the desktop window."
              options={closeActionOptions}
              value={closeAction.toString()}
              onChange={(e) =>
                onCloseActionChange(parseInt(e.target.value, 10) as CloseAction)
              }
            />

            <div className="flex flex-col gap-2 pb-2">
              <label className="flex cursor-pointer items-center gap-3">
                <input
                  type="checkbox"
                  checked={display?.showItemHeader ?? false}
                  onChange={(e) =>
                    onDisplayChange({ showItemHeader: e.target.checked })
                  }
                  className={checkboxClass}
                />
                <span className="text-sm text-zinc-300">Show Item Header</span>
              </label>

              <label className="flex cursor-pointer items-center gap-3">
                <input
                  type="checkbox"
                  checked={advancedMode}
                  onChange={(e) => onAdvancedModeChange(e.target.checked)}
                  className={checkboxClass}
                />
                <span className="text-sm text-zinc-300">Advanced Mode</span>
                <HelpTooltip
                  text={
                    "Shows the Frameworks tab and lets each profile pick which framework to launch with. Off keeps game settings here and uses a single Default framework.\n\nA framework bundles what's needed to launch a bot: the game executable, the D2BS directory, which DLL(s) to inject, the game version, and health/crash-recovery thresholds."
                  }
                />
              </label>
            </div>

            <Select
              id="item-font"
              label="Item Font"
              tooltip="Font for rendering item name headers on the items page."
              options={fontOptions}
              value={(display?.itemFont ?? ItemFont.EXOCET).toString()}
              onChange={(e) =>
                onDisplayChange({
                  itemFont: parseInt(e.target.value, 10) as ItemFont,
                })
              }
            />
          </div>

          {/* Data folder — application data. Game/D2BS locations live on frameworks. */}
          <PathInput
            id="base-path"
            label="Data Folder"
            tooltip="Where D2BotNG stores its own data — profiles, keys, frameworks, schedules, and saved item images. Game, D2BS, DLL, and version are configured per framework."
            placeholder="Where D2BotNG stores its data"
            autoComplete="off"
            value={basePath}
            onChange={(e) => onBasePathChange(e.target.value)}
            onBrowse={() => setShowBasePathPicker(true)}
          />

          {/* Startup pacing */}
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Input
              id="startup-concurrency"
              label="Max Profiles Starting At Once"
              tooltip="How many profiles can be starting at the same time. Extra profiles wait their turn. Combine with Startup Delay to space out logins. 0 = no limit."
              type="number"
              min={0}
              placeholder="0"
              autoComplete="off"
              value={startup?.concurrency?.toString() ?? "0"}
              onChange={(e) => {
                const value = parseInt(e.target.value, 10);
                onStartupChange({
                  concurrency: isNaN(value) ? 0 : Math.max(0, value),
                });
              }}
            />

            <Input
              id="startup-delay"
              label="Startup Delay (milliseconds)"
              tooltip="How long each profile waits before launching, once it's its turn. Combine with Max Profiles Starting At Once to space out logins."
              type="number"
              min={0}
              placeholder="0"
              autoComplete="off"
              value={startup?.delayMs?.toString() ?? "0"}
              onChange={(e) => {
                const value = parseInt(e.target.value, 10);
                onStartupChange({
                  delayMs: isNaN(value) ? 0 : Math.max(0, value),
                });
              }}
            />
          </div>
        </CardContent>

        <PathSelectorDialog
          open={showBasePathPicker}
          onClose={() => setShowBasePathPicker(false)}
          onSelect={(path) => {
            onBasePathChange(path);
            setShowBasePathPicker(false);
          }}
          mode="directory"
          title="Select Base Directory"
          initialPath={basePath}
        />
      </Card>

      {/* Game — edits the Default framework directly via the shared field
          fragments. In advanced mode this (and DLLs / env vars) is managed per
          framework on the Frameworks tab instead. */}
      {!advancedMode && (
        <Card>
          <CardHeader
            title="Game"
            description="Where the game and botting framework live, and how the manager monitors them."
          />
          <CardContent className="space-y-3">
            <FrameworkPathFields
              value={defaultFramework}
              onChange={onFrameworkChange}
              idPrefix="default"
            />
            <FrameworkVersionField
              value={defaultFramework}
              onChange={onFrameworkChange}
              idPrefix="default"
            />

            {/* Health & Crash Recovery */}
            <div className="flex items-center gap-1.5 pt-1">
              <span className="text-sm font-medium text-zinc-400">
                Health &amp; Crash Recovery
              </span>
              <HelpTooltip text="How the manager monitors and recovers games. Set a timeout to 0 to disable that watchdog." />
            </div>
            <HealthThresholdFields
              value={defaultFramework}
              onChange={onFrameworkChange}
              idPrefix="default"
            />

            {/* Game Directory Cleanup */}
            <CleanupRetentionFields
              value={defaultFramework}
              onChange={onFrameworkChange}
              idPrefix="default"
            />
          </CardContent>
        </Card>
      )}
    </div>
  );
}
