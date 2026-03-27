# CLAUDE.md

## Project Overview

Vimm's Lair download queue manager with PS3 ISO conversion and sync. Modular architecture under `Modules/`. React + Vite + Tailwind frontend in `VimmsDownloader/client/`. Host project `VimmsDownloader/`. Mock server in `MockServer/` for testing. GitHub user: eduvhc. Target platform: Linux (Docker + bare metal), also runs on Windows for dev.

## Architecture

### Modules (under `Modules/`)

All modules follow the convention in `Modules/MODULE_GUIDE.md`. Each module is a standalone class library with zero web dependencies, communicating with the host via a typed bridge (`IModuleBridge<TEvent>`).

- **Module.Core** — `IModuleBridge<TEvent>` interface, `SharedConstants` (`FileExtensions`, `Platforms`), and `Pipeline/` infrastructure (`IPipeline`, `PipelineState`, `PipelinePhase`, `PipelineStatusEvent`). Every module references this.
- **Module.Core.Testing** — Shared test infrastructure: `FakeBridge<T>`, `TempDirectory`, `ToolsContainer` (Testcontainers with `ghcr.io/eduvhc/vimm-dl-tools`).
- **Module.Download** — Download service: `DownloadService` (download loop, resume, progress), `VaultPageParser` (HTML parsing), `IDownloadItemProvider` (host provides queue items). Bridge: `IDownloadBridge` emitting `DownloadStatusEvent`, `DownloadProgressEvent`, `DownloadCompletedEvent`, `DownloadErrorEvent`, `DownloadDoneEvent`.
- **Module.Extractor** — 7z wrapper (`ZipExtract.QuickCheckAsync`, `ExtractAsync` with progress callback). Auto-detects `7z` on PATH or `C:\Program Files\7-Zip\7z.exe` on Windows via `Create7zProcess()`.
- **Module.Ps3IsoTools** — Pure PS3 tools, no pipeline: `ParamSfo` (PARAM.SFO binary parser), `Ps3IsoConverter` (makeps3iso + patchps3iso + `FindJbFolder`), `IsoFilenameFormatter` (serial/The-fix/region rename with `IsoRenameOptions`).
- **Module.Ps3Pipeline** — PS3 pipeline orchestration, implements `IPipeline`: `Ps3ConversionPipeline` (facade), `Ps3JbFolderPipeline` (extract→convert workers), `Ps3DecIsoPipeline` (rename/extract .dec.iso). Uses `PipelineState` from Module.Core. Bridge: `IPs3PipelineBridge : IModuleBridge<PipelineStatusEvent>`.
- **Module.Sync** — Compares ISOs between `completed/` and an external drive. Copy with progress, disk info, space checks via `ISyncBridge`.

### Host (VimmsDownloader/)

