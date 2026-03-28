# Changelog

## v0.6.0

### Event Sourcing & Audit
- **Event log** — append-only `events` table captures every download, pipeline, and sync event with timestamps and JSON payloads
- **Events tab** — filterable audit view (Status, Progress, Downloads, Errors, Pipeline, Sync) with expandable rows and copy buttons
- **Real-time events** — SignalR pushes invalidate the events query instantly
- **Retention** — auto-prune on startup: 7-day retention + 50k row cap

### Result Pattern
- **`Result<T>`** in Module.Core — zero-allocation struct replacing exception-based error handling across all modules
- **`FileOps`** utility — `TryMove`, `TryDelete`, `TryDeleteDirectory`, `TryWriteAllText` for safe file operations
- **DownloadService** — `StreamDownload` returns `Result<(string, string)>` instead of throwing
- **ZipExtract** — `QuickCheckAsync`/`ExtractAsync` return `Result<bool>` instead of tuples
- **Ps3IsoConverter** — `ConvertFolderToIsoAsync` returns `Result<string>`, deleted `ConversionResult`
- Exceptions reserved for critical failures + `OperationCanceledException`

### Database Migrator
- **`DatabaseMigrator.cs`** — embedded SQL migrations from `Migrations/*.sql`, tracked in `schema_migrations` table
- Idempotent: catches "duplicate column" / "already exists" errors gracefully
- Replaces inline `InitAsync` schema code — add `NNN_description.sql` for new migrations

### Metrics Dashboard
- **Metrics tab** (always visible) — system info, download speed chart, disk usage
- **System info card** — hostname, IPv4, platform, OS, download path
- **Download speed chart** — real-time Chart.js line chart from SignalR progress (rolling 2min window), current + average speed
- **Disk usage card** — volume bar, queued vs free ratio, completed/downloading/orphaned breakdown with contextual hints

### Pipeline Trace
- **Backend-driven trace** — `PipelineTrace` per completed item with steps, statuses, and available actions
- **Frontend renders what backend says** — zero business logic in `HistoryItem.tsx`, all phase/badge/action decisions made server-side
- Step indicators: Extract → Convert (JB folder) or Extract → Rename (dec.iso)

### PS3 Improvements
- **Default format per platform** — `ps3_default_format` setting, defaults to 1 (.dec.iso)
- **Format fallback** — if preferred format unavailable, falls back to JB Folder with status event notification
- **Preserve archive** — `ps3_preserve_archive` setting (default: true) keeps .7z after conversion
- **Settings consolidated** — ISO rename rules, default format, preserve archive, parallelism all under "PS3" section with `.dec.iso only` info tag

### Feature Flags
- **Beta/Developer tiers** — `feature_sync` (Beta), `feature_events` (Developer) stored in settings table
- **Tab gating** — hidden tabs auto-detected from settings, falls back to Active if current tab hidden
- **Settings UI** — Feature Flags section with toggles

### Download Improvements
- **Async repository** — all `QueueRepository` methods converted to async
- **Background metadata fetch** — `MetaReady` SignalR event triggers instant queue refresh (no F5 needed)
- **Format in events** — download status includes `[JB Folder]` or `[.dec.iso (format N)]`
- **429 rate limit** — logged as status (not error) since it's a transient retry
- **No queue limit** — removed 40-item cap on add endpoint

### UI Enhancements
- **Completed item delete** — inline confirmation with "Record only" or "Delete files too" (removes archive + ISO)
- **Queue item pause** — active download shows pause button instead of play
- **Settings grid layout** — responsive 1/2/3 column grid
- **Toggle reactivity** — fixed `staleTime: Infinity` + empty response parsing that prevented settings from updating

### Infrastructure
- **Download path auto-detection** — `/downloads` if exists (Docker volume), else `~/Downloads` (bare metal). No env var, no setting
- **Conversion state in DB** — `conv_phase`/`conv_message`/`iso_filename` columns on `completed_urls`, replaces `.ps3converted` file
- **Converted set seeded from DB** — `PipelineState.SeedConverted()` replaces file-based `LoadConvertedList()`
- **One-time migration** — `.ps3converted` file migrated to DB on first startup, renamed to `.ps3converted.migrated`

## v0.5.0

### Architecture
- **Module.Ps3Iso split** into `Module.Ps3IsoTools` (pure tools: ParamSfo, Ps3IsoConverter, IsoFilenameFormatter) and `Module.Ps3Pipeline` (orchestration: JB folder + dec.iso pipelines)
- **IPipeline contract** in `Module.Core/Pipeline/` — generic interface for console pipelines with `PipelineState`, `PipelinePhase`, `PipelineStatusEvent`. Future consoles implement `IPipeline`
- **Module.Download** — download loop extracted from `DownloadQueue.cs` into a proper module with bridge events, `VaultPageParser`, `IDownloadItemProvider`. No repo/SignalR/ASP.NET dependencies
- **Magic strings eliminated** — phases (`PipelinePhase`, `Ps3Phase`), platforms (`Platforms.IsPS3()`), extensions (`FileExtensions.IsDecIso()`), settings (`SettingsKeys.*`)
- **Endpoint consolidation** — 22 → 18 endpoints: merged status+partials into data, config into settings, queue move+format into PATCH, PS3 convert+single, sync path+compare, sync copy+single

