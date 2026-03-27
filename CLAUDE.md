# CLAUDE.md

## Project Overview

Vimm's Lair download queue manager with PS3 ISO conversion and sync. Modular architecture under `Modules/`. Host project `VimmsDownloader/` with UI in `wwwroot/index.html`. Mock server in `MockServer/` for testing. GitHub user: eduvhc. Target platform: Linux (Docker + bare metal).

## Architecture

### Modules (under `Modules/`)

All modules follow the convention in `Modules/MODULE_GUIDE.md`. Each module is a standalone class library with zero web dependencies, communicating with the host via a typed bridge (`IModuleBridge<TEvent>`).

- **Module.Core** - `IModuleBridge<TEvent>` interface. Every module references this.
- **Module.Core.Testing** - Shared test infrastructure: `FakeBridge<T>`, `TempDirectory`, `ToolsContainer` (Testcontainers with `ghcr.io/eduvhc/vimm-dl-tools`).
- **Module.Extractor** - 7z wrapper (`ZipExtract.QuickCheckAsync`, `ExtractAsync` with progress callback). Pure static utility, no bridge needed.
- **Module.Ps3Iso** - PS3 ISO pipeline: `ParamSfo` (PARAM.SFO binary parser), `Ps3IsoConverter` (makeps3iso + patchps3iso), `Ps3ConversionPipeline` (two-phase extract→convert with `IPs3IsoBridge`). Also handles `.dec.iso` → `.iso` rename.
- **Module.Sync** - Compares ISOs between `completed/` and an external drive. Copy with progress, disk info, space checks via `ISyncBridge`.

### Host (VimmsDownloader/)

- **SRP file structure** - `Program.cs` (startup/DI only), `Models.cs` (records + PathHelpers), `AppJsonContext.cs` (JSON source gen), `QueueRepository.cs`, `DownloadHub.cs`, `DownloadQueue.cs`, and `Endpoints/` folder with 6 endpoint files (`FileEndpoints`, `DownloadEndpoints`, `MetadataEndpoints`, `ConfigEndpoints`, `Ps3Endpoints`, `SyncEndpoints`).
- **SignalR bridges** - `SignalRPs3IsoBridge.cs` and `SignalRSyncBridge.cs` route module events to SignalR. Events are serialized via `JsonSerializer.SerializeToElement` with `AppJsonContext` to ensure correct camelCase property names under AOT.
- **AOT-ready** - `PublishAot=true`, raw ADO.NET via `SqliteCommand`/`SqliteDataReader`, JSON source generator (`AppJsonContext`), all API responses use named record types. SignalR configured with `AddJsonProtocol` using same source-generated context. All modules marked `IsAotCompatible`.
- **QueueRepository** - singleton, encapsulates all SQLite operations. Uses `$param` style parameters. `Init()` handles DB creation, WAL mode, idempotent migrations.
- **DownloadQueue** - singleton. Uses `IHubContext<DownloadHub>` for SignalR broadcasts. Downloads survive browser close. After completion, routes PS3 downloads to the appropriate pipeline based on format.
- **QueueLock.Sync** - static lock object used by queue mutations (move, delete, complete). All multi-step DB ops wrapped in transactions.
- **Three-folder pattern** - `{path}/downloading/` for active downloads, `{path}/completed/` for done files + ISOs, `{path}/ps3_temp/{guid}/` for extraction scratch space.

## Database

SQLite, file `queue.db` in working directory (or `/app/data/queue.db` in Docker via `ConnectionStrings__Default`). Three tables:
- `queued_urls` (id INTEGER PK AUTOINCREMENT, url TEXT, format INTEGER DEFAULT 0) - download queue, ordered by id. format=0 is default (e.g. JB Folder for PS3), format>0 uses alt param
- `completed_urls` (id INTEGER PK AUTOINCREMENT, url TEXT, filename TEXT, filepath TEXT, completed_at TEXT) - finished downloads with full path and timestamp. Indexed on `url` for metadata joins
- `url_meta` (url TEXT PK, title TEXT, platform TEXT, size TEXT, formats TEXT) - metadata cache to avoid re-fetching, formats is JSON array of {value,label,title,size}

