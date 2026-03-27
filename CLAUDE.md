# CLAUDE.md

## Project Overview

Vimm's Lair download queue manager with PS3 ISO conversion. Main project split into SRP files under `VimmsDownloader/`, UI in `wwwroot/index.html`. Two class libraries: `Ps3IsoTools` (JB→ISO conversion) and `ZipExtractor` (7z wrapper). Mock server in `MockServer/` for testing. GitHub user: eduvhc. Target platform: Linux (Docker + bare metal).

## Architecture

- **SRP file structure** - `Program.cs` (startup/DI only), `Models.cs` (records + PathHelpers), `AppJsonContext.cs` (JSON source gen), `QueueRepository.cs`, `DownloadHub.cs`, `DownloadQueue.cs`, `Ps3ConversionPipeline.cs`, and `Endpoints/` folder with 5 endpoint files (`FileEndpoints`, `DownloadEndpoints`, `MetadataEndpoints`, `ConfigEndpoints`, `Ps3Endpoints`).
- **AOT-ready** - `PublishAot=true`, no Dapper (raw ADO.NET via `SqliteCommand`/`SqliteDataReader`), JSON source generator (`AppJsonContext`), all API responses use named record types. SignalR configured with `AddJsonProtocol` using same source-generated context. Both class libraries marked `IsAotCompatible`.
- **QueueRepository** - singleton, encapsulates all SQLite operations. Uses `$param` style parameters. `Init()` handles DB creation, WAL mode, idempotent migrations (`CREATE TABLE IF NOT EXISTS` + `try/catch ALTER TABLE ADD COLUMN`).
- **DownloadQueue** - singleton registered via DI. Uses `IHubContext<DownloadHub>` to broadcast to all clients (decoupled from any specific SignalR connection). Downloads survive browser close. After PS3 JB Folder downloads complete, enqueues to `Ps3ConversionPipeline` (non-blocking).
- **Ps3ConversionPipeline** - singleton, two-phase pipeline with configurable parallelism (`Ps3ConvertParallelism`, default 3). Uses two `Channel<T>` queues: extraction (7z) → conversion (makeps3iso). N extract workers + N convert workers run concurrently, competing for their respective channel. `ConcurrentDictionary` tracks per-file status and prevents double-queueing active items via atomic `AddOrUpdate`.
- **QueueLock.Sync** - static lock object used by queue mutations (move, delete, complete) to prevent race conditions. Both the move API and download completion use this lock. All multi-step DB ops wrapped in transactions.
- **Three-folder pattern** - `{path}/downloading/` for active downloads, `{path}/completed/` for done files + ISOs, `{path}/ps3_temp/{guid}/` for extraction scratch space. Auto-created as needed.

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
5. Checks `downloading/` for existing partial file:
   - `existingBytes >= totalBytes` → file already complete, skip download, move to completed (crash recovery)
   - `existingBytes > 0 && < totalBytes` → send Range header, resume
   - `existingBytes == 0` → fresh download
6. Streams to FileStream with 80KB buffer, reports progress every 2 seconds
7. On completion: lock → File.Move to completed → BEGIN TRANSACTION → DELETE from queue + INSERT into completed → COMMIT (rollback moves file back on failure)
8. If PS3 JB Folder (format=0, platform="PlayStation 3"): enqueues zip to `Ps3ConversionPipeline` (non-blocking)
9. Random 5-30s delay, then next item

## PS3 ISO Conversion Pipeline

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

**Crash recovery** (runs on startup via `CleanupOrphans`):
- Checks each `ps3_temp/` subdir for `.extraction_complete` marker
- If marker found → extraction was complete, enqueues directly to convert queue (skips re-extraction)
- If no marker → orphaned incomplete extraction, deletes the dir
- Deletes `temp_*.iso` files in `completed/` (orphaned partial ISOs)

**Edge cases handled**:
- Archive corrupted/truncated → `7z l` header check fails fast before extraction starts
- Zip deleted between queue and extract → `File.Exists` check, error status
- Disk full → pre-check: zip size * 3 vs available space, skips with error
- Same zip queued twice → atomic `AddOrUpdate` blocks active items (queued/extracting/converting)
- Non-PS3 zip → `FindJbFolder` returns null, marked "skipped"
- Done/error/skipped items can be re-queued (only active phases blocked)

