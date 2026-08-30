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
| **CaptureService** | captures.proto (`d2bot.captures`) | GetCharacter, SearchItems, ResetKills, ResetAreaTime (schema v2 captures; pulled, not streamed — the selector summaries ride the event stream instead) |

## Event Architecture

Frontend uses a single gRPC server-stream for all real-time state:

1. `useEventStream` hook connects to `EventService.StreamEvents()`
2. Server sends initial snapshots (server info first, then profiles, key lists, proxies, frameworks, characters, schedules, settings, update status, log levels, console history)
3. Server streams incremental changes (ProfileState, Message, Settings, etc.)
4. Zustand `event-store` processes events and updates state maps. Snapshot handlers preserve object identity for content-equal messages (usage snapshots re-broadcast on every profile start/stop; identity-keyed form-seeding effects must not reset)
5. Mutations (create/update/delete) return `Empty` - UI updates arrive via the stream
6. Auto-reconnect on disconnect with 5s retry

**Event types:** ProfilesSnapshot, KeyListsSnapshot, ProxiesSnapshot, FrameworksSnapshot, SchedulesSnapshot, CharactersSnapshot, CapturesSnapshot, ProfileState, CharacterState, CaptureChanged, Message, Settings, UpdateStatus, EntitiesChanged, LogLevelsSnapshot, ServerInfo

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
- **CaptureEngine / CaptureStore** (`Capture/`) - Ingest and SQLite storage for **wire schema v2** character captures, a stack parallel to `CharacterStateService` (v1). See **Capture stack** below
- **ErrorDialogWatcher** - Monitors for game error dialogs
- **DataCache** - Transient key-value store for D2BS data retrieve/store
- **IniWriter** - Rewrites each framework's d2bs.ini, writing only the profiles assigned to that framework
- **LoggerRegistry** - `ILogEventFilter` with per-category log level control, exposed via LoggingService gRPC
- **TrackingLoggerFactory** - Wraps `ILoggerFactory` to register all `D2BotNG.*` logger categories in LoggerRegistry

### Capture stack (`Capture/`, wire schema v2)

A second character stack, parallel to `CharacterStateService` (v1) and sharing no engine, storage or endpoint with it. A profile fills exactly one, decided by the engine DLL it runs; `D2BSMessageHandler` routes on the payload's own `schemaVersion`, so no producer change was needed (both arrive as `func: "characterState"`).

