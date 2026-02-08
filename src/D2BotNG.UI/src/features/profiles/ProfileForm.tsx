/**
 * ProfileForm component
 *
 * Form for creating/editing bot profiles with all configuration fields.
 */

import { useState, useCallback, useEffect, useMemo } from "react";
import {
  Button,
  Input,
  PathInput,
  PasswordInput,
  Select,
  Card,
  CardHeader,
  CardContent,
  CardFooter,
  PathSelectorDialog,
} from "@/components/ui";
import {
  useKeyLists,
  useSchedules,
  useProfiles,
  useSettings,
} from "@/stores/event-store";
import { fileClient } from "@/lib/grpc-client";
import { Realm, Difficulty, GameMode } from "@/generated/common_pb";
import type { Profile } from "@/generated/profiles_pb";
import type { ProfileInput } from "@/hooks/useProfiles";

interface ProfileFormProps {
  /** Existing profile for editing (undefined for new profile) */
  profile?: Profile;
  /** Initial values for new profile (e.g., when cloning) */
  initialValues?: Partial<Profile>;
  /** Called when form is submitted with profile data */
  onSubmit: (data: ProfileInput) => void;
  /** Called when user cancels */
  onCancel: () => void;
  /** Whether form submission is in progress */
  isLoading?: boolean;
}

// Realm options for select
const realmOptions = [
  { value: String(Realm.US_EAST), label: "US East" },
  { value: String(Realm.US_WEST), label: "US West" },
  { value: String(Realm.EUROPE), label: "Europe" },
  { value: String(Realm.ASIA), label: "Asia" },
];

// Difficulty options for select
const difficultyOptions = [
  { value: String(Difficulty.HIGHEST), label: "Highest" },
  { value: String(Difficulty.HIGHEST), label: "Hell" },
  { value: String(Difficulty.NIGHTMARE), label: "Nightmare" },
  { value: String(Difficulty.NORMAL), label: "Normal" },
];

// Game mode options for select
const modeOptions = [
  { value: String(GameMode.BATTLE_NET), label: "Battle.net" },
  { value: String(GameMode.OPEN_BATTLE_NET), label: "Open Battle.net" },
  { value: String(GameMode.SINGLE_PLAYER), label: "Single Player" },
  { value: String(GameMode.TCP_HOST), label: "TCP/IP Host" },
  { value: String(GameMode.TCP_JOIN), label: "TCP/IP Join" },
];

// Modes that require account/password
const battleNetModes = [GameMode.BATTLE_NET, GameMode.OPEN_BATTLE_NET];

