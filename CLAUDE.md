# CLAUDE.md

## Project Overview

Vimm's Lair download queue manager with PS3 ISO conversion and sync. Modular architecture under `Modules/`. React + Vite + Tailwind frontend in `VimmsDownloader/client/`. Host project `VimmsDownloader/`. Mock server in `MockServer/` for testing. GitHub user: eduvhc. Target platform: Linux (Docker + bare metal), also runs on Windows for dev.

## Architecture

### Modules (under `Modules/`)

All modules follow the convention in `Modules/MODULE_GUIDE.md`. Each module is a standalone class library with zero web dependencies, communicating with the host via a typed bridge (`IModuleBridge<TEvent>`).

- **Module.Core** — `IModuleBridge<TEvent>` interface, `Result<T>` (generic result type + `FileOps` safe I/O helpers), `SharedConstants` (`FileExtensions`, `Platforms`), and `Pipeline/` infrastructure (`IPipeline`, `PipelineState`, `PipelinePhase`, `PipelineStatusEvent`, `PipelineFlowInfo`, `DuplicateCheckResult`). Every module references this.
- **Module.Core.Testing** — Shared test infrastructure: `FakeBridge<T>`, `TempDirectory`, `ToolsContainer` (Testcontainers with `ghcr.io/eduvhc/vimm-dl-tools`).
- **Module.Download** — Download service: `DownloadService` (download loop, resume, progress, Result-based error handling), `VaultPageParser` (HTML parsing + format resolution with fallback), `IDownloadItemProvider` (async, host provides queue items). Bridge: `IDownloadBridge` emitting `DownloadStatusEvent`, `DownloadProgressEvent`, `DownloadCompletedEvent`, `DownloadErrorEvent`, `DownloadDoneEvent`.
- **Module.Extractor** — 7z wrapper (`ZipExtract.QuickCheckAsync`, `ExtractAsync` returning `Result<bool>`). Auto-detects `7z` on PATH or `C:\Program Files\7-Zip\7z.exe` on Windows.
- **Module.Ps3IsoTools** — Pure PS3 tools, no pipeline: `ParamSfo` (PARAM.SFO binary parser), `Ps3IsoConverter` (makeps3iso + patchps3iso + `FindJbFolder`, returns `Result<string>`), `IsoFilenameFormatter` (serial/The-fix/region rename with `IsoRenameOptions`).
- **Module.Ps3Pipeline** — PS3 pipeline orchestration, implements `IPipeline`: `Ps3ConversionPipeline` (facade with `BuildFlow`, `CheckDuplicate`, `GetStepDurations`), `Ps3JbFolderPipeline` (extract→convert workers), `Ps3DecIsoPipeline` (rename/extract .dec.iso, optional archive preservation). Uses `PipelineState` from Module.Core. Bridge: `IPs3PipelineBridge : IModuleBridge<PipelineStatusEvent>`.
- **Module.Sync** — Compares ISOs between `completed/` and an external drive. Copy with progress, disk info, space checks via `ISyncBridge`.

### Host (VimmsDownloader/)