### Frontend
- **React + Vite + Tailwind** — replaced 831-line vanilla `index.html` with component-based React 19 + TypeScript app
- **PS3 XMB theme** — deep blue-black, blue glow, PS3 button colors (X=blue, O=red, △=green, □=purple)
- **qBittorrent layout** — toolbar → controls → tabs (Active/Completed/Sync/Settings) → content → status bar
- **Drag-and-drop reorder** — HTML5 native drag with bulk `POST /api/queue/reorder`, removed up/down arrows
- **Download speed** — real-time MB/s in queue items, calculated server-side
- **Settings tab** — ISO rename toggles (fix "The", add serial, strip region) + parallelism slider
- **Auto-restore** — reads `isRunning`/`currentUrl`/`progress` from `/api/data` on page load

### PS3 Pipeline
- **Archive .dec.iso support** — format>0 archives extracted, .dec.iso found inside → renamed → moved to completed
- **JB folder fallback** — `Ps3DecIsoPipeline.HandleExtractedArchive()` delegates cleanly instead of internal hack
- **ISO filename formatting** — `IsoFilenameFormatter`: fix "The" placement, add serial (BLES-00043), strip region. All configurable
- **Serial number** — scraped from vault HTML, stored in `url_meta.serial`

### Configuration
- **Settings in SQLite** — `settings` table with `SettingsKeys` constants. Removed `DownloadPath`/`SyncPath`/`Ps3ConvertParallelism` from `appsettings.json`

### Download
- **Auto-resume on startup** — backend starts downloading immediately if queue has items
- **IsPaused reset** — `Run()` clears `IsPaused` flag, fixing ghost paused state after resume
- **7z auto-detection** — Windows: finds `C:\Program Files\7-Zip\7z.exe` when not in PATH

### Testing
- 181 tests across 5 modules: 87 Sync, 51 Download, 16 Extractor, 10 Ps3IsoTools, 17 Ps3Pipeline
- Download module: state management, file recovery, vault parser edge cases
- ISO formatter: "The" fix, serial append, region stripping, options

### Breaking API Changes
- `GET /api/status`, `/api/partials`, `/api/config` removed (merged)
- `POST /api/queue/move`, `/api/queue/format` → `PATCH /api/queue/{id}`
- `POST /api/convert-ps3/*` → `POST /api/ps3/convert` + `/api/ps3/action`
- `GET /api/sync/compare` → `POST`, `POST /api/sync/path` removed
- SignalR `ConvertStatus`: `zipName` → `itemName`, `isoFilename` → `outputFilename`
- `DataResponse` includes status fields, `SettingsResponse` includes system info

## v0.4.0

### Added
- **Two-section UI** -- Active (queue + converting) and History (completed with inline ISO status). Replaces the confusing three-section layout
- **Enriched history items** -- game title, platform icon, archive/ISO file sizes, existence checks, completion timestamps all shown inline
- **Inline ISO status** -- each completed PS3 item shows its ISO status directly (Ready/Converting/Failed/Not converted) with action buttons
- **Smart crash recovery** -- extraction marker files (`.extraction_complete`) allow the app to skip re-extraction after a crash and resume directly from ISO conversion
- **Archive header validation** -- quick `7z l` check catches truncated/corrupt archives before extraction starts, without reading the full file
- **Multithreaded extraction** -- `7z x -mmt=on` enables multi-core decompression for archives that support it
- **DB index** -- `completed_urls.url` indexed for faster metadata joins as history grows
- **Completion timestamps** -- `completed_at` column tracks when each download finished

### Changed
- **SRP refactoring** -- `Program.cs` (1562 lines) split into 13 files: `Models.cs`, `AppJsonContext.cs`, `QueueRepository.cs`, `DownloadHub.cs`, `DownloadQueue.cs`, `Ps3ConversionPipeline.cs`, and 5 endpoint files under `Endpoints/`
- **Unified `/api/data` endpoint** -- returns enriched history with metadata, conversion status, ISO info, and file existence in one call. Eliminates N+1 `/api/check-exists` calls
- **Structured ISO filename** -- `ConvertStatusUpdate` carries `IsoFilename` field instead of encoding it in the message string
- **`IsArchive()` centralized** -- shared `PathHelpers.IsArchive()` replaces duplicate extension checks across files
- **Frontend debouncing** -- `loadData()` debounced and fetches parallelized with `Promise.all`
- **Async temp cleanup** -- post-conversion temp directory deletion runs in background, unblocking the worker for the next job
- **`HasQueuedUrls()` optimized** -- uses `EXISTS` instead of `COUNT(*)`

### Removed
- **PS3 Conversion section** -- conversion status now inline in Active and History sections
- **`/api/check-exists`** -- file existence folded into `/api/data`
- **`/api/convert-ps3/status`** -- conversion status folded into `/api/data`
- Dead code: `GetCompletedItems()`, `GetCompletedPs3FilePaths()`, `CheckExistsResponse`