export function ProfileForm({
  profile,
  initialValues,
  onSubmit,
  onCancel,
  isLoading = false,
}: ProfileFormProps) {
  // Use profile for editing, or initialValues for new profiles (e.g., cloning)
  const defaults = profile ?? initialValues;
  // Get key lists, schedules, existing profiles, and settings from event store
  const keyListsData = useKeyLists();
  const schedulesData = useSchedules();
  const profilesData = useProfiles();
  const settings = useSettings();

  // Build set of existing profile names for uniqueness validation
  const existingNames = useMemo(() => {
    return new Set(profilesData.map((p) => p.profile.name.toLowerCase()));
  }, [profilesData]);

  // Form state
  const [name, setName] = useState(defaults?.name ?? "");
  const [group, setGroup] = useState(defaults?.group ?? "");
  const [d2Path, setD2Path] = useState(defaults?.d2Path ?? "");
  const [account, setAccount] = useState(defaults?.account ?? "");
  const [password, setPassword] = useState(defaults?.password ?? "");
  const [character, setCharacter] = useState(defaults?.character ?? "");
  const [realm, setRealm] = useState(defaults?.realm ?? Realm.US_EAST);
  const [difficulty, setDifficulty] = useState(
    defaults?.difficulty ?? Difficulty.HIGHEST,
  );
  const [mode, setMode] = useState(defaults?.mode ?? GameMode.BATTLE_NET);
  const [gameName, setGameName] = useState(defaults?.gameName ?? "");
  const [gamePass, setGamePass] = useState(defaults?.gamePass ?? "");
  const [parameters, setParameters] = useState(
    defaults?.parameters ?? "-w -sleepy -ftj",
  );
  const [entryScript, setEntryScript] = useState(defaults?.entryScript ?? "");
  const [infoTag, setInfoTag] = useState(defaults?.infoTag ?? "");
  const [keyList, setKeyList] = useState(defaults?.keyList ?? "");
  const [schedule, setSchedule] = useState(defaults?.schedule ?? "");
  const [runsPerKey, setRunsPerKey] = useState(defaults?.runsPerKey ?? 0);
  const [switchKeysOnRestart, setSwitchKeysOnRestart] = useState(
    defaults?.switchKeysOnRestart ?? false,
  );
  const [visible, setVisible] = useState(defaults?.visible ?? true);
  const [windowX, setWindowX] = useState(
    defaults?.windowLocation?.x?.toString() ?? "",
  );
  const [windowY, setWindowY] = useState(
    defaults?.windowLocation?.y?.toString() ?? "",
  );
  const [scheduleEnabled, setScheduleEnabled] = useState(
    defaults?.scheduleEnabled ?? true,
  );

  // Track which fields have been touched (blurred)
  const [touched, setTouched] = useState<Record<string, boolean>>({});

  // Path selector dialog state
  const [showD2PathPicker, setShowD2PathPicker] = useState(false);

  // Entry script options loaded from FileService
  const [entryScriptOptions, setEntryScriptOptions] = useState<
    { value: string; label: string }[]
  >([]);

  // Load available entry scripts from d2bs/*bot/*.dbj
  useEffect(() => {
    async function loadEntryScripts() {
      const basePath = settings?.basePath;
      if (!basePath) return;

      try {
        // List directories under basePath/d2bs
        const d2bsPath = `${basePath}/d2bs`;
        const d2bsListing = await fileClient.listDirectory({ path: d2bsPath });

        // Find first directory matching *bot (alphabetically)
        const botDirs = d2bsListing.entries
          .filter((e) => e.isDirectory && e.name.toLowerCase().endsWith("bot"))
          .map((e) => e.name)
          .sort((a, b) => a.localeCompare(b));

        if (botDirs.length === 0) {
          setEntryScriptOptions([]);
          return;
        }

        const botDir = botDirs[0];
        const botPath = `${d2bsPath}/${botDir}`;

        // List *.dbj files in the bot directory
        const botListing = await fileClient.listDirectory({ path: botPath });
        const dbjFiles = botListing.entries
          .filter(
            (e) => !e.isDirectory && e.name.toLowerCase().endsWith(".dbj"),
          )
          .map((e) => e.name)
          .sort((a, b) => a.localeCompare(b));

        setEntryScriptOptions(
          dbjFiles.map((name) => ({ value: name, label: name })),
        );
      } catch (err) {
        console.error("Failed to load entry scripts:", err);
        setEntryScriptOptions([]);
      }
    }

    loadEntryScripts();
  }, [settings?.basePath]);

  const handleBlur = useCallback((field: string) => {
    setTouched((prev) => ({ ...prev, [field]: true }));
  }, []);

  // Update form when defaults change (profile or initialValues)
  useEffect(() => {
    if (defaults) {
      setName(defaults.name ?? "");
      setGroup(defaults.group ?? "");
      setD2Path(defaults.d2Path ?? "");
      setAccount(defaults.account ?? "");
      setPassword(defaults.password ?? "");
      setCharacter(defaults.character ?? "");
      setRealm(defaults.realm || Realm.US_EAST);
      setDifficulty(defaults.difficulty || Difficulty.HIGHEST);
      setMode(defaults.mode || GameMode.BATTLE_NET);
      setGameName(defaults.gameName ?? "");
      setGamePass(defaults.gamePass ?? "");
      setParameters(defaults.parameters ?? "-w -sleepy -ftj");
      setEntryScript(defaults.entryScript ?? "");
      setInfoTag(defaults.infoTag ?? "");
      setKeyList(defaults.keyList ?? "");
      setSchedule(defaults.schedule ?? "");
      setRunsPerKey(defaults.runsPerKey ?? 0);
      setSwitchKeysOnRestart(defaults.switchKeysOnRestart ?? false);
      setVisible(defaults.visible ?? true);
      setWindowX(defaults.windowLocation?.x?.toString() ?? "");
      setWindowY(defaults.windowLocation?.y?.toString() ?? "");
      setScheduleEnabled(defaults.scheduleEnabled ?? true);
    }
  }, [defaults]);

  // Build key list options (keyListsData contains { keyList, usage })
  const keyListOptions = [
    { value: "", label: "None" },
    ...keyListsData.map((kl) => ({
      value: kl.keyList.name,
      label: kl.keyList.name,
    })),
  ];

  // Build schedule options
  const scheduleOptions = [
    { value: "", label: "None" },
    ...schedulesData.map((s) => ({ value: s.name, label: s.name })),
  ];

  // Validation
  const requiresAccount = battleNetModes.includes(mode);

  // Check if name is a duplicate (for new profiles, or renames to an existing name)
  const trimmedNameLower = name.trim().toLowerCase();
  const isSameName = profile && profile.name.toLowerCase() === trimmedNameLower;
  const isDuplicateName =
    !isSameName && existingNames.has(trimmedNameLower);

  // Validation errors (only shown when field is touched)
  const errors = {
    name:
      touched.name && name.trim() === ""
        ? "Profile name is required"
        : touched.name && isDuplicateName
          ? "A profile with this name already exists"
          : undefined,
    d2Path:
      touched.d2Path && d2Path.trim() === ""
        ? "Diablo II path is required"
        : undefined,
    character:
      touched.character && character.trim() === ""
        ? "Character name is required"
        : undefined,
    entryScript:
      touched.entryScript && entryScript.trim() === ""
        ? "Entry script is required"
        : undefined,
    account:
      touched.account && requiresAccount && account.trim() === ""
        ? "Account is required for Battle.net"
        : undefined,
    password:
      touched.password && requiresAccount && password.trim() === ""
        ? "Password is required for Battle.net"
        : undefined,
  };

  const canSave =
    name.trim() !== "" &&
    !isDuplicateName &&
    d2Path.trim() !== "" &&
    character.trim() !== "" &&
    entryScript.trim() !== "" &&
    (!requiresAccount || (account.trim() !== "" && password.trim() !== ""));

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();

      // Mark all required fields as touched to show validation errors
      if (!canSave) {
        setTouched((prev) => ({
          ...prev,
          name: true,
          d2Path: true,
          character: true,
          entryScript: true,
          account: true,
          password: true,
        }));
        return;
      }

      const data: ProfileInput = {
        name,
        group,
        d2Path,
        account,
        password,
        character,
        realm,
        difficulty,
        mode,
        gameName,
        gamePass,
        parameters,
        entryScript,
        infoTag,
        keyList: keyList || undefined,
        schedule: schedule || undefined,
        runsPerKey,
        switchKeysOnRestart,
        visible,
        windowLocation:
          windowX && windowY
            ? { x: parseInt(windowX, 10), y: parseInt(windowY, 10) }
            : undefined,
        scheduleEnabled,
      };

      onSubmit(data);
    },
    [
      canSave,
      name,
      group,
      d2Path,
      account,
      password,
      character,
      realm,
      difficulty,
      mode,
      gameName,
      gamePass,
      parameters,
      entryScript,
      infoTag,
      keyList,
      schedule,
      runsPerKey,
      switchKeysOnRestart,
      visible,
      windowX,
      windowY,
      scheduleEnabled,
      onSubmit,
    ],
  );

  return (
    <form onSubmit={handleSubmit} noValidate>
      <div className="space-y-3">
        {/* Profile Settings - Basic, Account, Game */}
        <Card>
          <CardHeader title="Profile Settings" />
          <CardContent className="space-y-3">
            {/* Basic Info */}
            <div className="grid gap-2 sm:grid-cols-3">
              <Input
                id="group"
                label="Group"
                value={group}
                onChange={(e) => setGroup(e.target.value)}
                placeholder="e.g., Farming, Keys, Testing"
              />
              <Input
                id="name"
                label="Profile Name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                onBlur={() => handleBlur("name")}
                placeholder="My Bot Profile"
                error={errors.name}
              />
              <Input
                id="parameters"
                label="Parameters"
                value={parameters}
                onChange={(e) => setParameters(e.target.value)}
                placeholder="-w -sleepy -ftj"
              />
            </div>
            <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
              <div className="sm:col-span-2">
                <PathInput
                  id="d2Path"
                  label="Diablo II Path"
                  value={d2Path}
                  onChange={(e) => setD2Path(e.target.value)}
                  onBlur={() => handleBlur("d2Path")}
                  placeholder="C:\Games\Diablo II\Game.exe"
                  error={errors.d2Path}
                  onBrowse={() => setShowD2PathPicker(true)}
                />
              </div>
              <div className="grid grid-cols-2 gap-2">
                <Input
                  id="windowX"
                  label="Window X"
                  value={windowX}
                  onChange={(e) => setWindowX(e.target.value)}
                  placeholder="100"
                />
                <Input
                  id="windowY"
                  label="Window Y"
                  value={windowY}
                  onChange={(e) => setWindowY(e.target.value)}
                  placeholder="200"
                />
              </div>
              <div className="flex items-center gap-2 pt-6">
                <input
                  id="visible"
                  type="checkbox"
                  checked={visible}
                  onChange={(e) => setVisible(e.target.checked)}
                  className="h-4 w-4 rounded border-zinc-700 bg-zinc-800 text-d2-gold focus:ring-d2-gold"
                />
                <label htmlFor="visible" className="text-sm text-zinc-400">
                  Show window
                </label>
              </div>
            </div>

            {/* Account & Game - combined row */}
            <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
              <Input
                id="character"
                label="Character"
                value={character}
                onChange={(e) => setCharacter(e.target.value)}
                onBlur={() => handleBlur("character")}
                placeholder="Character name"
                error={errors.character}
              />
              <Select
                id="realm"
                label="Realm"
                value={String(realm)}
                onChange={(e) => setRealm(Number(e.target.value) as Realm)}
                options={realmOptions}
              />
              <Select
                id="mode"
                label="Game Mode"
                value={String(mode)}
                onChange={(e) => setMode(Number(e.target.value) as GameMode)}
                options={modeOptions}
              />
              <Select
                id="difficulty"
                label="Difficulty"
                value={String(difficulty)}
                onChange={(e) =>
                  setDifficulty(Number(e.target.value) as Difficulty)
                }
                options={difficultyOptions}
              />
              <Input
                id="gameName"
                label="Game Name"
                value={gameName}
                onChange={(e) => setGameName(e.target.value)}
                placeholder="Game name pattern"
              />
              <Input
                id="gamePass"
                label="Game Password"
                value={gamePass}
                onChange={(e) => setGamePass(e.target.value)}
                placeholder="Game password"
              />
              {requiresAccount && (
                <>
                  <Input
                    id="account"
                    label="Account"
                    value={account}
                    onChange={(e) => setAccount(e.target.value)}
                    onBlur={() => handleBlur("account")}
                    placeholder="Account name"
                    error={errors.account}
                    autoComplete="off"
                  />
                  <PasswordInput
                    id="password"
                    label="Password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    onBlur={() => handleBlur("password")}
                    placeholder="Account password"
                    error={errors.password}
                    autoComplete="new-password"
                  />
                </>
              )}
            </div>
          </CardContent>
        </Card>

        {/* Bot Configuration - Script, Keys, Schedule */}
        <Card>
          <CardHeader title="Bot Configuration" />
          <CardContent className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
            <Select
              id="entryScript"
              label="Entry Script"
              value={entryScript}
              onChange={(e) => setEntryScript(e.target.value)}
              onBlur={() => handleBlur("entryScript")}
              options={[
                { value: "", label: "Select a script..." },
                ...entryScriptOptions,
              ]}
              error={errors.entryScript}
            />
            <Input
              id="infoTag"
              label="Info Tag"
              value={infoTag}
              onChange={(e) => setInfoTag(e.target.value)}
              placeholder="Info tag for scripts"
            />
            <Select
              id="keyList"
              label="Key List"
              value={keyList}
              onChange={(e) => setKeyList(e.target.value)}
              options={keyListOptions}
            />
            <Input
              id="runsPerKey"
              label="Runs Per Key"
              type="number"
              value={runsPerKey}
              onChange={(e) => setRunsPerKey(Number(e.target.value))}
              min={0}
            />
            <Select
              id="schedule"
              label="Schedule"
              value={schedule}
              onChange={(e) => setSchedule(e.target.value)}
              options={scheduleOptions}
            />
            <div className="flex items-center gap-2 pt-6">
              <input
                id="scheduleEnabled"
                type="checkbox"
                checked={scheduleEnabled}
                onChange={(e) => setScheduleEnabled(e.target.checked)}
                className="h-4 w-4 rounded border-zinc-700 bg-zinc-800 text-d2-gold focus:ring-d2-gold"
              />
              <label
                htmlFor="scheduleEnabled"
                className="text-sm text-zinc-400"
              >
                Schedule enabled
              </label>
            </div>
            <div className="flex items-center gap-2 pt-6">
              <input
                id="switchKeysOnRestart"
                type="checkbox"
                checked={switchKeysOnRestart}
                onChange={(e) => setSwitchKeysOnRestart(e.target.checked)}
                className="h-4 w-4 rounded border-zinc-700 bg-zinc-800 text-d2-gold focus:ring-d2-gold"
              />
              <label
                htmlFor="switchKeysOnRestart"
                className="text-sm text-zinc-400"
              >
                Switch keys on restart
              </label>
            </div>
          </CardContent>
        </Card>

        {/* Form Actions */}
        <CardFooter className="flex justify-end gap-2 border-t border-zinc-800 pt-4">
          <Button
            type="button"
            variant="ghost"
            onClick={onCancel}
            disabled={isLoading}
          >
            Cancel
          </Button>
          <Button type="submit" disabled={isLoading}>
            {profile ? "Save Changes" : "Create Profile"}
          </Button>
        </CardFooter>
      </div>

      <PathSelectorDialog
        open={showD2PathPicker}
        onClose={() => setShowD2PathPicker(false)}
        onSelect={(path) => {
          setD2Path(path);
          setShowD2PathPicker(false);
        }}
        mode="file"
        title="Select Diablo II Executable"
        description="Looking for: Game.exe, Diablo II.exe"
        initialPath={
          d2Path
            ? d2Path.replace(/[/\\][^/\\]+$/, "")
            : settings?.game?.d2InstallPath || ""
        }
        filter={(entry) => /^(Game|Diablo II).*\.exe$/i.test(entry.name)}
      />
    </form>
  );
}
