/**
 * FrameworkForm component
 *
 * Full-page form for creating/editing a framework: the launch bundle (game/d2bs
 * paths, inject DLLs, version), health & crash recovery thresholds, per-install
 * cleanup retention, injected environment variables, and d2bs.ini usage.
 *
 * The fields shared with the basic-mode Settings "Game" card are rendered via
 * the FrameworkFields fragments, which own their labels/tooltips/clamping.
 */

import { useState, useEffect, useMemo, useCallback } from "react";
import {
  Button,
  Input,
  Card,
  CardHeader,
  CardContent,
  HelpTooltip,
  EnvVarsEditor,
  type EnvVar,
} from "@/components/ui";
import { PlusIcon, TrashIcon } from "@heroicons/react/24/outline";
import { useFrameworks } from "@/stores/event-store";
import type { FrameworkInput } from "@/hooks/useFrameworks";
import type { Framework } from "@/generated/frameworks_pb";
import {
  FrameworkPathFields,
  FrameworkVersionField,
  HealthThresholdFields,
  CleanupRetentionFields,
} from "./FrameworkFields";

interface FrameworkFormProps {
  /** Existing framework for editing (undefined for a new framework). */
  framework?: Framework;
  /** Initial values for a new framework (e.g. when cloning). */
  initialValues?: Partial<Framework>;
  onSubmit: (data: FrameworkInput) => void;
  onCancel: () => void;
  isLoading?: boolean;
}