### Fixed
- File handle not closed before move causing "Zip file no longer exists" error on PS3 conversion (explicit `DisposeAsync` before `File.Move`)
- `prefers-reduced-motion` respected for progress bar animations
- Tabular numerics (`font-variant-numeric: tabular-nums`) on all size/percentage displays

## v0.3.0

### Added
- **PS3 ISO conversion** -- PS3 JB Folder downloads are automatically converted to ISO after completion (extract 7z/zip, makeps3iso, patchps3iso, firmware patch 3.55)
- **Parallel conversion pipeline** -- configurable N workers per phase (default 3 via `Ps3ConvertParallelism`). Multiple extractions and conversions run simultaneously
- **Convert All button** -- one-click bulk conversion for all completed PS3 archives. Skips already-converted items (tracked in `.ps3converted` file, persists across restarts)
- **Per-item convert button** -- gear icon on each completed archive to convert individually, even if already done (force re-convert)
- **Mark as converted** -- checkmark button on completed archives to mark as already converted without processing. Prevents "Convert All" from re-processing
- **Conversion UI section** -- per-file status cards with color-coded phase badges (Extract 42% / Converting / Done / Error), inline progress bars, retry on error, dismiss completed
- **Format selection** -- dropdown on queue items to choose download format (e.g. PS3: JB Folder or .dec.iso), with per-format file sizes
- **Native AOT** -- app compiles to a self-contained native binary. No .NET runtime needed at runtime. Faster startup, smaller memory footprint
- **Crash recovery for conversions** -- orphaned temp extraction dirs and partial ISOs are cleaned up automatically on startup
- **Filesystem scan** -- completed list shows all archives on disk (7z/zip/rar), not just DB entries. Files placed manually in `completed/` are picked up
- **Extraction progress** -- real-time percentage from 7z output (parses backspace-delimited progress), shown in badge and progress bar
- **Disk space check** -- extraction skipped with error if insufficient space (~3x zip size needed)
- **Delete stale entries** -- X button on "File removed" completed items to clean up the DB

### Changed
- **Dapper removed** -- replaced with raw ADO.NET (`SqliteCommand`/`SqliteDataReader`) for full AOT compatibility
- **Repository pattern** -- all DB operations extracted to `QueueRepository` class
- **JSON source generators** -- all API responses use named record types with `[JsonSerializable]` attributes. No reflection-based serialization
- **SignalR AOT** -- configured with `AddJsonProtocol` using source-generated JSON context
- **Docker image** -- switched from `aspnet:10.0` (JIT) to `sdk:10.0-noble-aot` build + `runtime-deps:10.0-noble` runtime. Three-stage build includes ps3iso-utils compilation
- **File exists check** -- now checks stored filepath first (handles Docker/path changes), with trailing separator handling
- **Completed list** -- only shows archives (7z/zip/rar), ISOs filtered out. Hidden files (`.ps3converted`) excluded

### Fixed
- Completed files showing "File removed" when running in Docker (path reconstruction mismatch)
- `EndsWith("downloading")` check failing with trailing path separators
- Partial progress bar matching wrong game when titles share a prefix (e.g. "Grand Theft Auto IV" matching "Grand Theft Auto V")
- JSON parse errors when converting/marking files that no longer exist on disk
- 7z progress not updating (was looking for `\r` but 7z uses `\b` backspace characters)

## v0.2.0

### Added
- **Update checker** -- app checks GitHub for new releases on startup, shows a dismissable banner with changelog link
- **Partial progress on queue items** -- when switching downloads, previously downloading items show their progress bar
- **Smart URL parsing** -- paste concatenated URLs (`https://...https://...`), space/comma separated, or mixed with text
- **Auto-start on add** -- adding URLs automatically starts downloading if not already running
- **Version from assembly** -- version set in `.csproj`, read at runtime, compared with semver

### Fixed
- Partials API now always checks `downloading/` subfolder correctly
- Partial file cache refreshes when switching between downloads or pausing
- Pause button uses clean CSS pseudo-elements instead of Unicode characters

### Changed
- Removed Save To toolbar from UI (path configured via env var or appsettings.json)
- Removed folder browser (Browse button)
- Cleaner queue item action buttons (play/pause/resume reflect actual state)

## v0.1.0

### Added
- Queue-based download manager for Vimm's Lair
- Pause/resume with HTTP Range headers
- Auto-resume on app restart
- Two-folder system (downloading/ + completed/)
- Metadata fetching (title, platform, size) cached in SQLite
- Platform icons with CSS mask coloring
- Real-time progress via SignalR (2 decimal precision)
- Background downloads (survives browser close)
- Queue reordering (up/down arrows)
- Per-item play/pause/remove buttons
- Crash recovery (detects complete files in downloading/)
- Race condition safe (locked + transactional queue mutations)
- Docker support with volume mounts
- Mock server for testing
- GitHub Actions CI (Docker image to ghcr.io)