- **SRP file structure** — `Program.cs` (startup/DI), `Models.cs` (records + PathHelpers), `AppJsonContext.cs` (JSON source gen), `QueueRepository.cs`, `SettingsKeys.cs` (centralized DB setting key constants), `DownloadHub.cs`, `DownloadQueue.cs` (thin wrapper delegating to `DownloadService` + PS3 post-download routing), `QueueItemProvider.cs` (`IDownloadItemProvider` wrapping repo).
- **Endpoints/** — `FileEndpoints` (merged `/api/data` with status+partials), `DownloadEndpoints`, `MetadataEndpoints`, `Ps3Endpoints`, `SyncEndpoints`, `SettingsEndpoints` (merged config+settings). 18 endpoints total.
- **SignalR bridges** — `SignalRPs3PipelineBridge.cs`, `SignalRSyncBridge.cs`, `SignalRDownloadBridge.cs` route module events to SignalR. Events serialized via `JsonSerializer.SerializeToElement` with `AppJsonContext`.
- **AOT-ready** — `PublishAot=true`, raw ADO.NET, JSON source generator, all modules `IsAotCompatible`.
- **QueueRepository** — singleton, all SQLite operations. `Init()` handles DB creation, WAL mode, idempotent migrations, default settings.
- **Settings stored in DB** — `settings` table (key-value). Keys in `SettingsKeys.cs`. No more `appsettings.json` for app config (only logging + connection string).

## Database

SQLite, file `queue.db` in working directory (or `/app/data/queue.db` in Docker). Four tables:
- `queued_urls` (id INTEGER PK AUTOINCREMENT, url TEXT, format INTEGER DEFAULT 0) — download queue ordered by id
- `completed_urls` (id INTEGER PK AUTOINCREMENT, url TEXT, filename TEXT, filepath TEXT, completed_at TEXT) — finished downloads
- `url_meta` (url TEXT PK, title TEXT, platform TEXT, size TEXT, formats TEXT, serial TEXT) — metadata cache including game serial number
- `settings` (key TEXT PK, value TEXT NOT NULL) — user settings with defaults:
  - `rename_fix_the` = "true" — fix "Godfather, The" → "The Godfather"
  - `rename_add_serial` = "true" — append BLES-00043 to filename
  - `rename_strip_region` = "true" — remove (Europe) etc.
  - `ps3_parallelism` = "3" — workers per pipeline phase
  - `download_path` = "" — falls back to ~/Downloads
  - `sync_path` = "" — external drive path

## Download Flow (Module.Download)

1. `DownloadService.Run()` loops `while (!cancelled)`, gets next item via `IDownloadItemProvider`
2. `VaultPageParser.Parse()` extracts `mediaId`, title, download server from vault HTML
3. Resolves download URL with format param (`&alt={format}` for format>0)
4. Streams with 80KB buffer, reports progress every 2s with speed (MB/s)
5. Crash recovery: detects partial files, resumes via Range headers
6. On completion: `provider.Complete()` (host handles DB), emits `DownloadCompletedEvent`
7. `OnPostDownload` callback for PS3 pipeline routing
8. Auto-resumes on backend startup if queue has items

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
- Archive with `.dec.iso` → `ExtractAndRenameDecIsoAsync()` → extract → find → rename
- `HandleExtractedArchive()` — shared method called by both dec.iso pipeline and JB folder fallback

**ISO Filename Formatting** (`IsoFilenameFormatter`):
- Strips all trailing `(Region) (Languages)` groups
- Fixes "The" placement: "Godfather, The" → "The Godfather"
- Appends serial: "- BLES-00043"
- All rules configurable via `IsoRenameOptions` (settings UI toggles)

## Pipeline Infrastructure (Module.Core/Pipeline)

- `IPipeline` — generic contract: `GetStatuses()`, `Abort()`, `IsConverted()`, `MarkConverted()`
- `PipelinePhase` — universal lifecycle: Queued, Done, Error, Skipped. `IsActive()`/`IsTerminal()` helpers
- `PipelineState` — shared state class (not base class): statuses dict, converted set/file, cancellation tokens, bridge
- `PipelineStatusEvent` — generic event: `ItemName`, `Phase`, `Message`, `OutputFilename`
- Console-specific phases (e.g. `Ps3Phase.Extracting`) extend the universal set

## Frontend (React + Vite + Tailwind)

- `VimmsDownloader/client/` — React 19 + TypeScript + Vite + Tailwind CSS v4
- PS3 XMB-inspired dark theme with blue glow accents, PS3 controller button colors (X=blue, O=red, △=green, □=purple)
- qBittorrent-style layout: Header → Toolbar → ControlBar → TabBar → Content → StatusBar
- Tabs: Active, Completed, Sync, Settings
- State: React Query for REST data, `DownloadContext` (useReducer) for SignalR live state
- `useSignalR` hook with auto-reconnect, invalidates React Query on data events
- Drag-and-drop queue reordering (HTML5 native, bulk reorder via `POST /api/queue/reorder`)
- Auto-restores download state from `/api/data` on page load (isRunning, currentUrl, progress)
- `npm run build` outputs to `../wwwroot/`, served by .NET static files
- Dev: Vite proxy on :5173 → .NET on :5031

## API Endpoints (18 total)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/data` | Queue + history + download status (merged) |
| GET | `/api/settings` | System info + user settings (merged) |
| POST | `/api/settings` | Save setting by key |
| POST | `/api/settings/check-path` | Validate directory path |
| GET | `/api/version` | Update check |
| GET | `/api/meta` | Vault page metadata cache |
| POST | `/api/queue` | Add URLs |
| PATCH | `/api/queue/{id}` | Move or set format |
| DELETE | `/api/queue/{id}` | Remove from queue |
| DELETE | `/api/queue` | Clear queue |
| POST | `/api/queue/reorder` | Bulk drag-and-drop reorder |
| GET | `/api/queue/export` | Export queue JSON |
| POST | `/api/queue/import` | Import queue JSON |
| DELETE | `/api/completed/{id}` | Remove history entry |
| POST | `/api/ps3/convert` | Convert all or single (optional filename) |
| POST | `/api/ps3/action` | Mark-done or abort |
| POST | `/api/sync/compare` | Set path + compare |
| POST | `/api/sync/copy` | Copy all or single (optional filename) |
| POST | `/api/sync/cancel` | Cancel sync |

## Magic String Elimination

- `ConversionPhase` → replaced by `PipelinePhase` (Module.Core) + `Ps3Phase` (Module.Ps3Pipeline)
- Platform strings → `Platforms.IsPS3()` etc. in `Module.Core.SharedConstants`
- File extensions → `FileExtensions.IsDecIso()`, `.IsArchive()`, `.IsIso()` in Module.Core
- Settings keys → `SettingsKeys.*` constants in host
- No raw strings for phases, platforms, extensions, or settings keys anywhere in the codebase

## Testing

181 tests across 5 modules:
- 87 Sync (real file I/O, disk simulation, edge cases)
- 51 Download (state management, file recovery, vault parser, edge cases)
- 16 Extractor (7z integration via Testcontainers)
- 10 Ps3IsoTools (ParamSfo, FindJbFolder, IsoFilenameFormatter)
- 17 Ps3Pipeline (pipeline state, rename, extract, abort, IPipeline contract)

All integration tests use real file I/O via `TempDirectory`. Container tests use `ghcr.io/eduvhc/vimm-dl-tools`.

## Docker

- Main app: `Dockerfile` at repo root. Multi-stage (Node frontend → ps3tools → .NET AOT → runtime).
- Tools image: `Modules/Module.Core.Testing/Dockerfile.tools`. Published to `ghcr.io/eduvhc/vimm-dl-tools:latest`.
- Two volumes: `/app/data` (SQLite DB), `/downloads` (downloading/ + completed/ + ps3_temp/)
- Port 5000
- **Sync to external drive** — the target drive must be bind-mounted into the container. Without a bind mount, the container cannot access host drives.
  ```bash
  # Linux
  docker run -v /mnt/usb/PS3ISO:/sync-target ...

  # Windows (e.g. H:\PS3ISO external drive)
  docker run -v H:\PS3ISO:/sync-target ...

  # Docker Compose
  volumes:
    - /app/data:/app/data
    - /downloads:/downloads
    - H:\PS3ISO:/sync-target    # Windows external drive
  ```
  Then set the sync path in Settings to `/sync-target`.

## CI/CD

- `.github/workflows/publish.yml` — Node build + .NET publish + Docker push on `v*` tags
- `.github/workflows/tools-image.yml` — tools image on Dockerfile.tools changes

## User Preferences

- Keep it simple. Repository abstraction for DB. No EF Core, no Dapper.
- Modern dark UI — PS3 XMB aesthetic, blue glow, PS3 button colors.
- 2 decimal precision on all percentages and file sizes.
- No redundant status info. Errors only when they happen.
- Linux is the target platform. Windows dev supported (7z auto-detection).
- MockServer on 5111, main app on 5031 (dev) / 5000 (Docker).
- Future console support: add Module.{Console}Tools + Module.{Console}Pipeline, implement IPipeline.
