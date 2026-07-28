/**
 * ProfileForm component
 *
 * Form for creating/editing bot profiles with all configuration fields.
 */

import {
  useState,
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
} from "react";
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
  EnvVarsEditor,
  type EnvVar,
} from "@/components/ui";
import {
  DiscordWebhooksList,
  type DiscordWebhookInput,
} from "@/components/discord/DiscordWebhooksList";
import {
  useKeyLists,
  useProxies,
  useFrameworks,
  useSchedules,
  useProfiles,
} from "@/stores/event-store";
import { useEntryScripts } from "@/hooks";
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
  { value: String(Difficulty.HELL), label: "Hell" },
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

export function ProfileForm({
  profile,
  initialValues,
  onSubmit,
  onCancel,
  isLoading = false,
}: ProfileFormProps) {
  // Use profile for editing, or initialValues for new profiles (e.g., cloning)
  const defaults = profile ?? initialValues;
  // Get key lists, schedules, and existing profiles from event store
  const keyListsData = useKeyLists();
  const proxiesData = useProxies();
  const frameworksData = useFrameworks();
  const schedulesData = useSchedules();
  const profilesData = useProfiles();

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
  const [proxy, setProxy] = useState(defaults?.proxy ?? "");
  const [framework, setFramework] = useState(defaults?.framework ?? "");
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
  const [discordWebhooks, setDiscordWebhooks] = useState<DiscordWebhookInput[]>(
    () =>
      (defaults?.discordWebhooks ?? []).map((w) => ({
        url: w.url,
        postItems: w.postItems,
        postConsole: w.postConsole,
        postAnnounce: w.postAnnounce,
      })),
  );
  const [envVars, setEnvVars] = useState<EnvVar[]>(() =>
    Object.entries(defaults?.environment ?? {}).map(([key, value]) => ({
      key,
      value,
    })),
  );

  // Track which fields have been touched (blurred)
  const [touched, setTouched] = useState<Record<string, boolean>>({});

  // Path selector dialog state
  const [showD2PathPicker, setShowD2PathPicker] = useState(false);

  // Entry scripts come from the selected framework's D2BS directory.
  const selectedFramework = frameworksData.find(
    (f) => f.framework.name === framework,
  )?.framework;
  const entryScriptOptions = useEntryScripts(selectedFramework?.d2bsPath);

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
      setProxy(defaults.proxy ?? "");
      setFramework(defaults.framework ?? "");
      setSchedule(defaults.schedule ?? "");
      setRunsPerKey(defaults.runsPerKey ?? 0);
      setSwitchKeysOnRestart(defaults.switchKeysOnRestart ?? false);
      setVisible(defaults.visible ?? true);
      setWindowX(defaults.windowLocation?.x?.toString() ?? "");
      setWindowY(defaults.windowLocation?.y?.toString() ?? "");
      setScheduleEnabled(defaults.scheduleEnabled ?? true);
      setDiscordWebhooks(
        (defaults.discordWebhooks ?? []).map((w) => ({
          url: w.url,
          postItems: w.postItems,
          postConsole: w.postConsole,
          postAnnounce: w.postAnnounce,
        })),
      );
      setEnvVars(
        Object.entries(defaults.environment ?? {}).map(([key, value]) => ({
          key,
          value,
        })),
      );
    }
  }, [defaults]);

  // Give a NEW profile a framework so the common single-framework case needs no
  // choice. An existing profile with none had it cleared by a framework delete and
  // must be reassigned deliberately — the backend refuses to rebind those too, and
  // the picker below is forced visible for exactly that case.
  //
  // Layout effect, not effect: an empty framework makes the picker visible (via the
  // blank option), so assigning after paint would flash the picker and shift the
  // grid on every New Profile.
  useLayoutEffect(() => {
    if (framework || profile) return;
    // Wait for the frameworks snapshot before auto-assigning, so we never commit a
    // name before data has loaded.
    if (!frameworksData.length) return;
    setFramework(frameworksData[0].framework.name);
  }, [framework, profile, frameworksData]);

  // Build key list options (keyListsData contains { keyList, usage })
  const keyListOptions = [
    { value: "", label: "None" },
    ...keyListsData.map((kl) => ({
      value: kl.keyList.name,
      label: kl.keyList.name,
    })),
  ];

  // Build proxy options (value and label are both the full address)
  const proxyOptions = [
    { value: "", label: "None" },
    ...proxiesData.map((p) => ({
      value: p.proxy.address,
      label: p.proxy.address,
    })),
  ];
  if (proxy && !proxiesData.some((p) => p.proxy.address === proxy)) {
    proxyOptions.push({ value: proxy, label: proxy });
  }

  // Build framework options (value and label are both the framework name). No "None"
  // option: a profile always launches via a framework.
  const frameworkOptions = [
    ...frameworksData.map((f) => ({
      value: f.framework.name,
      label: f.framework.name,
    })),
  ];
  if (
    framework &&
    !frameworksData.some((f) => f.framework.name === framework)
  ) {
    frameworkOptions.push({ value: framework, label: framework });
  }
  if (!framework) {
    // A profile left without a framework (by a framework delete) needs an option
    // matching its empty value. Without one the browser displays the first framework
    // while state stays "", and re-picking that framework fires no change event —
    // leaving the profile permanently unsaveable behind a "required" error.
    frameworkOptions.unshift({ value: "", label: "Select a framework…" });
  }

  // Only worth showing when there is something to decide (the blank placeholder above
  // makes this true whenever a profile is missing its framework).
  const showFrameworkPicker = frameworkOptions.length > 1;

  // Build schedule options
  const scheduleOptions = [
    { value: "", label: "None" },
    ...schedulesData.map((s) => ({ value: s.name, label: s.name })),
  ];

  // Check if name is a duplicate (for new profiles, or renames to an existing name)
  const trimmedNameLower = name.trim().toLowerCase();
  const isSameName = profile && profile.name.toLowerCase() === trimmedNameLower;
  const isDuplicateName = !isSameName && existingNames.has(trimmedNameLower);

  // Validation errors (only shown when field is touched). Account, password and
  // character are intentionally optional — some automation workflows don't use
  // them — so only the structural fields needed to launch are required.
  const errors = {
    name:
      touched.name && name.trim() === ""
        ? "Profile name is required"
        : touched.name && isDuplicateName
          ? "A profile with this name already exists"
          : undefined,
    // The Diablo II Path is the executable this profile launches, so it's required.
    d2Path:
      touched.d2Path && d2Path.trim() === ""
        ? "Diablo II path is required"
        : undefined,
    // Only surfaced when the dropdown is rendered; otherwise the framework is
    // auto-assigned once the frameworks snapshot arrives.
    framework:
      showFrameworkPicker && touched.framework && framework.trim() === ""
        ? "Framework is required"
        : undefined,
    entryScript:
      touched.entryScript && entryScript.trim() === ""
        ? "Entry script is required"
        : undefined,
  };

  // Surfaced near the submit button when the framework dropdown isn't rendered:
  // without this, a blocked save would be a silent no-op.
  const hiddenPickerFrameworkError =
    !showFrameworkPicker &&
    framework.trim() === "" &&
    frameworksData.length === 0
      ? "No frameworks are available. Create one on the Frameworks tab, or restart D2BotNG to recreate the Default framework."
      : undefined;

  // Always required: a save racing the frameworks snapshot must not persist an
  // empty framework (the profile couldn't launch).
  const canSave =
    name.trim() !== "" &&
    !isDuplicateName &&
    d2Path.trim() !== "" &&
    framework.trim() !== "" &&
    entryScript.trim() !== "" &&
    discordWebhooks.every((w) => w.url.trim() !== "");

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();

      // Mark all required fields as touched to show validation errors
      if (!canSave) {
        setTouched((prev) => ({
          ...prev,
          name: true,
          d2Path: true,
          framework: true,
          entryScript: true,
          webhooks: true,
        }));
        return;
      }

      const environment: Record<string, string> = {};
      for (const { key, value } of envVars) {
        const trimmedKey = key.trim();
        if (trimmedKey.length > 0) {
          environment[trimmedKey] = value;
        }
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
        proxy: proxy || undefined,
        framework,
        schedule: schedule || undefined,
        runsPerKey,
        switchKeysOnRestart,
        visible,
        windowLocation:
          windowX && windowY
            ? { x: parseInt(windowX, 10), y: parseInt(windowY, 10) }
            : undefined,
        scheduleEnabled,
        discordWebhooks,
        environment,
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
      proxy,
      framework,
      schedule,
      runsPerKey,
      switchKeysOnRestart,
      visible,
      windowX,
      windowY,
      scheduleEnabled,
      discordWebhooks,
      envVars,
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
                id="name"
                label="Profile Name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                onBlur={() => handleBlur("name")}
                placeholder="My Bot Profile"
                error={errors.name}
              />
              <Input
                id="group"
                label="Group"
                value={group}
                onChange={(e) => setGroup(e.target.value)}
                placeholder="e.g., Farming, Keys, Testing"
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
                  tooltip="The game executable this profile launches (e.g. Game.exe)."
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

            {/* Connection & account */}
            <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
              <Select
                id="mode"
                label="Game Mode"
                value={String(mode)}
                onChange={(e) => setMode(Number(e.target.value) as GameMode)}
                options={modeOptions}
              />
              <Input
                id="account"
                label="Account"
                value={account}
                onChange={(e) => setAccount(e.target.value)}
                placeholder="Account name"
                autoComplete="off"
              />
              <PasswordInput
                id="password"
                label="Password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Account password"
                autoComplete="new-password"
              />
              <Input
                id="character"
                label="Character"
                value={character}
                onChange={(e) => setCharacter(e.target.value)}
                placeholder="Character name"
              />
              <Select
                id="realm"
                label="Realm"
                value={String(realm)}
                onChange={(e) => setRealm(Number(e.target.value) as Realm)}
                options={realmOptions}
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
            <Select
              id="keyList"
              label="Key List"
              value={keyList}
              onChange={(e) => setKeyList(e.target.value)}
              options={keyListOptions}
            />
            <Select
              id="proxy"
              label="Proxy"
              value={proxy}
              onChange={(e) => setProxy(e.target.value)}
              options={proxyOptions}
            />
            {showFrameworkPicker && (
              <Select
                id="framework"
                label="Framework"
                value={framework}
                onChange={(e) => setFramework(e.target.value)}
                onBlur={() => handleBlur("framework")}
                options={frameworkOptions}
                error={errors.framework}
              />
            )}
            <Input
              id="runsPerKey"
              label="Runs Per Key"
              type="number"
              value={runsPerKey}
              onChange={(e) => setRunsPerKey(Number(e.target.value))}
              min={0}
            />
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
            <Input
              id="infoTag"
              label="Info Tag"
              value={infoTag}
              onChange={(e) => setInfoTag(e.target.value)}
              placeholder="Info tag for scripts"
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader
            title="Environment Variables"
            description="Extra environment variables for this profile's game process, merged over the framework's."
          />
          <CardContent>
            <EnvVarsEditor
              value={envVars}
              onChange={setEnvVars}
              idPrefix="profile-env"
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader
            title="Discord Webhooks"
            description="Different per profile. For global rules, see Settings - Discord."
          />
          <CardContent>
            <DiscordWebhooksList
              webhooks={discordWebhooks}
              onChange={setDiscordWebhooks}
              idPrefix="profile-webhook"
              errors={discordWebhooks.map((w) =>
                touched.webhooks && w.url.trim() === ""
                  ? "Webhook URL is required"
                  : undefined,
              )}
              onUrlBlur={() =>
                setTouched((prev) => ({ ...prev, webhooks: true }))
              }
            />
          </CardContent>
        </Card>

        {/* Form Actions */}
        <CardFooter className="flex items-center justify-end gap-2 border-t border-zinc-800 pt-4">
          {hiddenPickerFrameworkError && (
            <span className="mr-auto text-sm text-red-400">
              {hiddenPickerFrameworkError}
            </span>
          )}
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
            : (selectedFramework?.gameDirectory ?? "")
        }
        filter={(entry) => /^(Game|Diablo II).*\.exe$/i.test(entry.name)}
      />
    </form>
  );
}