- **Why separate.** v1 sends presentation the game engine already resolved (sprite name, palette shift, sockets as bare codes); v2 sends the raw `D2UnitStrc` capture those answers come from — the `Unit` message, which satisfies **D2ItemToolkit's `IUnit`** via the `CapturedUnit` adapter (`Capture/CapturedUnit.cs`) so the same values feed the tooltip engine with no second copy of the item. Merging would mean fabricating on one side or discarding on the other. Only v2 can be searched *by stat*, because only v2's items carry stats.
- **The protos ARE the wire format.** The engine's JSON parses straight into `captures.proto` via `ProtobufJsonConfig.Parser` and SQL reads rebuild the same messages — no DTO layer. Its own package **`d2bot.captures`** is what lets messages take unprefixed names (`Unit`, `Stat`, `Character`…) that already exist in `d2bot` for v1. `json_name` pins the only four keys that don't line up (`width`/`height` → `w`/`h`, `skill_id`/`hard_points` → `skill`/`hard`).
- **Presence, not values.** Snapshots are section-partial: absent = unchanged, never empty. Two traps this schema had to design around. **Merc dismissal rides `keyframe`, not `merc: null`** — proto3 JSON collapses null to unset, and `optional` can't fix a message field whose loss is in the JSON *mapping*. **A wearer document is detected by `HasName`**, not a non-empty name — the producer legitimately sends an empty one while the client hasn't resolved it, and reading the value would discard a real area/hand/class/skill update. Both pinned by `MercPresenceTests`.
- **Storage is SQLite** (`data/ng/captures.db`, WAL + `synchronous=NORMAL`), decomposed into `item`/`statlist`/`stat` plus `merged_stat`. `item` carries `parent_id`/`socket_index` for socket ownership plus a denormalised `root_id`, so "this item including its gems" is `WHERE root_id = ?` rather than a recursive CTE; `gid` is **not** a key (unique only within one `game_id`). No in-memory mirror — current state *is* the accumulation, and the database is that accumulation. `stat` is `WITHOUT ROWID` keyed by `(statlist_id, ordinal)`, which makes the PK its own lookup index (21% off the file at 337k stats). The file is derived state: one at **any** other schema version is deleted and recreated rather than migrated, since nothing has shipped that would be worth preserving.
- **Search** (`SearchQueryBuilder.cs`) is **stat-first**: each condition becomes a CTE of item ids built from the `stat` side, which the item query semi-joins. A correlated `EXISTS` per candidate item can use no stat index and cannot count. `stat_by_stat(stat_id, layer, statlist_id, value)` carries `statlist_id` *before* the only compared column so the join back is covered — worth 7–13x measured. Counting is why a group aggregates rather than AND-ing: branches `UNION ALL` and `GROUP BY` item id, so `COUNT(*)` is "how many conditions held" and `min_matches` is a `HAVING` — which expresses OR, at-least-N and (with `negate`) NOT. `SocketScope` decides whether a gem's stats count towards its host; `StatListScope` which lists may satisfy a condition — load-bearing, since BASE and AFFIX stats both carry `state_no` 0 and are separated only by `STATLIST_MAGIC` (0x40) — which is the bit on the GRANTED list, the base array being marked `STATLIST_EXTENDED` (0x80000000) instead, so `flags_all = 0x40` reads as "granted only" and `flags_none = 0x40` as "base only".
- **Two stat surfaces, named per condition** (`StatSurface`). RAW reads the per-source `stat` rows the capture literally contained, so a match means "has a *source* of ≥ N" — a Tal Rasha's Crest with an Um in it carries two separate 15s and no 30 anywhere. MERGED reads `merged_stat(item_id, stat_id, layer, value, value_host)`, totals computed at **ingest** by the toolkit's `mergedStats()` with the game's own combine ops applied (a 76 base plus a 45 flat defence is stored as the 121 the game draws, which raw cannot express at all): `value` folds the socket fillers in, `value_host` leaves them out — NULL when the stat exists *only* because of a filler, which is what makes `SocketScope.HOST_ONLY` mean "30 of its own" rather than 30 either way. RAW stays the default so nothing already expressible changes meaning: "all resistances at most 20" matches that Crest through its 15 row and must keep doing so. Three things MERGED deliberately cannot answer — **packed** encodings (charges, chance-to-cast: a packed word is not a quantity, and `scopeFor` routes those terms to RAW whatever the panel asked for), earned **set tier** bonuses (they exist only while the wearer holds the other pieces, so indexing them would drop a belt's defence from 98 to 38 the moment it is muled), and the one asymmetry: a **worn set piece's fillers ARE counted** even though the game sometimes discards them (`ITEM_RecalcAllEquippedItems` rebuilds an equipped set item's list without re-applying them), because an item must not fall out of a search on account of being equipped. The game's own answer is not even stable — the mods come back on a re-socket or re-equip and go on the next recalc, and one session's captures of the *same* equipped helm hold `All Resistances +15` and `+30` — so toolkit 0.4.0 dropped the choice entirely: render, `mergedStats` and `socketFillerStats` all count fillers unconditionally, and there is no longer an option or a flag for the game's answer. Combining MERGED with `lists` or `FILLERS_ONLY` is rejected rather than ignored — both select among sources, and a total has none.
- **Malformed requests are rejected, never repaired** — unknown container/enum, a condition naming no stat ids, an empty group, `min_matches` outside 1..N, `layer_max` without a `layer`, an inverted range, a limit past the page ceiling, an ordering naming an unknown column or no stat ids (silently falling back to the default order returns a page that looks sorted and is not, and the caller reads row one as the best match), or lists past SQLite's parameter *and* structural limits (compound-SELECT arms and expression depth bite far earlier than the parameter cap). Silent normalisation mostly erred towards matching **more** than was asked, the one direction a caller reading results cannot detect. The refusals live in the store, not the service: it is callable in-process, and a guard above it protects only one caller.
- **Two things a caller owns**, because rows store what was captured: values are **raw** (stat ids 6-11 are 8.8 fixed point, so "max life ≥ 100" is `100 << 8`), and one stat id can appear on several of an item's lists, so a match means "has a source of ≥ N", not "totals ≥ N". Three distinct base-item axes, not synonyms: `class_ids` (items.txt rows — a specific base item, the bot-community sense of "class" and the engine's `dwClassId`), `item_types` (ItemTypes.txt rows — a category, matched through the `nEquiv1`/`nEquiv2` ancestor set, which is a DAG since a row has two parents) and `tiers` (normal/exceptional/elite, orthogonal to `quality`). Only `class_ids` is answerable from a capture alone; the other two need items.txt / ItemTypes.txt and are resolved at **ingest** into the `tier`/`type_0`/`type_1` columns (NULL when the class id resolves to no row, so a modded base fails those filters rather than being counted as normal). They resolve in the **manager** from `class_id`, not by the engine sending `wType` — that would cost two ints per item per resend and still need the table, since the closure lives there rather than on the item. `runewords` needs no tables at all: the game reuses `magic_prefix[0]` for the runes.txt id on a runeword, so the capture already carries it — but that slot is an affix index on everything else, so the filter also requires flag 0x4000000, which is part of what the field means rather than a normalisation. Beware the vocabulary: ResurrectedTrade's `item_classes` means *tier*, and its `file_indexes` means an items.txt row, which is the opposite of this contract's `file_index` (`dwFileIndex`, the quality-overloaded uniqueitems/setitems row = their `item_data_file_indexes`).
- **Endpoints are pulled, not streamed** — an inventory carries every item's whole stat list chain. `GetCharacter` returns the whole capture; there is deliberately no ListCharacters RPC beside it, because a selector has to stay live as bots report and a pull cannot do that without polling. This is the **one place the UI issues React Query queries** rather than reading the event stream (`hooks/useCaptures.ts`, `hooks/useItemSearch.ts`). Nothing polls: the **summaries** ride the event stream like everything else (`CapturesSnapshot` on connect, `CaptureChanged` per change), while a capture is fetched on demand and refetched when `CaptureChanged` bumps that profile's revision in the event store. So the list is live for free and the heavy payload moves only for the character on screen. That refetch is debounced ~750ms, because gold and experience churn constantly and each churn is a snapshot — undebounced, watching a running character would refetch its whole inventory several times a second. Search is not refetched at all, since a background refresh would reshuffle the page under the reader. v1 is the contrast worth knowing: it pushes the **whole** `Character` — full inventory included — to every client on every report, which is the thing that does not scale to hundreds of profiles.
- **The UI has two character views, never a conversion** (`features/characters/viewer/`). `CharacterViewer``CharacterViewer` is a shell owning the merged list, selection, liveness and the v1/v2 badge (which lives only in the picker — in the chrome it labelled a view the reader had already chosen); it renders `StreamedCharacterView` (v1) or `CapturedCharacterView` (v2). An earlier attempt normalised v2 into the v1 `Character` shape and was rejected because it had to **invent**: v1 sends a skill's invested points plus the gear share while v2 sends invested plus the bonused total, so soft points came out as `level - hardPoints`; kills had to be re-nested from a flat list. What *is* shared — chrome, paperdolls, grids, panels — is stated as narrow shapes in `viewer/contracts.ts` that each view projects into, so neither schema leaks into the render tree. `ItemTooltip`/`ItemImage` likewise take a structural `RenderableItem` rather than the v1 message, so mule items, v1 items and v2 captures all feed one renderer. `RenderableItem.detail` is an optional **closure** supplying the held-Ctrl view — a re-render with `sockets: 'separated'` (the item without its fillers, then one labelled block per gem/rune) and `ranges` (each stat annotated with the span it could have rolled within). Only the v2 path has one, since a mule or v1 item is finished text with nothing to re-derive. It is stated as ÿcN-coded strings, which is what the description parser already reads, so `features/items` never imports the toolkit and the tables chunk stays out of the initial bundle. Two non-bugs: the required level DROPS in this view (the fillers carry it, and they are excluded), and a socketed *set* item gets no filler blocks because set pieces take a different tooltip path. **Adapters** (`toolkitUnit.ts` / `CapturedUnit.cs`) bridge exactly three gaps to the library's `Unit`: int64→int32 stat values, one `items` list where the capture has `sockets` (an item) or `containers` (a wearer) — concatenated, since a unit is only ever one, and stash pages collapse — that's it, since `location`/`x`/`item_level` all ride along as the document's own fields (`x` already IS the equip location for an equipped item). **`item_level`** is `dwItemLevel`: not a requirement and not the character's level, but what the item rolled at and so which affixes it could have. Stored on `item`, exposed as a search range and a sort key, and fed to the library so item-level-dependent roll ranges narrow instead of landing in `ItemRollRanges.itemLevelDependent`. The three fields v2 does not send — sprite name, palette shift, InvTrans — come from **`d2itemtoolkit`**'s `appearance()`; its embedded tables are a ~735KB blob, so it is the app's only dynamic `import()` and never enters the initial chunk. **Game vocabulary comes from those tables too** (`viewer/gameNames.ts`): a skill id resolves through skills.txt + skilldesc.txt, a monster id through monstats.txt `NameStr`, a class id through charstats.txt — each landing in the LOCALE string table, so these follow the game's language file rather than a transcription in this repo, and they cannot drift from the patch the tables came from. That replaced 1,000-odd hand-copied lines and is why BOTH schemas now load the chunk: a skill id means the same thing whichever stack reported it. Four maps stay hand-written because the tables that would answer them are not shipped — areas and waypoints (Levels.txt), super uniques (SuperUniques.txt), quests, and difficulty names; attribute labels stay too, since ItemStatCost has no NOUN for a stat, only the phrase the game composes ("+# to Strength"), and trimming that back is an English rule. Tooltips are **rendered**, and a v2 capture carries no tooltip text at all. It used to: the producer set the ItemDesc globals, called `LoadItemDesc` and read D2Win's buffer, and we stored the string and showed it verbatim. It is gone from the wire, the `item` table and `Unit` because it costs a game-thread hop per item to obtain what the library derives from the fields already captured — and because it was not a stable answer (see the worn set piece, above: one session captured the same equipped helm at `All Resistances +15` and at `+30`, while the library renders the 30 unconditionally). `RenderableItem.describe()` is how a source that must build its text offers it: **lazy**, because this is per item in a grid and only the hovered one is ever read, and separate from `description` because `isEthereal` reads *that* for every cell it draws and needs no render (a capture states the flag outright). Everything that DISPLAYS a tooltip prefers `describe()`; mule and v1 items arrive as finished text and leave it unset. `render()` takes the wearer because the required level is the one viewer-dependent line (a search result passes none — the item may sit on a mule of any class). The **breakdown** additionally sets `showItemLevel`: ` [ilvl 67]` after the name is a line the game never draws, and it belongs with the roll spans because item level is what decides which affixes an item could have rolled. **Set state is derived from the viewer, never supplied**: `SetItemTooltipInput` is not exported at 0.4.1 — `render(item, viewer)` builds it itself, walking the viewer's `items` for pieces of the same set and reading owned-vs-worn off each one's location plus the hovered item's own. That is exactly why the adapters flatten every container onto a wearer's `items`: hand `render` the character and the piece list, the earned tier and the full-set block all follow. Pass no viewer — a search result does, since the item may sit on a mule of any class — and the derived input is empty: every piece red, no tier, no full-set block, which is what the game draws for a piece carried alone. The one known gap in a rendered SET item is what the capture carries rather than the library: the **partial-tier** bonuses come off the item's own `STATE_ITEMSET` lists (165-170), which a capture does not always carry: in a live snapshot three worn set pieces had theirs (165+166, 165-168, 167) while a socketed Tal Rasha's Crest had only `state 0` lists, so its `+25% FHR / Replenish Life +10 / 65% MF` cannot be rendered at all
- **Item search is a v2-only tab** (`features/characters/search/`), shown once the capture store is non-empty — only v2 stores per-item stat lists, so only v2 can be searched by stat. `statCatalog.ts` derives ~1,230 searchable modifiers from ItemStatCost.txt's `descFunc`, expanding layered stats into one option per class / skill tab / castable skill / aura, and carrying each term's own value scale (raw storage: "+100 life" is `100 << 8`) and layer encoding (class skills = the class; a tab = `(class << 3) + tab`; chance-to-cast and charges pack skill and level, level in the low `skillIdShift` bits). Modelled on ResurrectedTrade with three deliberate differences: groups come from the game's `descGrp` rather than a hand-curated list, an unknown `descFunc` is skipped rather than thrown on, and chance-to-cast keeps the chance and the level as separate bounds instead of collapsing them. `searchRequest.ts` maps a pick onto the contract — one term = a bare condition, several = a group with `min_matches` 1 (OR), a `descGrp` group = `min_matches` absent (ALL), a packed level = a `layer`..`layer_max` range over the one skill. The same-wording merge is keyed on the **shape**, since two sources bounded differently are not interchangeable. **Ordering** has no control of its own: the only handle is a modifier **clicked on a result line** (`SearchResults.tsx` → `SortChoice`), and clicking the line already ranked by reverses it. A rendered line carries its own stat key from the tooltip library, so that is a transcription rather than a resolution — a catalogue pick would be a *different* handle, since one wording may cover several stats at several value scales and only some of them are rankable. Anything else leaves the request's ordering unset, which the store reads as its own grouping order (character, then container, then position) — deliberately not spelled as a column, because it is several and the store owns the list. Three rules make the store side correct: the requested key is a *prefix* of the ORDER BY (the position tiebreakers stay, since no key is unique and OFFSET paging over a non-unique key repeats and skips rows); unknowns sort **last in both directions** (SQLite puts NULLs first ascending, which would lead with the items lacking the thing entirely); and a stat ranks by its **best single source — MAX, never a sum**, and MAX ascending too, because that is what a condition matches on and a MIN would rank by the *worst* source. An ordering names one exact layer or none — a span is a filtering shape, and ranking across one would sort by which skill an item carries rather than by a value — and layer 0 is stated rather than dropped, since 0 is the Amazon and her Bow tab. **Named runewords** need no extra capture: the game reuses `magic_prefix[0]` for the runeword's STRING-TABLE index (not its runes.txt row — real captures give Sanctuary 20627, Call to Arms 20519, Treachery 20653), so the stored value IS the filter and runes.txt (on `D2DataFiles` since 0.2.0) only supplies the name. The catalogue lists the 78 the game *enables* of 169 rows, de-duplicated by id since two rows share "Passion"; the query pairs the id with flag 0x4000000 because that slot means an affix index on everything else. See `docs/plans/2026-08-20-v2-character-viewer.md`
- **Neither character message carries an `online` field** — v2 never had one and v1's was removed. It could only say what was true at the last snapshot, and nothing reports that a profile *stopped*, so it was wrong from shutdown until the next restart; `CharacterViewer.tsx` already derived liveness from the owning profile's run state and ignored it. Both services track "has reported this session" as an in-memory `HashSet<string>` instead, purely to gate time-in-area accrual across restarts — deliberately not a column, since a persisted flag would survive the restart it exists to detect. `CaptureStore` adds to it only *after* the transaction commits, so a failed apply cannot leave the gate claiming one landed.

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
/characters         CharactersPage (tabs: Character viewer, Item search [v2 only], Mule logged items)
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
| `captures.db` | SQLite: characterState **schema v2** captures, items decomposed for stat search (`CaptureStore`). Derived state — deleted and recreated if unreadable or at any other schema version |

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
