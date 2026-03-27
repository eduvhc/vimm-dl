# Changelog

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