WAL mode set on init. Idempotent migrations: tables created with `IF NOT EXISTS`, columns added with `try { ALTER TABLE ADD COLUMN } catch { }`. DB path configurable via `ConnectionStrings:Default` in config/env.

## Download Flow

1. `DownloadQueue.Run()` loops `while (!cancelled)`, picks `SELECT ... ORDER BY id LIMIT 1`
2. Fetches vault page HTML, extracts `mediaId` from hidden input via regex
3. Resolves download server URL (tries: form action attr, JS `.action=`, `dl*.vimm.net` in source, fallback `https://dl3.vimm.net/`)
4. First GET request gets filename from Content-Disposition + total size from Content-Length
5. Checks `downloading/` for existing partial file (crash recovery / resume)
6. Streams to FileStream with 80KB buffer, reports progress every 2 seconds
7. On completion: lock → File.Move to completed → BEGIN TRANSACTION → DELETE from queue + INSERT into completed → COMMIT (rollback moves file back on failure)
8. Post-download PS3 routing:
   - **format=0** (JB Folder) + platform="PlayStation 3" → enqueue to `Ps3ConversionPipeline` (extract + convert)
   - **format>0** + filename ends with `.dec.iso` + platform="PlayStation 3" → `RenameDecIsoAsync` (strip `.dec` suffix)
9. Random 5-30s delay, then next item

## PS3 ISO Conversion Pipeline (Module.Ps3Iso)

Two-phase pipeline with N workers per phase (default N=3, configurable via `Ps3ConvertParallelism`):

**Phase 1 - Extraction** (N workers read from `_extractQueue`):
1. Quick archive header check via `7z l` (catches truncated/corrupt archives without full I/O)
2. Check zip exists, check disk space (~3x zip size needed)
3. Extract to `{downloadPath}/ps3_temp/{guid}/` via `ZipExtract.ExtractAsync` (7z with `-mmt=on`)
4. Find JB folder via `Ps3IsoConverter.FindJbFolder` (checks root, 1-deep, 2-deep for `PS3_GAME/PARAM.SFO`)
5. Write `.extraction_complete` marker (zipName + jbFolder path) for crash recovery
6. Push to conversion queue

**Phase 2 - Conversion** (N workers read from `_convertQueue`):
1. Parse PARAM.SFO for game title + ID
2. Run `makeps3iso` → `patchps3iso` (firmware 3.55)
3. Rename ISO to `[Game-Name] - [Game-ID].iso`
4. Place ISO in `completed/`
5. Delete temp extraction folder

**Dec ISO rename** - `RenameDecIsoAsync(filePath)`: strips `.dec` from `.dec.iso` files. Emits converting→done status events, adds to converted list.

**Crash recovery** (runs on startup via `CleanupOrphans`):
- Checks each `ps3_temp/` subdir for `.extraction_complete` marker
- If marker found → extraction was complete, enqueues directly to convert queue (skips re-extraction)
- If no marker → orphaned incomplete extraction, deletes the dir
- Deletes `temp_*.iso` files in `completed/` (orphaned partial ISOs)

## Sync Module (Module.Sync)

Compares `.iso` files between `completed/` and a configurable target path (e.g. `H:\PS3ISO`).

- `Compare()` returns new (source only), synced (both), target-only lists with disk info for both drives
- `CopyFileAsync()` streams file with progress events, pre-flight checks (path accessible, free space, source exists)
- `CopyAllNewAsync()` copies all new files with shared CancellationTokenSource
- Handles edge cases: disk disconnection, drive not ready, mid-copy failures, partial file cleanup
- UI section with path input, disk info cards (ISO count, free/total space, usage bar), file list with copy buttons

## Format Selection

- `.dec.iso` format is auto-selected when metadata loads and a `.dec.iso` option exists (`autoSelectDecIso` in UI)
- Format can be changed via dropdown before download starts
- format=0 → JB Folder (default for non-PS3 or when no .dec.iso available), format>0 → alt format with `&alt={format}` URL param

## Vimm's Lair HTML Structure

