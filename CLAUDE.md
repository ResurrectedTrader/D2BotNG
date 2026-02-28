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
  D2BotNG/               # .NET backend (x86 Windows)
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
        ui/              # Reusable UI library (Button, Card, Dialog, Table, Toast, etc.)
      stores/            # Zustand (event-store, toast-store)
      hooks/             # React Query mutations + useEventStream
      lib/               # gRPC client, auth, DC6 rendering pipeline
        rendering/       # dc6Decoder, paletteManager, itemRenderer, colors
      generated/         # Auto-generated protobuf types (buf generate)
Resources/               # DC6 sprites, palettes (pal.dat, Pal.PL2), fonts
docs/plans/              # Design docs and implementation plans
reference/               # Reference D2Bot implementation for parity
```

## Commands

```bash
# Backend
cd src/D2BotNG
dotnet build                       # Build (also builds UI via MSBuild target)
dotnet build -p:SkipUIBuild=true   # Build backend only (skip npm build)
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
# Output: bin/Release/net10.0-windows/win-x86/publish/D2BotNG.exe
```

## gRPC Services

All defined in `protos/*.proto`, implemented in `src/D2BotNG/Services/*ServiceImpl.cs`:

| Service | Proto | Methods |
|---------|-------|---------|
| **ProfileService** | profiles.proto | CRUD, Start/Stop/Restart, ShowWindow/HideWindow, ResetStats, RotateKey, ReleaseKey, SetScheduleEnabled, Reorder, TriggerMule |
| **KeyService** | keys.proto | CreateKeyList, UpdateKeyList, DeleteKeyList, HoldKey, ReleaseHeldKey |
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
2. Server sends initial snapshots (profiles, key lists, schedules, settings, update status, log levels, console history)
3. Server streams incremental changes (ProfileStatusChanged, Message, SettingsChanged, etc.)
4. Zustand `event-store` processes events and updates state maps
5. Mutations (create/update/delete) return `Empty` - UI updates arrive via the stream
6. Auto-reconnect on disconnect with 5s retry

**Event types:** ProfilesSnapshot, KeyListsSnapshot, SchedulesSnapshot, ProfileStatusChanged, Message, SettingsChanged, UpdateStatusChanged, EntitiesChanged, LogLevelsSnapshot

## Backend Architecture

### Engine Layer
- **ProfileEngine** (`Engine/ProfileEngine.cs`) - Core orchestrator. State machine (Stopped -> Starting -> Running -> Stopping -> Stopped, Error -> Starting/Stopping/Stopped), process monitoring, crash recovery (max 5 retries), heartbeat tracking (30s timeout, 3 missed = kill), key rotation
- **ProfileInstance** (`Engine/ProfileInstance.cs`) - Thread-safe state holder per profile with SemaphoreSlim
- **ScheduleEngine** (`Engine/ScheduleEngine.cs`) - Checks schedules every 60s, supports overnight ranges (22:00-06:00)
- **EngineHostedService** - IHostedService that initializes both engines

### Windows Layer
- **GameLauncher** (`Windows/GameLauncher.cs`) - 12-step launch pipeline: clear cache, build CLI args, create suspended process, patch memory, resume, inject D2BS.dll, set title
- **ProcessManager** (`Windows/ProcessManager.cs`) - DLL injection via LoadLibraryA remote thread, process creation, graceful shutdown (WM_CLOSE + force kill), job object for auto-killing child processes on crash
- **Patcher** (`Windows/Patcher.cs`) - Binary memory patches via VirtualProtectEx + WriteProcessMemory
- **MessageWindow** (`Windows/MessageWindow.cs`) - WM_COPYDATA receiver, parses JSON from D2BS, queues to Channel<D2BSMessage>
- **DaclOverwriter** - Changes DACL for elevated process access

### Data Layer
- **FileRepository<TItem, TList>** - Generic protobuf JSON file-backed repo using `JsonFormatter`/`JsonParser`. Stores data in `data/ng/` as single JSON documents (list-wrapper messages from `storage.proto`). Atomic saves (write to `.tmp`, then rename). Thread-safe with SemaphoreSlim. Supports `ReloadAsync()` for base path changes.
- **ProfileRepository** - Extends `FileRepository<Profile, ProfileList>`, writes d2bs.ini via IniWriter on save
- **KeyListRepository** - Extends `FileRepository<KeyList, KeyListCollection>`, round-robin key selection, in-use/held state tracking (transient, not persisted)
- **ItemRepository** - In-memory dictionary with FileSystemWatcher on `d2bs/kolbot/mules/`
- **SettingsRepository** - Singleton, protobuf JSON in `d2botng.json` next to the exe, default D2 path from registry
- **Paths** (`Data/Paths.cs`) - Reactive path resolver, subscribes to `SettingsChanged` event. Exposes BasePath, DataDirectory, D2BSDirectory, MulesDirectory, LegacyDataDirectory
- **ScheduleRepository**, **PatchRepository** - Standard FileRepository implementations
- **Migration** (`Legacy/Models/Migration.cs`) - Static one-time migration from legacy JSONL files (`data/`) to modern protobuf JSON (`data/ng/`). Runs on startup and on base path change. Skips IRC profiles. Separate `MigrateLegacyApi` migrates `server.json` → `LegacyApiSettings`.

### Services Layer
- **EventBroadcaster** - Per-client Channel<Event> (unbounded), pub-sub for gRPC streaming
- **D2BSMessageHandler** - Background service processing WM_COPYDATA messages: heartbeat, updateStatus, printToConsole, uploadItem, rotateKey, etc.
- **MessageService** - Circular buffer of 10k console messages, thread-safe via `Lock`
- **AuthInterceptor** - gRPC interceptor checking `x-auth-password` header
- **DiscordService** - Discord.Net BackgroundService with slash commands (/list, /status, /start, /stop, /restart, /mule, /schedule, /identify), rich embeds, per-user auth, auto-reconnect on settings change
- **UpdateManager** / **UpdateCheckBackgroundService** - Version checking and download management
- **ErrorDialogWatcher** - Monitors for game error dialogs
- **DataCache** - Transient key-value store for D2BS data retrieve/store
- **IniWriter** - Generates d2bs.ini files with game paths and CD keys
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
/schedules          SchedulesPage (schedule CRUD, time period management)
/characters         CharactersPage (entity tree, item search, virtual list)
/settings           SettingsPage (general, server, discord, display, game, legacy API, logging)
```

### Key Patterns
- Feature-based folder structure under `src/features/`
- Reusable UI component library in `src/components/ui/`
- gRPC clients in `src/lib/grpc-client.ts` with auth interceptor
- DC6 sprite rendering pipeline in `src/lib/rendering/`
- Drag-and-drop via `@dnd-kit` for profile reordering
- D2 color codes (ÿc0-ÿc<) parsed in console output

## Data Files

App settings in `d2botng.json` next to the exe (protobuf `Settings` - start_minimized, close_action, server, Discord, display, legacy_api, game, base_path, window geometry).

Bot data in `data/ng/` directory (protobuf JSON format, location determined by `BasePath` in settings):

| File | Content |
|------|---------|
| `profiles.json` | Bot profiles (protobuf `ProfileList`) |
| `keylists.json` | CD key lists (protobuf `KeyListCollection`) |
| `schedules.json` | Schedules with time periods (protobuf `ScheduleList`) |
| `patches.json` | Version-specific binary memory patches (protobuf `PatchList`) |

Legacy JSONL files in `data/` (pre-migration format, auto-migrated on first startup):

| File | Content |
|------|---------|
| `profile.json` | Legacy profiles (JSONL, one per line, includes IRC profiles) |
| `cdkeys.json` | Legacy CD key lists (JSONL) |
| `schedules.json` | Legacy schedules (JSONL, flat time pairs) |
| `patch.json` | Legacy patches (JSONL) |

Item/mule data lives in `d2bs/kolbot/mules/` (*.txt files, watched by FileSystemWatcher).

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
| HeartbeatTimeout | 30s | ProfileEngine |
| MaxMissedHeartbeats | 3 | ProfileEngine |
| MaxCrashRetries | 5 | ProfileEngine |
| MaxHistorySize | 10,000 | MessageService |
| ScheduleCheckInterval | 60s | ScheduleEngine |
| ProcessInputIdleTimeout | 30s | GameLauncher |
| GracefulShutdownPeriod | 5s | ProcessManager |
| ViteDevPort | 4200 | vite.config.ts |
| BackendPort | 5000 | Default in settings |

## Workflow

- **After making backend changes**, always build with format + inspect and check the SARIF output:
  `dotnet build -p:RunFormat=true -p:RunInspect=true -p:SkipUIBuild=true --no-restore` then review `src/D2BotNG/obj/inspect.sarif`
- **If no UI changes were made**, skip the UI build for faster iteration:
  `dotnet build -p:SkipUIBuild=true`

## CI/CD (GitHub Actions)

- **`pr.yml`** (on PR to main) - Detects changed paths (TS/C#), conditionally runs: Build TS (ubuntu), Build C# (windows), Format TS (Prettier), Format C# (`dotnet format --verify-no-changes`), Lint TS (ESLint), Lint C# (ReSharper inspectcode)
- **`release.yml`** (on push to main) - Same checks + auto-increment patch version from latest git tag, publish standalone + framework-dependent .exe, create GitHub Release with both artifacts

## Notes

- **x86 required** - D2BS compatibility (32-bit DLL injection into game process)
- **Windows-only** - WinForms, WebView2, Win32 APIs, P/Invoke throughout
- **Dual-mode** - GUI (WebView2 desktop) or headless (server-only with message-only window)
- **Frontend embedded** - Production UI builds to `wwwroot/`, served by Kestrel
- **Protobuf source of truth** - All data models defined in `protos/`, generated for both C# and TS
- **No tests** - No test projects currently exist
- **Serilog logging** - Console + daily rolling file (`logs/d2bot-*.log`) + MessageService sink + per-logger level control via UI (LoggerRegistry)
- **CORS** - AllowAnyOrigin configured for development
