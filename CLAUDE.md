# D2BotNG

Modern Diablo II bot manager. Manages D2 game instances, handles CD key rotation, communicates with D2BS scripts via WM_COPYDATA, provides a web UI.

## Stack

| Layer | Tech |
|-------|------|
| Backend | .NET 10, C# 13, ASP.NET Core, gRPC, Serilog |
| Frontend | React 18, TypeScript, Vite 6, Tailwind CSS, HeadlessUI |
| State | Zustand (events), TanStack React Query (mutations) |
| gRPC | `@connectrpc/connect` + `@connectrpc/connect-web` |
| Windows | P/Invoke, WebView2, WM_COPYDATA IPC |

## Layout

```
protos/                  # Protobuf definitions (source of truth for all services)
src/
  D2BotNG/               # .NET backend (x64 Windows)
    Services/            # gRPC implementations (*ServiceImpl.cs)
    Engine/              # Profile lifecycle (ProfileEngine), scheduling (ScheduleEngine)
    Windows/             # Win32 interop: GameLauncher, ProcessManager, Patcher, MessageWindow
    Data/                # Protobuf JSON persistence (FileRepository pattern, data/ng/)
    Legacy/
      Api/               # Legacy D2Bot# HTTP API compatibility layer
      Models/            # Legacy JSONL models + migration
    Rendering/           # DC6 sprite decoding, palette management, item rendering
    Logging/             # Serilog extensions, TrackingLoggerFactory, LoggerRegistry, MessageServiceSink
    UI/                  # WinForms MainForm, WebView2 host, system tray
  D2BotNG.UI/            # React frontend
    src/
      features/          # Page components (profiles, keys, schedules, characters, items, settings)
      components/
        layout/          # Layout, Sidebar, Header, ConsolePanel
        discord/         # DiscordWebhooksList (shared between profile editor and settings)
        ui/              # Reusable UI library (Button, Card, Dialog, Table, Toast, etc.)
      stores/            # Zustand (event-store, toast-store)
      hooks/             # React Query mutations + useEventStream + useEntryScripts
      lib/               # gRPC client, auth, DC6 rendering pipeline
        rendering/       # dc6Decoder, paletteManager, itemRenderer, colors
      generated/         # Auto-generated protobuf types (buf generate)
tests/
  D2BotNG.Tests/         # xUnit; contract/invariant tests (see Notes)
Resources/               # DC6 sprites, palettes (pal.dat)
docs/plans/              # Design docs and implementation plans
reference/               # Reference D2Bot implementation for parity
```

## Commands

```bash
# Backend
cd src/D2BotNG
dotnet build                       # Build (also builds UI via MSBuild target)
dotnet build -p:SkipUIBuild=true   # Build backend only (skip npm build)
dotnet test ../../tests/D2BotNG.Tests/D2BotNG.Tests.csproj -p:SkipUIBuild=true   # Run tests
dotnet build -p:RunFormat=true     # Run dotnet format before build
dotnet build -p:RunInspect=true    # Run ReSharper inspect after build (review src/D2BotNG/obj/inspect.sarif)
dotnet run -- --dev-ui             # Dev mode (proxy to Vite at :4200)
dotnet run -- --headless           # Server only (no GUI window)

# Frontend
cd src/D2BotNG.UI
npm install
npm run dev                        # Vite dev server on port 4200
npm run build                      # Production build to ../D2BotNG/wwwroot
npm run lint                       # ESLint
npm run format                     # Prettier
npm run generate-grpc              # Regenerate protobuf types from protos/

# Publish (single exe)
cd src/D2BotNG
dotnet publish -c Release --self-contained       # Bundles .NET runtime (~60-80MB)
dotnet publish -c Release --no-self-contained    # Requires .NET 10 runtime (~15-25MB)
# Output: bin/Release/net10.0-windows/win-x64/publish/D2BotNG.exe
```

## gRPC Services

All defined in `protos/*.proto`, implemented in `src/D2BotNG/Services/*ServiceImpl.cs`:

