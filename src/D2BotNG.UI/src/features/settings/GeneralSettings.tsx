/**
 * GeneralSettings component
 *
 * Card for configuring server, paths, display, and application behavior.
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
} from "@/components/ui";
import { CloseAction, ItemFont } from "@/generated/settings_pb";
import type { ServerSettings as ServerSettingsType } from "@/generated/settings_pb";
import type { GameSettings as GameSettingsType } from "@/generated/settings_pb";
import type { DisplaySettings as DisplaySettingsType } from "@/generated/settings_pb";

interface GeneralSettingsProps {
  /** Current server settings */
  server?: Partial<ServerSettingsType>;
  /** Current game settings */
  game?: Partial<GameSettingsType>;
  /** Current display settings */
  display?: Partial<DisplaySettingsType>;
  /** Whether to start minimized */
  startMinimized: boolean;
  /** What action to take on close */
  closeAction: CloseAction;
  /** Application base directory path */
  basePath: string;
  /** Callback when server settings change */
  onServerChange: (server: Partial<ServerSettingsType>) => void;
  /** Callback when game settings change */
  onGameChange: (game: Partial<GameSettingsType>) => void;
  /** Callback when display settings change */
  onDisplayChange: (display: Partial<DisplaySettingsType>) => void;
  /** Callback when start minimized changes */
  onStartMinimizedChange: (value: boolean) => void;
  /** Callback when close action changes */
  onCloseActionChange: (value: CloseAction) => void;
  /** Callback when base path changes */
  onBasePathChange: (value: string) => void;
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

export function GeneralSettings({
  server,
  game,
  display,
  startMinimized,
  closeAction,
  basePath,
  onServerChange,
  onGameChange,
  onDisplayChange,
  onStartMinimizedChange,
  onCloseActionChange,
  onBasePathChange,
}: GeneralSettingsProps) {
  const [showD2PathPicker, setShowD2PathPicker] = useState(false);
  const [showBasePathPicker, setShowBasePathPicker] = useState(false);

  return (
    <Card>
      <CardHeader
        title="General Configuration"
        description="Server connection, paths, and application settings."
      />
      <CardContent className="space-y-3">
        {/* Server settings & Game version */}
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
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
            tooltip="Protects the web UI and Discord bot. Clients must authenticate to access controls."
            placeholder="Optional"
            value={server?.password || ""}
            onChange={(e) => onServerChange({ password: e.target.value })}
          />

          <Input
            id="game-version"
            label="Game Version"
            tooltip="Used only for selecting which memory patches to apply. Does not affect any other behavior."
            placeholder="1.14d"
            autoComplete="off"
            value={game?.gameVersion || ""}
            onChange={(e) => onGameChange({ gameVersion: e.target.value })}
          />
        </div>

        {/* App behavior & display */}
        <div className="grid grid-cols-1 items-end gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <label className="flex cursor-pointer items-center gap-3 pb-2">
            <input
              type="checkbox"
              checked={startMinimized}
              onChange={(e) => onStartMinimizedChange(e.target.checked)}
              className="h-4 w-4 rounded border-zinc-600 bg-zinc-800 text-d2-gold focus:ring-d2-gold focus:ring-offset-zinc-900"
            />
            <span className="text-sm text-zinc-300">Start Minimized</span>
          </label>

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

          <label className="flex cursor-pointer items-center gap-3 pb-2">
            <input
              type="checkbox"
              checked={display?.showItemHeader ?? false}
              onChange={(e) =>
                onDisplayChange({ showItemHeader: e.target.checked })
              }
              className="h-4 w-4 rounded border-zinc-600 bg-zinc-800 text-d2-gold focus:ring-d2-gold focus:ring-offset-zinc-900"
            />
            <span className="text-sm text-zinc-300">Show Item Header</span>
          </label>

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

        {/* Paths */}
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <PathInput
            id="d2-install-path"
            label="Diablo II Install Path"
            tooltip="Default game directory for new profiles. Individual profiles can override this."
            placeholder="C:\Program Files\Diablo II"
            autoComplete="off"
            value={game?.d2InstallPath || ""}
            onChange={(e) => onGameChange({ d2InstallPath: e.target.value })}
            onBrowse={() => setShowD2PathPicker(true)}
          />

          <PathInput
            id="base-path"
            label="Base Path"
            tooltip="Root directory for bot data. The data/ folder (profiles, keys, schedules) and d2bs/ directory are read from here."
            placeholder="Directory for d2bs and application files"
            autoComplete="off"
            value={basePath}
            onChange={(e) => onBasePathChange(e.target.value)}
            onBrowse={() => setShowBasePathPicker(true)}
          />
        </div>
      </CardContent>

      <PathSelectorDialog
        open={showD2PathPicker}
        onClose={() => setShowD2PathPicker(false)}
        onSelect={(path) => {
          onGameChange({ d2InstallPath: path });
          setShowD2PathPicker(false);
        }}
        mode="directory"
        title="Select Diablo II Install Directory"
        initialPath={game?.d2InstallPath || ""}
      />

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
  );
}