export function FrameworkForm({
  framework,
  initialValues,
  onSubmit,
  onCancel,
  isLoading = false,
}: FrameworkFormProps) {
  const frameworksData = useFrameworks();

  // Editing an existing framework, or prefilling a new one (e.g. cloning).
  const source = framework ?? initialValues;

  const [name, setName] = useState("");
  // The fields shared with the basic-mode Game card, edited through the
  // FrameworkFields fragments. Absent optional thresholds stay undefined so
  // blank round-trips as "absent" (server default, shown as the placeholder).
  const [shared, setShared] = useState<FrameworkInput>({});
  const [dllPaths, setDllPaths] = useState<string[]>(["D2BS.dll"]);
  const [envVars, setEnvVars] = useState<EnvVar[]>([]);
  const [nameTouched, setNameTouched] = useState(false);

  useEffect(() => {
    setName(source?.name ?? "");
    setShared({
      // Carried, not edited: the submit below is a full replacement, so anything the
      // form drops is silently zeroed on the stored framework.
      gameType: source?.gameType,
      bottingFramework: source?.bottingFramework,
      gameDirectory: source?.gameDirectory ?? "",
      d2bsPath: source?.d2bsPath ?? "",
      gameVersion: source?.gameVersion ?? "",
      screenshotRetentionDays: source?.screenshotRetentionDays ?? 0,
      crashLogRetentionDays: source?.crashLogRetentionDays ?? 0,
      heartbeatTimeoutSeconds: source?.heartbeatTimeoutSeconds,
      maxMissedHeartbeats: source?.maxMissedHeartbeats,
      maxCrashRetries: source?.maxCrashRetries,
      unresponsiveTimeoutSeconds: source?.unresponsiveTimeoutSeconds,
    });
    setDllPaths(
      source?.dllPaths && source.dllPaths.length > 0
        ? [...source.dllPaths]
        : ["D2BS.dll"],
    );
    setEnvVars(
      Object.entries(source?.environment ?? {}).map(([key, value]) => ({
        key,
        value,
      })),
    );
    setNameTouched(false);
  }, [source]);

  // Spreading the MessageInit union widens $typeName; cast back (shared is
  // always a plain init object, never a Framework message instance).
  const handleSharedChange = useCallback(
    (partial: Partial<FrameworkInput>) =>
      setShared((prev) => ({ ...prev, ...partial }) as FrameworkInput),
    [],
  );

  // Name must be non-empty and unique (case-insensitive), ignoring the framework
  // being edited.
  const existingNames = useMemo(
    () => new Set(frameworksData.map((f) => f.framework.name.toLowerCase())),
    [frameworksData],
  );
  const trimmedNameLower = name.trim().toLowerCase();
  const isSameName = framework?.name.toLowerCase() === trimmedNameLower;
  const isDuplicateName = !isSameName && existingNames.has(trimmedNameLower);
  const nameError =
    name.trim() === ""
      ? "Framework name is required"
      : isDuplicateName
        ? "A framework with this name already exists"
        : undefined;

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      if (nameError) {
        setNameTouched(true);
        return;
      }

      const environment: Record<string, string> = {};
      for (const { key, value } of envVars) {
        const trimmedKey = key.trim();
        if (trimmedKey.length > 0) {
          environment[trimmedKey] = value;
        }
      }

      onSubmit({
        ...shared,
        name: name.trim(),
        gameDirectory: (shared.gameDirectory ?? "").trim(),
        d2bsPath: (shared.d2bsPath ?? "").trim(),
        gameVersion: (shared.gameVersion ?? "").trim(),
        dllPaths: dllPaths.map((d) => d.trim()).filter((d) => d.length > 0),
        environment,
      } as FrameworkInput);
    },
    [nameError, name, shared, dllPaths, envVars, onSubmit],
  );

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <Card>
        <CardHeader
          title="Framework"
          description="The botting framework to inject, and where the game is installed."
        />
        <CardContent className="space-y-3">
          <Input
            id="framework-name"
            label="Name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            onBlur={() => setNameTouched(true)}
            error={nameTouched ? nameError : undefined}
            placeholder="e.g., Kolbot 1.14d"
          />
          <FrameworkPathFields
            value={shared}
            onChange={handleSharedChange}
            idPrefix="framework"
          />

          {/* DLLs to inject */}
          <div className="w-full">
            <div className="mb-1.5 flex items-center gap-1.5">
              <span className="text-sm font-medium text-zinc-400">
                DLLs to Inject
              </span>
              <HelpTooltip text="Injected in order after the game starts. Relative paths resolve against the D2BS path; leave the default D2BS.dll unless you need extra DLLs." />
            </div>
            <div className="space-y-2">
              {dllPaths.map((dll, index) => (
                <div key={index} className="flex items-center gap-2">
                  <div className="flex-1">
                    <Input
                      id={`framework-dll-${index}`}
                      value={dll}
                      placeholder="D2BS.dll"
                      onChange={(e) =>
                        setDllPaths((prev) =>
                          prev.map((d, i) =>
                            i === index ? e.target.value : d,
                          ),
                        )
                      }
                    />
                  </div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    aria-label="Remove DLL"
                    disabled={dllPaths.length === 1}
                    onClick={() =>
                      setDllPaths((prev) => prev.filter((_, i) => i !== index))
                    }
                  >
                    <TrashIcon className="h-4 w-4" />
                  </Button>
                </div>
              ))}
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => setDllPaths((prev) => [...prev, ""])}
              >
                <PlusIcon className="h-4 w-4" />
                Add DLL
              </Button>
            </div>
          </div>

          <FrameworkVersionField
            value={shared}
            onChange={handleSharedChange}
            idPrefix="framework"
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader
          title="Health & Crash Recovery"
          description="How the manager monitors and recovers this framework's games. Set a timeout to 0 to disable that watchdog."
        />
        <CardContent>
          <HealthThresholdFields
            value={shared}
            onChange={handleSharedChange}
            idPrefix="framework"
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader
          title="Game Directory Cleanup"
          description="Auto-delete old files from this framework's game directory. 0 = disabled. Cleanup runs hourly."
        />
        <CardContent>
          <CleanupRetentionFields
            value={shared}
            onChange={handleSharedChange}
            idPrefix="framework"
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader
          title="Environment Variables"
          description="Extra variables set on the launched game process, merged over the manager's own environment."
        />
        <CardContent>
          <EnvVarsEditor
            value={envVars}
            onChange={setEnvVars}
            idPrefix="framework-env"
          />
        </CardContent>
      </Card>

      <div className="flex justify-end gap-2">
        <Button
          type="button"
          variant="ghost"
          onClick={onCancel}
          disabled={isLoading}
        >
          Cancel
        </Button>
        <Button type="submit" disabled={isLoading}>
          {framework ? "Save Changes" : "Create Framework"}
        </Button>
      </div>
    </form>
  );
}