- **SRP file structure** — `Program.cs` (startup/DI), `Models.cs` (records + PathHelpers), `AppJsonContext.cs` (JSON source gen), `QueueRepository.cs`, `SettingsKeys.cs`, `DatabaseMigrator.cs` (embedded SQL migrations), `DownloadHub.cs`, `DownloadQueue.cs`, `QueueItemProvider.cs`, `MetadataFetcher.cs`.
- **Endpoints/** — `FileEndpoints` (merged `/api/data` with pipeline trace), `DownloadEndpoints`, `MetadataEndpoints`, `Ps3Endpoints`, `SyncEndpoints`, `SettingsEndpoints`, `EventEndpoints`, `MetricsEndpoints`. 21 endpoints total.
- **SignalR bridges** — `SignalRPs3PipelineBridge.cs`, `SignalRSyncBridge.cs`, `SignalRDownloadBridge.cs` route module events to SignalR + append to events table. Pipeline bridge also updates `completed_urls` projection for terminal states.
- **AOT-ready** — `PublishAot=true`, raw ADO.NET, JSON source generator, all modules `IsAotCompatible`.
- **QueueRepository** — singleton, all async SQLite operations. Database initialized via `DatabaseMigrator` with embedded SQL files.
- **Settings stored in DB** — `settings` table (key-value). Keys in `SettingsKeys.cs`.

### Result Pattern

- `Result<T>` struct in `Module.Core/Result.cs` — zero-allocation, `IsOk`/`Error`/`Value`
- `FileOps` utility — `TryMove`, `TryDelete`, `TryDeleteDirectory`, `TryWriteAllText`
- Modules return `Result<T>` instead of throwing for expected failures
- Exceptions reserved for critical failures + `OperationCanceledException` (cancellation mechanism)

### Event Sourcing

- `events` table — append-only log of ALL module events (download, pipeline, sync) including progress
- `correlation_id` column links all events for a single pipeline run (12-char hex UUID)
- Bridges write to events table before SignalR dispatch
- `completed_urls` is a projection — `conv_phase`/`conv_message`/`iso_filename` updated by pipeline bridge on terminal events
- Events pruned on startup: 7-day retention + 50k max rows

### Database Migrator

- `DatabaseMigrator.cs` — runs embedded SQL files from `Migrations/*.sql` in order
- Tracks executed migrations in `schema_migrations` table
- Each migration is idempotent (catches "duplicate column" / "already exists" errors)
- Migrations split into individual statements for SQLite compatibility

## Database

SQLite, file `queue.db` in `data/` subdirectory (derived from connection string). Seven tables:
- `queued_urls` (id INTEGER PK AUTOINCREMENT, url TEXT, format INTEGER DEFAULT 0) — download queue ordered by id
- `completed_urls` (id INTEGER PK AUTOINCREMENT, url TEXT, filename TEXT, filepath TEXT, completed_at TEXT, conv_phase TEXT, conv_message TEXT, iso_filename TEXT) — finished downloads with conversion state projection
- `url_meta` (url TEXT PK, title TEXT, platform TEXT, size TEXT, formats TEXT, serial TEXT) — metadata cache
- `settings` (key TEXT PK, value TEXT NOT NULL) — user settings
- `events` (id INTEGER PK AUTOINCREMENT, item_name TEXT, event_type TEXT, phase TEXT, message TEXT, data TEXT, timestamp TEXT, correlation_id TEXT) — append-only event log
- `schema_migrations` (name TEXT PK, executed_at TEXT) — migration tracking

### Settings Keys
- `rename_fix_the` = "true" — fix "Godfather, The" → "The Godfather"
- `rename_add_serial` = "true" — append BLES-00043 to filename
- `rename_strip_region` = "true" — remove (Europe) etc.
- `ps3_parallelism` = "3" — workers per pipeline phase
- `ps3_default_format` = "1" — default download format (0=JB Folder, 1=.dec.iso)
- `ps3_preserve_archive` = "true" — keep .7z after conversion
- `sync_path` = "" — external drive path
- `feature_sync` = "false" — beta: Sync tab
- `feature_events` = "false" — developer: Events tab

## Download Flow (Module.Download)

1. `DownloadService.Run()` loops `while (!cancelled)`, gets next item via `IDownloadItemProvider` (async)
2. `VaultPageParser.Parse()` extracts `mediaId`, title, download server, resolves format with fallback (preferred → JB Folder → first available)
3. `StreamDownload` returns `Result<(string, string)>` — no exceptions for HTTP errors
4. Streams with 80KB buffer, reports progress every 2s with speed (MB/s)
5. Crash recovery: detects partial files, resumes via Range headers
6. On completion: `provider.CompleteAsync()` (host handles DB), emits `DownloadCompletedEvent`
7. `OnPostDownload` callback for PS3 pipeline routing
8. Auto-resumes on backend startup if queue has items
9. Background metadata fetch sends `MetaReady` SignalR event for instant UI refresh

## PS3 Pipelines (Module.Ps3Pipeline)

Two scoped pipelines sharing `PipelineState` from Module.Core:

**Ps3JbFolderPipeline** (format=0):
1. Extract archive → find `PS3_GAME/PARAM.SFO` via `FindJbFolder`
2. If no JB folder → delegates to `Ps3DecIsoPipeline.HandleExtractedArchive()` (checks for .dec.iso/.iso)
3. If JB folder found → write crash recovery marker → enqueue to convert workers
4. Convert: parse PARAM.SFO → `makeps3iso` → `patchps3iso` → rename ISO
5. N workers per phase (configurable via `ps3_parallelism` setting)

**Ps3DecIsoPipeline** (format>0):
- Raw `.dec.iso` → `RenameDecIsoAsync()` with `IsoFilenameFormatter`
- Archive with `.dec.iso` → `ExtractAndRenameDecIsoAsync()` → extract → find → rename (optional `deleteArchive` param controlled by `ps3_preserve_archive` setting)
- `HandleExtractedArchive()` returns `Result<string?>` — shared method called by both pipelines

**ISO Filename Formatting** (`IsoFilenameFormatter`):
- Strips all trailing `(Region) (Languages)` groups
- Fixes "The" placement: "Godfather, The" → "The Godfather"
- Appends serial: "- BLES-00043"
- All rules configurable via `IsoRenameOptions` (settings UI toggles, .dec.iso only)

**Pipeline Trace** (pipeline-owned flow):
- Each pipeline implements `IPipeline.BuildFlow()` — defines its own step order, status mapping, and available actions
- `Ps3ConversionPipeline` infers variant (JB folder / dec.iso / dec.iso archive) and builds `PipelineFlowInfo`
- Host's `FileEndpoints.BuildTrace()` delegates to pipeline generically, adds ISO size
- Steps with statuses (pending/active/done/error/skipped) + step durations from in-memory timing

## Pipeline Infrastructure (Module.Core/Pipeline)

- `IPipeline` — generic contract: `GetStatuses()`, `Abort()`, `IsConverted()`, `MarkConverted()`, `BuildFlow()`, `CheckDuplicate()`, `GetStepDurations()`
- `PipelinePhase` — universal lifecycle: Queued, Done, Error, Skipped. `IsActive()`/`IsTerminal()` helpers
- `PipelineState` — shared state class: statuses dict, converted set (seeded from DB on startup), cancellation tokens, correlation IDs, phase timings, bridge
- `PipelineStatusEvent` — generic event: `ItemName`, `Phase`, `Message`, `OutputFilename`, `CorrelationId`
- `PipelineFlowInfo` / `PipelineFlowStep` — pipeline's self-description of its flow for a given item
- `DuplicateCheckResult` — pipeline's answer for duplicate detection
- Console-specific phases (e.g. `Ps3Phase.Extracting`) extend the universal set
- `DownloadQueue.GetPipeline(platform)` — registry for pipeline lookup by platform

## Duplicate Detection

- `POST /api/queue` checks for duplicates before adding URLs
- `QueueRepository.CheckDuplicatesAsync()` — DB query across `queued_urls` and `completed_urls` (case-insensitive URL match, includes platform from `url_meta`)
- Pipeline-owned: `IPipeline.CheckDuplicate()` — each console defines its own filesystem/phase rules
- PS3 rules: active conversions always block, terminal states check disk (archive + ISO existence)
- No files on disk → not a duplicate (user can re-download freely)
- `AddRequest.Force` flag to override duplicate check
- Frontend `DuplicateDialog` shows per-file status with force-add option
- 38 tests covering DB query, filesystem validation, JB Folder, Dec ISO, cross-pipeline, generic fallback

## Frontend (React + Vite + Tailwind)

- `VimmsDownloader/client/` — React 19 + TypeScript + Vite + Tailwind CSS v4
- **PWA** — installable via vite-plugin-pwa, auto-update service worker, workbox caching
- PS3 XMB-inspired dark theme with blue glow accents, PS3 controller button colors (X=blue, O=red, △=green, □=purple)
- **Responsive** — mobile-friendly with `sm:` breakpoint, touch actions always visible, horizontal scroll tabs
- qBittorrent-style layout: Header → Toolbar → ControlBar → TabBar → Content → StatusBar
- Tabs: Active, Completed, Metrics (always visible), Events (developer flag), Sync (beta flag), Settings
- State: React Query for REST data, `DownloadContext` (useReducer) for SignalR live state
- `useSignalR` hook with auto-reconnect, invalidates React Query on data events + `MetaReady` for instant metadata refresh
- Drag-and-drop queue reordering (HTML5 native, bulk reorder via `POST /api/queue/reorder`)
- Auto-restores download state from `/api/data` on page load (isRunning, currentUrl, progress)
- Chart.js + react-chartjs-2 for real-time download speed graph in Metrics tab
- Step durations in pipeline trace (done steps show time, active steps show elapsed)
- `bun run build` outputs to `../wwwroot/`, served by .NET static files
- Dev: Vite proxy on :5173 → .NET on :5031

### Feature Flags
- Stored in `settings` table, exposed in `GET /api/settings` response
- Frontend `TabBar` accepts `hiddenTabs` set, `App.tsx` gates tabs on flags
- Settings UI has Feature Flags section with toggles
- Two tiers: Beta (Sync) and Developer (Events)
- Metrics tab is always visible — not behind a flag

## API Endpoints (21 total)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/data` | Queue + history with pipeline trace + download status |
| GET | `/api/settings` | System info (hostname, IPv4, platform) + user settings |
| POST | `/api/settings` | Save setting by key |
| POST | `/api/settings/check-path` | Validate directory path |
| GET | `/api/version` | Update check (hourly polling) |
| GET | `/api/meta` | Vault page metadata cache |
| GET | `/api/metrics` | Disk usage, queue/completed/orphaned/downloading sizes |
| GET | `/api/events` | Paginated event log with type/item filters |
| POST | `/api/queue` | Add URLs (duplicate check, force flag, default format) |
| PATCH | `/api/queue/{id}` | Move or set format |
| DELETE | `/api/queue/{id}` | Remove from queue |
| DELETE | `/api/queue` | Clear queue |
| POST | `/api/queue/reorder` | Bulk drag-and-drop reorder |
| GET | `/api/queue/export` | Export queue JSON |
| POST | `/api/queue/import` | Import queue JSON (triggers background metadata fetch) |
| DELETE | `/api/completed/{id}` | Remove history entry (optional `?deleteFiles=true` to delete archive + ISO) |
| POST | `/api/ps3/convert` | Convert all or single |
| POST | `/api/ps3/action` | Mark-done or abort |
| POST | `/api/sync/compare` | Set path + compare |
| POST | `/api/sync/copy` | Copy all or single |
| POST | `/api/sync/cancel` | Cancel sync |

## Volume Layout (Docker)

Single bind mount: `-v ~/vimm:/vimms`

```
/vimms/
├── data/
│   └── queue.db          ← SQLite database
└── downloads/
    ├── downloading/      ← Partial files (auto-resume)
    ├── completed/        ← Archives + ISOs
    └── ps3_temp/         ← Conversion temp (auto-cleaned)
```

- Download path derived from DB connection string — `data/` and `downloads/` are siblings
- `Dockerfile` sets `ConnectionStrings__Default="Data Source=/vimms/data/queue.db"`
- `InitAsync` creates `data/` dir, startup creates `downloading/` + `completed/` subdirs
- Bare metal: no connection string override → DB in working dir, downloads in `~/Downloads`

## Testing

238 tests across 6 projects:
- 87 Sync (real file I/O, disk simulation, edge cases)
- 61 Download (state management, file recovery, vault parser, format resolution, edge cases)
- 38 Duplicate detection (DB query, filesystem, JB Folder, Dec ISO, pipeline delegation, generic fallback)
- 25 Extractor (7z integration via Testcontainers)
- 10 Ps3IsoTools (ParamSfo, FindJbFolder, IsoFilenameFormatter)
- 17 Ps3Pipeline (pipeline state, rename, extract, abort, IPipeline contract)

All integration tests use real file I/O via `TempDirectory`. Container tests use `ghcr.io/eduvhc/vimm-dl-tools`.

## Docker

- Main app: `Dockerfile` at repo root. Multi-stage (Bun frontend → ps3tools → .NET AOT → runtime).
- Tools image: `Modules/Module.Core.Testing/Dockerfile.tools`. Published to `ghcr.io/eduvhc/vimm-dl-tools:latest`.
- Single volume: `/vimms` (data/ + downloads/)
- Port 5000
- Assembly version set from git tag via `ARG VERSION` build arg
- **Sync to external drive** — the target drive must be bind-mounted into the container:
  ```bash
  docker run -p 5000:5000 \
    -v ~/vimm:/vimms \
    -v /mnt/usb/PS3ISO:/sync-target \
    --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
  ```

## CI/CD

- `.github/workflows/publish.yml` — Bun build + .NET publish + Docker push on `v*` tags. Passes `VERSION` build arg from tag.
- `.github/workflows/tools-image.yml` — tools image on Dockerfile.tools changes

## Release Flow

One command — creates the git tag, GitHub release with changelog, and triggers CI to build + push the Docker image:

```bash
gh release create v0.8.0 --title "v0.8.0" --notes "$(cat <<'EOF'
## What's New

- Feature X
- Fix Y
EOF
)"
```

To draft first (review before publishing):

```bash
gh release create v0.8.0 --title "v0.8.0" --draft --notes "..."
# Review at https://github.com/eduvhc/vimm-dl/releases, then publish from browser
```

CI extracts version from the tag, passes it as `--build-arg VERSION=0.8.0` to Docker, sets the assembly version via `/p:Version`. No manual csproj edits needed.

After CI completes, update the server:

```bash
docker stop vimm-dl && docker rm vimm-dl
docker pull ghcr.io/eduvhc/vimm-dl:latest
docker run -d -p 5000:5000 -v ~/vimm:/vimms --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

## User Preferences

- Keep it simple. Repository abstraction for DB. No EF Core, no Dapper.
- Result pattern for expected errors, exceptions only for critical failures.
- Backend is the king — frontend renders what backend says, no business logic in UI.
- Modern dark UI — PS3 XMB aesthetic, blue glow, PS3 button colors.
- 2 decimal precision on all percentages and file sizes.
- No redundant status info. Errors only when they happen.
- Linux is the target platform. Windows dev supported (7z auto-detection).
- Bun for frontend builds (not npm).
- Per-platform settings convention: `ps3_*` prefix (e.g. `ps3_default_format`, `ps3_parallelism`).
- MockServer on 5111, main app on 5031 (dev) / 5000 (Docker).
- Future console support: add Module.{Console}Tools + Module.{Console}Pipeline, implement IPipeline.