**UI section** (between Queue and Completed, hidden when empty):
- Color-coded phase badges: queued (gray), extracting (amber), converting (blue), done (green), error (red), skipped (gray)
- Retry button on error/skipped items
- Dismiss button on done/error/skipped items
- "Convert All" button triggers `POST /api/convert-ps3` (scans DB for PS3 completed zips)
- "Clear Done" button removes finished items from list
- Real-time updates via `ConvertStatus` SignalR event
- Status restored on connect/reconnect via `GET /api/convert-ps3/status`

## Vimm's Lair HTML Structure

- **Title**: `<title>The Vault: Game Name (System)</title>` - strip "The Vault: " prefix, remove trailing `(System)`
- **Platform**: `<div class="sectionTitle">PlayStation 3</div>` (NOT `<h2>`)
- **MediaId**: `<input type="hidden" name="mediaId" value="83789">` - can be `name` before or after `value`
- **Format**: `<select id="dl_format">` with `<option value="0" title="JailBreak folder">JB Folder</option>` etc. When format=0, alt input is disabled (omit from URL). When format>0, append `&alt={format}` to download URL. Format options only exist on certain platforms (e.g. PS3).
- **Form action**: `<form id="dl_form" action="https://dl3.vimm.net/">` - can be absolute, protocol-relative, or relative
- **Size**: regex `([\d,.]+)\s*(GB|MB|KB)` anywhere in page (appears near download button)
- **Download**: GET `{form_action}/?mediaId={id}` (format=0, no alt) or `{form_action}/?mediaId={id}&alt={format}` (format>0) - the `submitDL()` JS switches form from POST to GET

Always `HtmlDecode` titles and platforms - Vimm's uses HTML entities (`&#039;` for apostrophes).

## HttpClient Configuration

Named client `"vimms"` with:
- CookieContainer (persists session cookies)
- Auto decompression (gzip/deflate/brotli)
- Auto redirect (up to 10 hops)
- Full Chrome 131 headers: User-Agent, Accept, Accept-Language, Accept-Encoding, Cache-Control, Pragma, Sec-CH-UA, Sec-CH-UA-Mobile, Sec-CH-UA-Platform, Sec-Fetch-Dest/Mode/Site/User, Upgrade-Insecure-Requests, DNT
- Referrer set to `https://vimm.net/`
- Download requests override Referrer to the vault page URL and set `Sec-Fetch-Site: cross-site`
- Timeout: 60 minutes

## Race Conditions Handled

- **Reorder during completion** - both wrapped in `lock(QueueLock.Sync)`, reorder uses SQLite transaction for 3-step ID swap
- **Delete during completion** - same lock
- **App killed after download but before File.Move** - on restart, detects `existingBytes >= totalBytes` and recovers immediately
- **App killed after File.Move but before DB update** - transaction rollback moves file back to downloading/. On restart, resumes normally.
- **App killed mid-reorder** - transaction rolls back, IDs stay consistent
- **App killed during PS3 extraction/conversion** - orphaned temp dirs + temp ISOs cleaned on next startup
- **Duplicate conversion enqueue** - atomic `ConcurrentDictionary.AddOrUpdate` prevents double-processing

## URL Parsing

Input parsing: `t.replace(/(https?:\/\/)/gi, '\n$1')` splits concatenated URLs, then `t.match(/https?:\/\/[^\s,;|"'<>]+/gi)` extracts all. Deduped with `Set`. Handles:
- One per line
- Space/comma/semicolon separated
- Concatenated without separator (`https://...https://...`)
- Mixed with arbitrary text

Adding URLs auto-starts the queue if not already running.

## UI

- Single `index.html`, no build step, no framework
- Fonts: Inter (UI) + JetBrains Mono (data/mono)
- SignalR client from CDN
- Two-section layout: **Active** (queue items + extracting/converting) and **History** (completed with inline ISO status)
- Platform icons use CSS `mask-image`: SVG has `fill="currentColor"`, div with mask + colored background (blue=PlayStation, red=Nintendo, green=Xbox, blue=Sega)
- Active items: download shown inline with progress bar, per-item buttons (play/pause/resume/reorder/remove)
- History items show: game title, platform, archive row (filename + size + exists check), ISO row (PS3 only: filename + size or conversion status), completion timestamp
- Format dropdown on queue items with multiple formats (e.g. PS3: JB Folder / .dec.iso)
- Convert All button for bulk PS3 ISO conversion
- Page title updates with progress: `51.23% — Skate 3`
- `loadData()` debounced and fetches parallelized with `Promise.all`
- `prefers-reduced-motion` respected, tabular numerics on all data columns
- `:focus-visible` on all interactive elements

