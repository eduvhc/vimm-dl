# CLAUDE.md

## Project Overview

Vimm's Lair download queue manager. Single .NET project, everything in `Program.cs`, UI in `wwwroot/index.html`. Mock server in `MockServer/` for testing. GitHub user: eduvhc. Target platform: Linux (Docker + bare metal).

## Architecture

- **Single file backend** - `Program.cs` contains minimal APIs, SignalR hub (`DownloadHub`), and the download service (`DownloadQueue`). No separate folders/layers.
- **DownloadQueue is a singleton** registered via DI. Uses `IHubContext<DownloadHub>` to broadcast to all clients (decoupled from any specific SignalR connection). Downloads survive browser close.
- **SQLite via Dapper** - no EF Core. Raw SQL. `Db.Open()` helper with configurable connection string. WAL mode enabled.
- **QueueLock.Sync** - static lock object used by queue mutations (move, delete, complete) to prevent race conditions. Both the move API and download completion use this lock. All multi-step DB ops wrapped in transactions.
- **Two-folder pattern** - `{path}/downloading/` for active, `{path}/completed/` for done. Auto-created on queue start. File.Move on completion is inside the lock + transaction.

## Database

SQLite, file `queue.db` in working directory (or `/app/data/queue.db` in Docker via `ConnectionStrings__Default`). Three tables:
- `queued_urls` (id INTEGER PK AUTOINCREMENT, url TEXT) - download queue, ordered by id
- `completed_urls` (id INTEGER PK AUTOINCREMENT, url TEXT, filename TEXT, filepath TEXT) - finished downloads with full path
- `url_meta` (url TEXT PK, title TEXT, platform TEXT, size TEXT) - metadata cache to avoid re-fetching

WAL mode set on init. No migrations - tables created with IF NOT EXISTS. DB path configurable via `ConnectionStrings:Default` in config/env.

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
8. Random 5-30s delay, then next item

## Vimm's Lair HTML Structure

- **Title**: `<title>The Vault: Game Name (System)</title>` - strip "The Vault: " prefix, remove trailing `(System)`
- **Platform**: `<div class="sectionTitle">PlayStation 3</div>` (NOT `<h2>`)
- **MediaId**: `<input type="hidden" name="mediaId" value="83789">` - can be `name` before or after `value`
- **Form action**: `<form id="dl_form" action="https://dl3.vimm.net/">` - can be absolute, protocol-relative, or relative
- **Size**: regex `([\d,.]+)\s*(GB|MB|KB)` anywhere in page (appears near download button)
- **Download**: GET `{form_action}/?mediaId={id}&alt=0` - the `submitDL()` JS switches form from POST to GET

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
- No folder picker / file browser - user types path directly, hits Check
- Platform icons use CSS `mask-image`: SVG has `fill="currentColor"`, div with mask + colored background (blue=PlayStation, red=Nintendo, green=Xbox, blue=Sega)
- uTorrent-style: active download shown inline on queue item, no separate card
- Per-item buttons change based on state: play (idle), pause (downloading, CSS pseudo-element bars), resume (paused, green glow)
- Up/down arrows for reorder, X to remove
- Completed list shows filepath, checks if file still exists on disk (teal = exists, red = removed)
- Page title updates with progress: `51.23% — Skate 3`
- `renderQueue()` called on every progress update to refresh inline state

## Docker

- Dockerfile: multi-stage build, aspnet:10.0 runtime
- Two volumes: `/app/data` (SQLite DB), `/downloads` (downloading/ + completed/)
- DB path set via env `ConnectionStrings__Default="Data Source=/app/data/queue.db"`
- Download path via env `DownloadPath=/downloads`
- Port 5000
- Use named volume for `/app/data` (SQLite WAL + bind mounts on Windows/WSL2 can conflict)
- Use bind mount for `/downloads` so host can access files
- `.dockerignore` excludes bin/obj/db/.git

## CI/CD

GitHub Actions (`.github/workflows/publish.yml`):
- Triggers on `v*` tags or manual dispatch
- Builds linux-x64 + linux-arm64 self-contained single-file trimmed binaries
- Builds + pushes Docker image to ghcr.io/eduvhc/vimm-dl
- Creates GitHub Release with .tar.gz artifacts

## User Preferences

- Keep it simple. Single file. No layers/abstractions.
- No EF Core, no heavy frameworks.
- Modern dark UI with teal/green accent. No terminal/hacker themes. No blue/cyan.
- 2 decimal precision on all percentages and file sizes.
- No redundant status info. No log panels. Errors only when they happen.
- Buttons must reflect actual item state.
- Linux is the target platform.
- MockServer on 5111, main app on 5031 (dev) / 5000 (Docker).