| Service | Proto | Methods |
|---------|-------|---------|
| **ProfileService** | profiles.proto | CRUD, Start/Stop, ShowWindow/HideWindow, ResetStats, RotateKey, ReleaseKey, EnableSchedule/DisableSchedule, Reorder, TriggerMule |
| **KeyService** | keys.proto | CreateKeyList, UpdateKeyList, DeleteKeyList, HoldKey, ReleaseHeldKey |
| **FrameworkService** | frameworks.proto | CreateFramework, UpdateFramework, DeleteFramework |
| **ScheduleService** | schedules.proto | Create, Update, Delete |
| **EventService** | events.proto | StreamEvents (server stream), ClearMessages |
| **SettingsService** | settings.proto | Update, TestDiscord |
| **FileService** | settings.proto | ListDirectory (file browser for path selection) |
| **ItemService** | items.proto | ListEntities, Search |
| **UpdateService** | updates.proto | CheckForUpdate, StartUpdate |
| **LoggingService** | logging.proto | SetLogLevel, GetLogLevels |

## Event Architecture

Frontend uses a single gRPC server-stream for all real-time state:

1. `useEventStream` hook connects to `EventService.StreamEvents()`
2. Server sends initial snapshots (server info first, then profiles, key lists, proxies, frameworks, characters, schedules, settings, update status, log levels, console history)
3. Server streams incremental changes (ProfileState, Message, Settings, etc.)
4. Zustand `event-store` processes events and updates state maps. Snapshot handlers preserve object identity for content-equal messages (usage snapshots re-broadcast on every profile start/stop; identity-keyed form-seeding effects must not reset)
5. Mutations (create/update/delete) return `Empty` - UI updates arrive via the stream
6. Auto-reconnect on disconnect with 5s retry

**Event types:** ProfilesSnapshot, KeyListsSnapshot, ProxiesSnapshot, FrameworksSnapshot, SchedulesSnapshot, CharactersSnapshot, ProfileState, CharacterState, Message, Settings, UpdateStatus, EntitiesChanged, LogLevelsSnapshot, ServerInfo