## Ps3IsoTools (class library)

Converts PS3 JB folders to ISO. Wraps the C tools `makeps3iso` and `patchps3iso` from [bucanero/ps3iso-utils](https://github.com/bucanero/ps3iso-utils). Marked `IsAotCompatible`. Two files:

- **ParamSfo.cs** - parses PS3 `PARAM.SFO` binary format. Extracts `TITLE` and `TITLE_ID`. Format: header (0x14 bytes) → index table (16 bytes per entry with key offset, data len, data offset) → key table (null-terminated strings) → data table (values). Title ID formatted as `XXXX-XXXXX` (dash inserted after 4 chars if missing).
- **Ps3IsoConverter.cs** - orchestrates folder → ISO conversion:
  1. Parses PARAM.SFO for game name + ID
  2. Runs `makeps3iso -p0 <folder> <temp.iso>` (PS3_UPDATE excluded by C tool's `NOPS3_UPDATE` define)
  3. Runs `patchps3iso -p0 <temp.iso> <version>` (patches firmware only if game requires higher version)
  4. Renames to `[Game-Name] - [Game-ID].iso`
  5. Cleans up temp ISO on failure

**ConversionOptions** defaults (matching PS3 ISO Tools V2.2 screenshot):
- `PatchFirmware=true`, `FirmwareVersion="3.55"`, `SplitForFat32=false`, `RenameToGameNameId=true`, `DeleteSourceAfter=false`
- `Makeps3isoPath="makeps3iso"`, `Patchps3isoPath="patchps3iso"` (in PATH inside Docker)

**FindJbFolder(root)** - static helper to locate JB folder in an extracted directory. Checks root, one level deep, and two levels deep for `PS3_GAME/PARAM.SFO`. Handles Vimm's nested zip structure (e.g. `GameName/GameName/PS3_GAME/`).

## ZipExtractor (class library)

Single static method: `ZipExtract.ExtractAsync(zipPath, outputDir, ct)`. Shells out to `7z` (`7z x <zip> -o<dir> -y`). Returns `(bool Success, string? Error)`. Requires `7z` in PATH (installed in Docker via `apt-get install 7zip`). Marked `IsAotCompatible`.

## Docker

- Single `Dockerfile` at repo root. Three-stage build:
  1. **ps3tools** (Alpine) - clones `bucanero/ps3iso-utils`, compiles `makeps3iso` + `patchps3iso` with `gcc -static`. Cached unless Dockerfile changes.
  2. **build** (`sdk:10.0-noble-aot`) - restores and publishes all projects (VimmsDownloader + Ps3IsoTools + ZipExtractor) as AOT linux-x64.
  3. **runtime** (`runtime-deps:10.0-noble`) - installs `7zip`, copies static ps3 tool binaries + .NET app. Non-chiseled image because we need to exec external binaries (7z, makeps3iso, patchps3iso).
- Two volumes: `/app/data` (SQLite DB), `/downloads` (downloading/ + completed/ + ps3_temp/)
- DB path set via env `ConnectionStrings__Default="Data Source=/app/data/queue.db"`
- Download path via env `DownloadPath=/downloads`
- Port 5000
- Use named volume for `/app/data` (SQLite WAL + bind mounts on Windows/WSL2 can conflict)
- Use bind mount for `/downloads` so host can access files
- `.dockerignore` excludes bin/obj/db/.git

## CI/CD

GitHub Actions (`.github/workflows/publish.yml`):
- Triggers on `v*` tags or manual dispatch
- Builds + pushes Native AOT Docker image to ghcr.io/eduvhc/vimm-dl

## User Preferences

- Keep it simple. Single file. Repository abstraction for DB access.
- No EF Core, no Dapper, no heavy frameworks. Raw ADO.NET for AOT compatibility.
- Modern dark UI with teal/green accent. No terminal/hacker themes. No blue/cyan.
- 2 decimal precision on all percentages and file sizes.
- No redundant status info. No log panels. Errors only when they happen.
- Buttons must reflect actual item state.
- Linux is the target platform.
- MockServer on 5111, main app on 5031 (dev) / 5000 (Docker).