- **Title**: `<title>The Vault: Game Name (System)</title>` - strip "The Vault: " prefix, remove trailing `(System)`
- **Platform**: `<div class="sectionTitle">PlayStation 3</div>` (NOT `<h2>`)
- **MediaId**: `<input type="hidden" name="mediaId" value="83789">` - can be `name` before or after `value`
- **Format**: `<select id="dl_format">` with `<option value="0" title="JailBreak folder">JB Folder</option>` etc. When format=0, alt input is disabled (omit from URL). When format>0, append `&alt={format}` to download URL.
- **Form action**: `<form id="dl_form" action="https://dl3.vimm.net/">` - can be absolute, protocol-relative, or relative
- **Size**: regex `([\d,.]+)\s*(GB|MB|KB)` anywhere in page
- **Download**: GET `{form_action}/?mediaId={id}` (format=0) or `{form_action}/?mediaId={id}&alt={format}` (format>0)

Always `HtmlDecode` titles and platforms - Vimm's uses HTML entities.

## HttpClient Configuration

Named client `"vimms"` with CookieContainer, auto decompression, auto redirect (10 hops), full Chrome 131 headers, Referrer `https://vimm.net/`, 60-min timeout.

## Race Conditions Handled

- **Reorder during completion** - both wrapped in `lock(QueueLock.Sync)`, reorder uses SQLite transaction
- **Delete during completion** - same lock
- **App killed after download but before File.Move** - on restart, detects `existingBytes >= totalBytes` and recovers
- **App killed after File.Move but before DB update** - transaction rollback moves file back
- **App killed during PS3 extraction/conversion** - orphaned temp dirs + temp ISOs cleaned on next startup
- **Duplicate conversion enqueue** - atomic `ConcurrentDictionary.AddOrUpdate` prevents double-processing

## URL Parsing

`t.replace(/(https?:\/\/)/gi, '\n$1')` splits concatenated URLs, then regex extracts all. Deduped with `Set`. Adding URLs auto-starts the queue if not running.

## UI

- Single `index.html`, no build step, no framework
- Fonts: Inter (UI) + JetBrains Mono (data/mono)
- SignalR client from CDN
- Three-section layout: **Active** (queue + converting), **History** (completed with ISO status), **Sync** (compare + copy to external drive)
- Platform icons via CSS `mask-image`
- Format dropdown auto-selects `.dec.iso` when available
- Sync section: path input, disk info cards (source + target), compare/copy buttons
- Page title updates with progress: `51.23% — Skate 3`

## Testing

200 tests across 3 modules (87 Sync + 88 Ps3Iso + 25 Extractor). All integration tests use real file I/O. Container tests use `ghcr.io/eduvhc/vimm-dl-tools` via Testcontainers (7z + makeps3iso + patchps3iso). See `Modules/MODULE_GUIDE.md` for conventions.

## Docker

- Main app: `Dockerfile` at repo root. Three-stage build (ps3tools → .NET AOT → runtime). References `Modules/` paths.
- Tools image: `Modules/Module.Core.Testing/Dockerfile.tools`. Published to `ghcr.io/eduvhc/vimm-dl-tools:latest` via `.github/workflows/tools-image.yml`.
- Two volumes: `/app/data` (SQLite DB), `/downloads` (downloading/ + completed/ + ps3_temp/)
- Port 5000

## CI/CD

- `.github/workflows/publish.yml` - builds + pushes app Docker image on `v*` tags
- `.github/workflows/tools-image.yml` - builds + pushes tools image on `Dockerfile.tools` changes

## User Preferences

- Keep it simple. Single file. Repository abstraction for DB access.
- No EF Core, no Dapper, no heavy frameworks. Raw ADO.NET for AOT compatibility.
- Modern dark UI with teal/green accent. No terminal/hacker themes. No blue/cyan.
- 2 decimal precision on all percentages and file sizes.
- No redundant status info. No log panels. Errors only when they happen.
- Buttons must reflect actual item state.
- Linux is the target platform.
- MockServer on 5111, main app on 5031 (dev) / 5000 (Docker).