**ServerInfo** carries facts fixed when the server was compiled — the running `version` and `analytics_available` (whether an Aptabase key was baked in; the UI hides the Usage Statistics opt-out when it wasn't). Sent once per connection, and first, so nothing the UI gates on renders before it. They don't belong in `Settings` (user configuration, and persisted — a build fact would go stale on disk), and the version isn't on `UpdateStatus` because the About dialog wants it without waiting for an update check.

## Backend Architecture

### Engine Layer
- **ProfileEngine** (`Engine/ProfileEngine.cs`) - Core orchestrator. State machine (Stopped -> Starting -> Running -> Stopping -> Stopped, Error -> Starting/Stopping/Stopped), process monitoring, crash recovery + heartbeat/unresponsive watchdogs (per-framework thresholds; defaults 5 retries, 30s heartbeat timeout, 3 missed = kill, 30s unresponsive; timeout 0 = watchdog off), key rotation
- **ProfileInstance** (`Engine/ProfileInstance.cs`) - Thread-safe state holder per profile with SemaphoreSlim
- **ScheduleEngine** (`Engine/ScheduleEngine.cs`) - Checks schedules every 60s, supports overnight ranges (22:00-06:00)
- **EngineHostedService** - IHostedService that initializes both engines

### Windows Layer
- **GameLauncher** (`Windows/GameLauncher.cs`) - 12-step launch pipeline: clear cache, build CLI args, create suspended process, patch memory, resume, inject the framework's DLL(s), set title
- **ProcessManager** (`Windows/ProcessManager.cs`) - DLL injection via LoadLibraryW remote thread, process creation, graceful shutdown (WM_CLOSE + force kill), job object for auto-killing child processes on crash
- **Patcher** (`Windows/Patcher.cs`) - Binary memory patches via VirtualProtectEx + WriteProcessMemory
- **RemoteModule** (`Windows/RemoteModule.cs`) - Resolves a target's kernel32 `LoadLibrary` address for cross-bitness injection (x64 manager → 32-bit game) and reads target module bases, via a PE export walk over ReadProcessMemory
- **MessageWindow** (`Windows/MessageWindow.cs`) - WM_COPYDATA receiver, parses JSON from D2BS, queues to Channel<D2BSMessage>
- **DaclOverwriter** - Changes DACL for elevated process access

### Data Layer
- **FileRepository<TItem, TList>** - Generic protobuf JSON file-backed repo using `JsonFormatter`/`JsonParser`. Stores data in `data/ng/` as single JSON documents (list-wrapper messages from `storage.proto`). Durable atomic saves via `Utilities/AtomicFile` (write `.tmp`, flush to disk, rename); unparseable files are quarantined to `.corrupt` and the repo starts empty. Reads and writes both take the SemaphoreSlim; use `MutateAllAsync()` for read-modify-write (GetAll → modify → ReplaceAll loses concurrent writes). Supports `ReloadAsync()` for base path changes. Every save is gated on `DataWriteGate`, so a predecessor mid-handoff writes nothing. **Versioned migration:** a repo opts in by overriding `SchemaVersion` (+ `MigrateAsync`) — on load a behind-version file is upgraded before parsing, persisted immediately (so it is one-time), and stamped on every save. `schema_version` is stamped *centrally* by looking the field up on the container descriptor, so there is no per-repo boilerplate; every storage container declares the field, and `tests/D2BotNG.Tests` asserts that for each repository so a future opt-in can't fail at a user's migration. Before anything is transformed the original file is copied to `<name>.v<n>.bak` — written once, never overwritten (the migration re-runs after a failed persist), never read back, and never auto-deleted.
- **ProfileRepository** - Extends `FileRepository<Profile, ProfileCollection>`, writes each framework's d2bs.ini via IniWriter inside `SaveAsync` (under the repo lock, ordering ini writes with profile saves); `RewriteInisAsync()` for framework-side callers
- **KeyListRepository** - Extends `FileRepository<KeyList, KeyListCollection>`, round-robin key selection, in-use/held state tracking (transient, not persisted)
- **FrameworkRepository** - Extends `FileRepository<Framework, FrameworkCollection>`. A framework bundles `game_directory`, `d2bs_path`, `dll_paths`, `game_version`; profiles reference one by name (`Profile.framework`) and supply the launched executable via `Profile.d2_path`. `FrameworkPaths` resolves the DLL/ini/mules paths from a framework.
- **FrameworkBootstrap** - Idempotent migration: ensures a `Default` framework exists and assigns it to any profile with no framework. Seeds the Default from the pre-frameworks config — `game_directory` from the old install-path setting (else the directory most profiles' `d2_path` live in, else the registry) with `d2bs_path` = `<base>/d2bs`, and game version + retention + health thresholds from `SettingsRepository.LegacySettings` (recovered by `SettingsMigrator`, since those keys were dropped from the `Settings` schema). Adopts framework-less profiles whenever there is nothing to choose — no frameworks yet (first-run migration) or exactly one — since the assignment it would make is the only one the user could make by hand. With two or more it declines and logs a warning naming the profiles: an empty `Profile.framework` there is the deliberate post-delete state and guessing could launch against the wrong game directory. The one-framework case is not a nicety: basic mode renders no framework control at all (nav hidden, route redirected, dropdown gated on `advanced_mode`), so an orphaned profile refused to start with no UI to repair it — `ProfileForm` now also forces the dropdown visible when the saved profile has no framework, in either mode. Runs at startup and on base-path change.
- **ItemRepository** - In-memory dictionary; aggregates and watches every framework's `kolbot/mules/`. `RefreshAsync()` rebuilds watchers when frameworks change.
- **SettingsRepository** - Singleton, protobuf JSON in `d2botng.json` next to the exe. On load, when the file's `schema_version` is behind, recovers pre-frameworks values into `LegacySettings` via `SettingsMigrator` but deliberately does NOT rewrite the file — leaving it at the old version keeps those values recoverable if startup fails before the framework migration completes; the file upgrades on the next save. A corrupt file is quarantined to `.corrupt` and the app boots with defaults. Stamps `schema_version` on every save
- **SettingsMigrator** (`Data/SettingsMigrator.cs`) - Versioned migration for `d2botng.json` (`schema_version`, absent = 0), applied on load up to `CurrentVersion`. Each breaking change archives the old settings shape as a **backend-only proto** in `src/D2BotNG/Legacy/Protos/` (kept out of `protos/`, so it's excluded from the frontend's buf generation) and parses the old file into it — typed and field-tolerant, not raw-JSON poking. v0→v1 recovers the removed `game`/`engine` values, exposed as `SettingsRepository.LegacySettings` for the framework migration. Only the settings file is versioned — the `repeated`-wrapper list files have no place for a version, so their one-off migrations stay in bootstraps
- **DataWriteGate** (`Data/DataWriteGate.cs`) - Process-wide switch that stops this instance persisting anything (every `FileRepository` save, the d2bs.ini writes, and `SettingsRepository`). Closed by `HandoffManager` *before* it spawns the successor, because the successor signals Adopted at the top of `Main` and only then runs `Migration`/`FrameworkBootstrap` — the predecessor is alive and message-driven throughout. A save rewrites its whole file from an in-memory list the OLD schema parsed, so one run counter arriving in that window silently drops every field the successor just added (this is how an update to the frameworks release could leave every profile with no `framework`, which then never self-healed because the successor's frameworks.json survived). Reopened only when no successor can still be migrating: it exited without signalling, or was never started. A successor that is alive but silent leaves the gate closed — read-only beats two writers on one directory.
- **Paths** (`Data/Paths.cs`) - Reactive path resolver, subscribes to `SettingsChanged` event. Exposes BasePath, DataDirectory, LegacyDataDirectory (d2bs/mules paths are per-framework now, via `FrameworkPaths`)
- **ScheduleRepository**, **PatchRepository** - Standard FileRepository implementations
- **Migration** (`Legacy/Models/Migration.cs`) - Static one-time migration from legacy JSONL files (`data/`) to modern protobuf JSON (`data/ng/`). Runs on startup and on base path change. Skips IRC profiles. Separate `MigrateLegacyApi` migrates `server.json` → `LegacyApiSettings`.

### Services Layer
- **EventBroadcaster** - Per-client Channel<Event> (unbounded), pub-sub for gRPC streaming
- **D2BSMessageHandler** - Background service processing WM_COPYDATA messages: heartbeat, updateStatus, printToConsole, printToItemLog, saveItem, postToIRC, uploadItem, rotateKey, etc.
- **MessageService** - Circular buffer of 10k console messages, thread-safe via `Lock`
- **AuthInterceptor** - gRPC interceptor checking `x-auth-password` header
- **DiscordService** - Discord.Net BackgroundService with slash commands (/list, /status, /start, /stop, /restart, /mule, /schedule, /identify), rich embeds, per-user auth, auto-reconnect on settings change
- **DiscordWebhookService** - Posts profile messages and item PNGs to per-profile and global Discord webhooks; fire-and-forget
- **UpdateManager** / **UpdateCheckBackgroundService** - Version checking and download management. `UpdateManager.AppVersion` is the running version, static so `EventServiceImpl` can put it on `ServerInfo` without an update check
- **AnalyticsService** (`Services/Analytics/`) - Anonymous usage reporting to Aptabase: a `session_start` install-shape snapshot ~15s after startup (inventory counts, per-feature adoption counts — `profilesWith*`/`frameworksWith*`, so "12 profiles, 1 proxied" is distinguishable from "12 profiles, all proxied" — install-level feature tags, game versions, build variant, hardware, Wine) and a 12-hourly `heartbeat` (profiles running, uptime). Only counts, booleans and environment facts — never a name, path, key or address. The app key is baked in at build time from the `APTABASE_APP_KEY` secret (`-p:AptabaseAppKey=`, surfaced as assembly metadata exactly like `BuildVariant`); **an absent key disables analytics**, so local and fork builds report nothing and the key is never committed. `Settings.analytics_disabled` (Settings → General → **Usage Statistics**) is the opt-out; it is re-read per send and cached into `ProfileEngine` off `SettingsChanged`, so toggling it applies immediately — no restart — to both the manager and the next game launched, which gets `-noanalytics` passed through. Phrased as *disabled* so absent/false means on; an `enabled` field would opt every existing install out on upgrade. `D2BOTNG_ANALYTICS_HOST` overrides the ingest host (self-hosted/dev keys). `InstallId` derives the install identifier as `sha256(salt|MachineGuid|volumeSerial|computerName)` in lowercase hex — **byte-identical to d2bsng's `DeriveInstallId`** so a machine's manager and its injected DLLs join to one install; nothing is stored, and the shared salt is what makes the two agree. `InstallIdTests` pins that format, since the two implementations are linked by nothing but convention
- **ErrorDialogWatcher** - Monitors for game error dialogs
- **DataCache** - Transient key-value store for D2BS data retrieve/store
- **IniWriter** - Rewrites each framework's d2bs.ini, writing only the profiles assigned to that framework
- **LoggerRegistry** - `ILogEventFilter` with per-category log level control, exposed via LoggingService gRPC
- **TrackingLoggerFactory** - Wraps `ILoggerFactory` to register all `D2BotNG.*` logger categories in LoggerRegistry

### Legacy API Layer (`Legacy/Api/`)
Backward-compatible HTTP API for legacy D2Bot# tools (e.g., Limedrop, D2BS scripts):
- **LegacyApiMiddleware** - ASP.NET middleware intercepting base64-encoded POST requests (skips gRPC-Web)
- **LegacyApiHandler** - Handles all legacy functions: challenge, validate, profiles, start, stop, store/retrieve/delete, query, get/put, emit, settag, gameaction, etc.
- **SessionManager** - Per-client AES session key management (keyed by IP + user agent)
- **GameActionScheduler** - IHostedService that queues game actions (start/stop/mule) for deferred execution based on profile state changes
- **NotificationQueue** - Per-user notification queuing for legacy polling clients
- **WebhookService** - Outbound HTTP webhooks for setTag/emit events
- **AesEncryption** - AES-128 CBC encryption for legacy session auth

## Frontend Architecture

### State Management
- **Zustand event-store** - Central state: profiles (`Map<name, ProfileWithStatusData>`), keyLists, schedules, messages (10k cap), settings, items, logLevels, connection status
- **Zustand toast-store** - Toast notifications with auto-dismiss
- **React Query** - Mutations only (no queries). All data comes through the event stream
- **Selector hooks** - `useProfiles()`, `useKeyLists()`, `useSchedules()`, `useSettings()`, `useMessages(source)`, etc. using `useShallow`

### Routing (`App.tsx`)
```
/ -> redirect to /profiles
/profiles           ProfilesPage (table with bulk actions, drag-and-drop reorder)
/profiles/new       ProfileDetailPage (create)
/profiles/:id       ProfileDetailPage (edit, clone via ?clone query param)
/keys               KeysPage (key list CRUD, hold/release, usage tracking)
/frameworks         FrameworksPage (list + usage; delete)
/frameworks/new     FrameworkDetailPage (create)
/frameworks/:id     FrameworkDetailPage (edit — game/d2bs/dll/version, health, cleanup, env vars)
/schedules          SchedulesPage (schedule CRUD, time period management)
/characters         CharactersPage (entity tree, item search, virtual list)
/settings           SettingsPage (tabs: General, Discord, Legacy API, Logging)
```

### Key Patterns
- Feature-based folder structure under `src/features/`
- Reusable UI component library in `src/components/ui/`
- `features/frameworks/FrameworkFields.tsx` fragments render the shared framework inputs for both FrameworkForm and the basic-mode Settings "Game" card (blank optional thresholds round-trip as proto-absent = server default)
- gRPC clients in `src/lib/grpc-client.ts` with auth interceptor
- DC6 sprite rendering pipeline in `src/lib/rendering/`
- Drag-and-drop via `@dnd-kit` for profile reordering
- D2 color codes (ÿc0-ÿc<) parsed in console output

## Data Files

App settings in `d2botng.json` next to the exe (protobuf `Settings` - start_minimized, close_action, server, Discord, display, legacy_api, base_path, startup, window geometry, analytics_disabled, schema_version). Versioned via `SettingsMigrator` (game/engine settings moved to frameworks).

Bot data in `data/ng/` directory (protobuf JSON format, location determined by `BasePath` in settings):

| File | Content |
|------|---------|
| `profiles.json` | Bot profiles (protobuf `ProfileCollection`) |
| `keylists.json` | CD key lists (protobuf `KeyListCollection`) |
| `frameworks.json` | Frameworks: game/d2bs/dll/version bundles (protobuf `FrameworkCollection`) |
| `proxies.json` | Proxies (protobuf `ProxyCollection`) |
| `characters.json` | Character snapshots from running bots (protobuf `CharacterCollection`) |
| `schedules.json` | Schedules with time periods (protobuf `ScheduleCollection`) |
| `patches.json` | Version-specific binary memory patches (protobuf `PatchCollection`) |

Item PNGs from the D2BS `saveItem` message are written to `<BasePath>/images/`.

Legacy JSONL files in `data/` (pre-migration format, auto-migrated on first startup):

| File | Content |
|------|---------|
| `profile.json` | Legacy profiles (JSONL, one per line, includes IRC profiles) |
| `cdkeys.json` | Legacy CD key lists (JSONL) |
| `schedules.json` | Legacy schedules (JSONL, flat time pairs) |
| `patch.json` | Legacy patches (JSONL) |

Item/mule data lives in each framework's `<d2bs_path>/kolbot/mules/` (*.txt files, watched by FileSystemWatcher; aggregated across all frameworks).

## Adding gRPC Methods

1. Define in `protos/*.proto`
2. `dotnet build` in src/D2BotNG (generates C# server types via Grpc.Tools)
3. Implement in `src/D2BotNG/Services/*ServiceImpl.cs`
4. `npm run generate-grpc` in src/D2BotNG.UI (generates TS client types via buf)
5. Add mutation hook in `src/D2BotNG.UI/src/hooks/`
6. Broadcast events via `EventBroadcaster` for real-time updates

## Auth

- Optional password via `d2botng.json` > `server.password`
- Backend: `AuthInterceptor` checks `x-auth-password` gRPC metadata header
- Frontend: `src/lib/auth.ts` Zustand store, password in sessionStorage
- No password configured = no auth required
- Window control RPCs (Show/Hide) restricted to localhost via `context.Peer` check

## Key Constants

| Constant | Value | Location |
|----------|-------|----------|
| HeartbeatTimeout | 30s | Framework default (per-framework, 0 = off) |
| MaxMissedHeartbeats | 3 | Framework default (per-framework) |
| MaxCrashRetries | 5 | Framework default (per-framework) |
| UnresponsiveTimeout | 30s | Framework default (per-framework, 0 = off) |
| MaxHistorySize | 10,000 | MessageService |
| ScheduleCheckInterval | 60s | ScheduleEngine |
| ProcessInputIdleTimeout | 30s | GameLauncher |
| GracefulShutdownPeriod | 5s | ProcessManager |
| ViteDevPort | 4200 | vite.config.ts |
| BackendPort | 5000 | Default in settings |

## Workflow

- **Write files through `Utilities/AtomicFile`, never `File.WriteAllText`/`WriteAllBytes`.** It stages a sibling `.tmp`, flushes it to physical disk, then atomically swaps it over the target, retrying the swap while another process holds the file open. Without the flush a crash can leave a correctly-sized but zero-filled file — that is how an all-null `characters.json` appeared. Async is the default; the sync `WriteAllText` overload exists for callers that cannot await (IniWriter holds a named mutex with thread affinity). The one exception is a caller whose swap can't be a plain replace: `UpdateManager` renames the running exe aside first, since that is the only way Windows replaces an in-use executable, so it flushes durably and does its own swap
- **After making backend changes**, always build with format + inspect and check the SARIF output:
  `dotnet build -p:RunFormat=true -p:RunInspect=true -p:SkipUIBuild=true` then review `src/D2BotNG/obj/inspect.sarif`
- **If no UI changes were made**, skip the UI build for faster iteration:
  `dotnet build -p:SkipUIBuild=true`
- **After making UI changes**, run ESLint AND Prettier — they are separate CI gates and `npm run lint` does NOT include Prettier (neither does `npm run build`, which runs ESLint + `tsc`):
  `npm run lint && npx prettier --check "src/**/*.{ts,tsx,css}"` (use `npm run format` to auto-fix)

## CI/CD (GitHub Actions)

- **`pr.yml`** (on PR to main) - Detects changed paths (TS/C#), conditionally runs: Build TS (ubuntu), Build C# (windows), Format TS (Prettier), Format C# (`dotnet format --verify-no-changes`), Lint TS (ESLint), Lint C# (ReSharper inspectcode)
- **`release.yml`** (on push to main) - Same checks + auto-increment patch version from latest git tag, publish standalone + framework-dependent .exe, create GitHub Release with both artifacts

## Notes

- **x64 build (forced)** - `Platforms`/`PlatformTarget`/`RuntimeIdentifier` in `D2BotNG.csproj` pin x64. Still injects the 32-bit D2BS into a 32-bit game cross-bitness via `RemoteModule` (see Windows Layer)
- **Windows-only** - WinForms, WebView2, Win32 APIs, P/Invoke throughout
- **Dual-mode** - GUI (WebView2 desktop) or headless (server-only with message-only window)
- **Frontend embedded** - Production UI builds to `wwwroot/`, served by Kestrel
- **Protobuf source of truth** - All data models defined in `protos/`, generated for both C# and TS. Field numbers carry no compatibility promise and are kept contiguous (persistence is protobuf JSON matched by field name; the frontend regenerates in lockstep) — except enums with semantic values (e.g. `MessageColor` matches D2 color codes)
- **Frameworks** - A profile launches via its assigned framework (`Profile.framework`). Two orthogonal type axes select the runtime behavior, each with a single value today: `game_type` (`GameType`, in `common.proto`) drives the launch/injection/auth pipeline (the `IGameBackend`), and `botting_framework` (`BottingFramework`) drives the log/heartbeat transport. Both default to 0, so existing data needs no migration. Only matched pairings are valid, enforced in `FrameworkServiceImpl.Normalize` — which also rejects the out-of-range enum values a non-UI gRPC caller can send. `GameLauncher` is a dispatcher over `IGameBackend` keyed on game type; `D2Backend` is the only implementation and holds today's pipeline (clear cache, build args, create suspended, patch, resume, inject, set title), with an unregistered game type throwing a clear `NotSupportedException` at launch. The axes exist so a second game or botting system slots in behind the seam rather than through the launch code; with one value each there is nothing to choose, so the UI has no pickers for them — add those alongside the second implementation. The manager writes d2bs.ini for every framework with a d2bs directory; when a botting system that doesn't use one arrives, that becomes a function of `botting_framework` rather than a separate field. The rest of the bundle: game install directory (`game_directory`), d2bs directory, inject DLL(s) (`dll_paths`, in order), patch version, per-install screenshot/crash-log retention, health/crash-recovery thresholds (`heartbeat_timeout_seconds`/`max_missed_heartbeats`/`max_crash_retries`/`unresponsive_timeout_seconds` — `optional int32`, absent = 30/3/5/30; a heartbeat or unresponsive timeout of 0 disables that watchdog), and injected environment variables (`map<string,string> environment`). `ProfileEngine.MonitorProcessAsync`/`HandleCrashAsync` resolve the thresholds per profile via `FrameworkPaths.*OrDefault`, re-resolving every ~10s tick so edits apply to running profiles; there is no longer a global `EngineSettings`. **UI disclosure:** there is no UI mode setting — nothing is hidden behind a preference. A framework is edited in exactly one place, the Frameworks tab. (There is deliberately no Settings "Game" card mirroring the `Default` framework: that second edit path, keyed on the literal name `Default`, diverged as soon as the framework was renamed.) Controls instead appear when the data makes them meaningful, on one rule — **a chooser is rendered only when there is more than one thing to choose**: the profile's framework dropdown (also forced visible when a profile has *no* framework, i.e. after a framework delete, since the backend deliberately won't rebind those either), and the profiles-table Framework and Game columns (shown once frameworks differ in name / in game type respectively). A new profile is auto-assigned the first framework (so the single-framework case needs no choice, and the picker is preselected when there are several); an existing one with none must be reassigned deliberately. Environment variables layer: manager env < framework `environment` < profile `environment`; the shared `EnvVarsEditor` UI control edits both. `Profile.d2_path` is the game executable the profile launches (required); the framework's `game_directory` is used only for cleanup and as the default folder when browsing for that executable. D2BS/DLL/version + the game directory + cleanup retention are all per-framework — the old `Settings.game`/`Settings.engine` (`GameSettings`/`EngineSettings`) messages are gone entirely, recovered during migration via `SettingsMigrator` (the `Default` framework's `game_directory` auto-detects the D2 install from the registry on first run). `Settings.base_path` (shown as **Data Folder**) stays global: it's the manager's own data dir (`data/ng`, `images/`), shared across all frameworks, so it can't be per-framework
- **Tests** - `tests/D2BotNG.Tests` (xUnit). Deliberately thin: contract/invariant tests that reflection can check but the compiler can't, not behavioural coverage. Run with `dotnet test tests/D2BotNG.Tests/D2BotNG.Tests.csproj -p:SkipUIBuild=true` (the flag matters — the referenced app project builds the UI by default). CI runs it as the **Test C#** job, gated on the same paths filter as the other C# jobs, and a failure blocks release
- **Serilog logging** - Console + daily rolling file (`logs/d2bot-*.log`) + MessageService sink + per-logger level control via UI (LoggerRegistry)
- **CORS** - AllowAnyOrigin configured for development
