/**
 * Shared framework field fragments, rendered by both FrameworkForm and the
 * basic-mode Settings "Game" card (which edits the Default framework inline).
 * Single source of truth for labels/tooltips/clamping; hosts supply the chrome.
 *
 * Fully controlled `value`/`onChange(partial)` over FrameworkInput. Numeric
 * fields clamp per keystroke (the Settings card saves via a plain button, so
 * native <form> min= validation cannot be relied on). Optional health
 * thresholds map blank -> undefined: the proto field stays absent and the
 * server default (shown as the placeholder) applies.
 */

import { useState } from "react";
import { Input, PathInput, PathSelectorDialog } from "@/components/ui";
import type { FrameworkInput } from "@/hooks/useFrameworks";

export interface FrameworkFieldsProps {
  value: FrameworkInput;
  onChange: (partial: Partial<FrameworkInput>) => void;
  /** Prefix for input element ids so both surfaces can mount without collisions. */
  idPrefix: string;
}

/** Clamp a number input's raw value to an integer >= min. */
function toClampedInt(raw: string, min: number): number {
  const value = parseInt(raw, 10);
  return isNaN(value) ? min : Math.max(min, value);
}

/** Parse an OPTIONAL threshold input: blank = absent (server default applies). */
function toOptionalInt(raw: string, min: number): number | undefined {
  const value = parseInt(raw, 10);
  return isNaN(value) ? undefined : Math.max(min, value);
}

/** Game Directory and D2BS Path inputs, each with its directory picker. */
export function FrameworkPathFields({
  value,
  onChange,
  idPrefix,
}: FrameworkFieldsProps) {
  const [showGameDirectoryPicker, setShowGameDirectoryPicker] = useState(false);
  const [showD2bsPathPicker, setShowD2bsPathPicker] = useState(false);

  return (
    <>
      <PathInput
        id={`${idPrefix}-game-directory`}
        label="Game Directory"
        tooltip="The Diablo II install folder. Used to clean up old screenshots and crash logs, and as the starting folder when browsing for a profile's executable. The launched game is each profile's own Diablo II Path."
        value={value.gameDirectory ?? ""}
        onChange={(e) => onChange({ gameDirectory: e.target.value })}
        placeholder="C:\Games\Diablo II"
        onBrowse={() => setShowGameDirectoryPicker(true)}
      />
      <PathInput
        id={`${idPrefix}-d2bs-path`}
        label="D2BS Path"
        tooltip="The botting-framework directory containing d2bs.ini, kolbot/, and the inject DLL(s)."
        value={value.d2bsPath ?? ""}
        onChange={(e) => onChange({ d2bsPath: e.target.value })}
        placeholder="C:\Games\Diablo II\d2bs"
        onBrowse={() => setShowD2bsPathPicker(true)}
      />

      <PathSelectorDialog
        open={showGameDirectoryPicker}
        onClose={() => setShowGameDirectoryPicker(false)}
        onSelect={(path) => {
          onChange({ gameDirectory: path });
          setShowGameDirectoryPicker(false);
        }}
        mode="directory"
        title="Select Diablo II Install Directory"
        initialPath={value.gameDirectory ?? ""}
      />
      <PathSelectorDialog
        open={showD2bsPathPicker}
        onClose={() => setShowD2bsPathPicker(false)}
        onSelect={(path) => {
          onChange({ d2bsPath: path });
          setShowD2bsPathPicker(false);
        }}
        mode="directory"
        title="Select D2BS Directory"
        initialPath={value.d2bsPath ?? ""}
      />
    </>
  );
}

/** The game version used for memory-patch selection. */
export function FrameworkVersionField({
  value,
  onChange,
  idPrefix,
}: FrameworkFieldsProps) {
  return (
    <Input
      id={`${idPrefix}-game-version`}
      label="Version"
      tooltip="Only used to pick which memory patches to apply (e.g. 1.14d). Doesn't affect anything else."
      value={value.gameVersion ?? ""}
      onChange={(e) => onChange({ gameVersion: e.target.value })}
      placeholder="1.14d"
    />
  );
}

/** The four optional health/crash-recovery thresholds (blank = server default). */
export function HealthThresholdFields({
  value,
  onChange,
  idPrefix,
}: FrameworkFieldsProps) {
  return (
    <div className="grid grid-cols-1 items-end gap-3 sm:grid-cols-2 lg:grid-cols-4">
      <Input
        id={`${idPrefix}-heartbeat-timeout`}
        label="Heartbeat Timeout (s)"
        tooltip="Seconds without a heartbeat before it counts as a miss. Leave blank for the default (30). 0 = disable the heartbeat watchdog (for frameworks that don't send heartbeats)."
        type="number"
        min={0}
        placeholder="30"
        value={value.heartbeatTimeoutSeconds?.toString() ?? ""}
        onChange={(e) =>
          onChange({
            heartbeatTimeoutSeconds: toOptionalInt(e.target.value, 0),
          })
        }
      />
      <Input
        id={`${idPrefix}-missed-heartbeats`}
        label="Missed Heartbeats"
        tooltip="Consecutive missed heartbeats before the profile is restarted. Leave blank for the default (3); minimum 1."
        type="number"
        min={1}
        placeholder="3"
        value={value.maxMissedHeartbeats?.toString() ?? ""}
        onChange={(e) =>
          onChange({ maxMissedHeartbeats: toOptionalInt(e.target.value, 1) })
        }
      />
      <Input
        id={`${idPrefix}-crash-retries`}
        label="Crash Retries"
        tooltip="Restart attempts after a crash before giving up and disabling the schedule. Leave blank for the default (5). 0 = never restart after a crash."
        type="number"
        min={0}
        placeholder="5"
        value={value.maxCrashRetries?.toString() ?? ""}
        onChange={(e) =>
          onChange({ maxCrashRetries: toOptionalInt(e.target.value, 0) })
        }
      />
      <Input
        id={`${idPrefix}-unresponsive-timeout`}
        label="Unresponsive Timeout (s)"
        tooltip="Seconds the game window may be frozen / hung before restart, even if heartbeats still arrive. Leave blank for the default (30). 0 = disable."
        type="number"
        min={0}
        placeholder="30"
        value={value.unresponsiveTimeoutSeconds?.toString() ?? ""}
        onChange={(e) =>
          onChange({
            unresponsiveTimeoutSeconds: toOptionalInt(e.target.value, 0),
          })
        }
      />
    </div>
  );
}

/** Screenshot / crash-log retention in the framework's game directory. */
export function CleanupRetentionFields({
  value,
  onChange,
  idPrefix,
}: FrameworkFieldsProps) {
  return (
    <div className="grid grid-cols-1 items-end gap-3 sm:grid-cols-2">
      <Input
        id={`${idPrefix}-screenshot-retention`}
        label="Screenshot Retention (days)"
        tooltip="Auto-delete Screenshot*.jpg older than this many days. 0 = disabled."
        type="number"
        min={0}
        placeholder="0"
        value={(value.screenshotRetentionDays ?? 0).toString()}
        onChange={(e) =>
          onChange({ screenshotRetentionDays: toClampedInt(e.target.value, 0) })
        }
      />
      <Input
        id={`${idPrefix}-crash-retention`}
        label="Crash Log Retention (days)"
        tooltip="Auto-delete BlizzardError crash-dump folders older than this many days. 0 = disabled."
        type="number"
        min={0}
        placeholder="0"
        value={(value.crashLogRetentionDays ?? 0).toString()}
        onChange={(e) =>
          onChange({ crashLogRetentionDays: toClampedInt(e.target.value, 0) })
        }
      />
    </div>
  );
}
