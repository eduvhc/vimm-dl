# VIMM // DL

A lightweight, queue-based download manager for [Vimm's Lair](https://vimm.net) with automatic PS3 ISO conversion, built with .NET Native AOT. No browser automation, no Selenium -- just HTTP.

## Features

- **Paste and go** -- paste one or many URLs (even concatenated like `https://...https://...`), downloads start automatically
- **Queue management** -- reorder with arrows, start any item directly, pause/resume individual downloads
- **Format selection** -- choose download format (e.g. PS3: JB Folder or .dec.iso) per item
- **Pause/Resume with HTTP Range** -- pause mid-download, restart the app, resume from where you left off
- **Auto-resume on restart** -- killed the app? rebooted? it picks up automatically on next launch
- **PS3 ISO conversion** -- automatically converts PS3 JB Folder downloads to ISO (extract zip, makeps3iso, patchps3iso)
- **Parallel conversion pipeline** -- configurable parallelism (default 3): multiple extractions and conversions run simultaneously
- **Two-section UI** -- Active (queue + converting) and History (completed with inline ISO status, file sizes, timestamps)
- **Convert All** -- one-click bulk conversion for all completed PS3 downloads, with per-file status, retry on error
- **Two-folder system** -- active downloads in `downloading/`, finished files + ISOs in `completed/`
- **Metadata** -- fetches game title, platform, and file size from vault pages, cached in SQLite
- **Platform icons** -- colored SVG icons per platform (PlayStation, Nintendo, Sega, Xbox)
- **Real-time progress** -- live percentage (2 decimal places), inline progress bar, page title updates
- **Background safe** -- downloads continue even if you close the browser tab
- **Crash recovery** -- detects fully downloaded files stuck in `downloading/` and recovers them. Resumes PS3 conversions from extraction markers. Cleans up orphaned temp files
- **Archive validation** -- quick header check catches truncated/corrupt archives before extraction
- **Race condition safe** -- queue mutations are locked and wrapped in transactions
- **Native AOT** -- fast startup, small memory footprint, self-contained binary (no .NET runtime needed)

## Stack

| | |
|---|---|
| Backend | ASP.NET Core minimal APIs (.NET 10, Native AOT) |
| Database | SQLite via raw ADO.NET (WAL mode) |
| Real-time | SignalR (JSON protocol, source-generated) |
| Downloads | HttpClient with browser-mimicking headers |
| PS3 Tools | [bucanero/ps3iso-utils](https://github.com/bucanero/ps3iso-utils) (makeps3iso, patchps3iso) |
| Extraction | 7-Zip (7z, multithreaded) |
| Frontend | Vanilla JS, single `index.html` |

## Quick Start

**Linux / macOS:**
```bash
docker run -d -p 5000:5000 -v vimm-data:/app/data -v ~/downloads:/downloads --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

**Windows (PowerShell):**
```powershell
docker run -d -p 5000:5000 -v vimm-data:/app/data -v ${HOME}\Downloads:/downloads --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

**Windows (CMD):**
```cmd
docker run -d -p 5000:5000 -v vimm-data:/app/data -v %USERPROFILE%\Downloads:/downloads --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

Open **http://localhost:5000** and paste your URLs.

## Updating

**Linux / macOS:**
```bash
docker stop vimm-dl && docker rm vimm-dl
docker pull ghcr.io/eduvhc/vimm-dl:latest
docker run -d -p 5000:5000 -v vimm-data:/app/data -v ~/downloads:/downloads --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

**Windows (PowerShell):**
```powershell
docker stop vimm-dl; docker rm vimm-dl
docker pull ghcr.io/eduvhc/vimm-dl:latest
docker run -d -p 5000:5000 -v vimm-data:/app/data -v ${HOME}\Downloads:/downloads --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

**Windows (CMD):**
```cmd
docker stop vimm-dl & docker rm vimm-dl
docker pull ghcr.io/eduvhc/vimm-dl:latest
docker run -d -p 5000:5000 -v vimm-data:/app/data -v %USERPROFILE%\Downloads:/downloads --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

Your queue, metadata cache, and partial downloads are preserved across updates. Active downloads auto-resume on restart.

The app checks for new versions on startup and shows a banner with a link to the changelog.

## Usage

1. Paste vault URLs into the input bar -- any format works:
   - One per line
   - Space/comma separated
   - Concatenated (`https://vimm.net/vault/123https://vimm.net/vault/456`)
   - Mixed with text
2. Downloads start automatically
3. For PS3 games, select format (JB Folder or .dec.iso) from the dropdown on each queue item
4. Use the per-item buttons to pause, resume, skip, reorder, or remove
5. Files download to `{path}/downloading/` and move to `{path}/completed/` when done
6. PS3 JB Folder downloads are automatically converted to ISO after completion
7. Use **Convert All** to bulk-convert all completed PS3 zips, with retry on errors
8. Close the browser -- it keeps going. Close the app -- it resumes on restart.

## PS3 ISO Conversion

PS3 games downloaded as JB Folder (format 0) are automatically converted to ISO after download:

1. Archive header validated via `7z l` (catches truncated/corrupt files fast)
2. Zip extracted via 7-Zip to a temp folder (multithreaded with `-mmt=on`)
3. Extraction marker written (`.extraction_complete`) for crash recovery
4. JB folder located (handles nested structures from Vimm's)
5. `makeps3iso` creates the ISO
6. `patchps3iso` patches firmware to 3.55
7. ISO renamed to `[Game Name] - [GAME-ID].iso` and placed in `completed/`
8. Original zip is preserved, temp files cleaned up asynchronously

The pipeline runs N parallel workers per phase (default 3). While 3 games extract, 3 others can convert simultaneously. Configure with `Ps3ConvertParallelism` env var.

If the container is killed mid-conversion:
- **After extraction but before conversion** -- the extraction marker is detected on restart, and conversion resumes without re-extracting
- **During extraction** -- the incomplete temp dir is cleaned up and extraction restarts from the archive
- **Orphaned temp ISOs** -- automatically cleaned on startup

## Configuration

### Docker

| Env var | Default | Description |
|---------|---------|-------------|
| `DownloadPath` | `/downloads` | Where files are saved |
| `ConnectionStrings__Default` | `Data Source=/app/data/queue.db` | SQLite database path |
| `ASPNETCORE_URLS` | `http://+:5000` | Listen address |
| `Ps3ConvertParallelism` | `3` | Max concurrent extractions/conversions |

| Volume | Purpose |
|--------|---------|
| `/app/data` | SQLite database (queue state, metadata cache) |
| `/downloads` | Download files (`downloading/` + `completed/` + `ps3_temp/`) |

Use a **named volume** for `/app/data` (SQLite WAL + bind mounts can conflict on some hosts). Use a **bind mount** for `/downloads` so you can access files from the host.

### From Source

Edit `appsettings.json`:

```json
{
  "DownloadPath": "",
  "Ps3ConvertParallelism": 3
}
```

Leave `DownloadPath` empty to default to `~/Downloads`. Three subfolders are created automatically:
- `downloading/` -- active/partial downloads
- `completed/` -- finished files and ISOs
- `ps3_temp/` -- temporary extraction space (auto-cleaned)

## Docker Compose

```yaml
services:
  vimm-dl:
    image: ghcr.io/eduvhc/vimm-dl:latest
    ports:
      - "5000:5000"
    volumes:
      - vimm-data:/app/data
      - ./downloads:/downloads
    restart: unless-stopped

volumes:
  vimm-data:
```

## Project Structure

```
VimmsDownloader/
  Program.cs              # App startup, DI, middleware
  Models.cs               # Record types, PathHelpers
  AppJsonContext.cs        # JSON source generator (AOT)
  QueueRepository.cs      # SQLite data access (raw ADO.NET)
  DownloadHub.cs           # SignalR hub
  DownloadQueue.cs         # Background download service
  Ps3ConversionPipeline.cs # Two-phase extract + convert pipeline
  Endpoints/
    FileEndpoints.cs       # /api/data, /api/partials
    DownloadEndpoints.cs   # Queue CRUD, /api/status
    MetadataEndpoints.cs   # /api/meta, /api/version
    ConfigEndpoints.cs     # /api/config, check-path
    Ps3Endpoints.cs        # /api/convert-ps3/*
  wwwroot/
    index.html             # Full UI (vanilla JS)
    icons/                 # Platform SVG icons

Ps3IsoTools/
  Ps3IsoConverter.cs       # JB folder -> ISO (wraps makeps3iso/patchps3iso)
  ParamSfo.cs              # PARAM.SFO parser (game title + ID)

ZipExtractor/
  ZipExtract.cs            # 7z wrapper (extract + header check)

MockServer/
  Program.cs               # Fake Vimm's Lair for testing

Dockerfile                 # Three-stage: ps3 tools (Alpine) -> .NET AOT build -> runtime
.github/workflows/
  publish.yml              # CI: Docker image to ghcr.io
```

## Mock Server

Test without hitting the real site:

```bash
cd MockServer
dotnet run
```

Browse `http://localhost:5111` for a list of fake games. Paste URLs like `http://localhost:5111/vault/1001` into the downloader.

## Adding Platform Icons

1. Place SVG files in `wwwroot/icons/` with `fill="currentColor"` in the SVG tag
2. Add the mapping in `index.html` (`platformIcons` object)

Colors are applied automatically via CSS mask:
- PlayStation -- blue
- Nintendo -- red
- Sega -- blue
- Xbox -- green

## How It Works Under the Hood

1. URLs are parsed from input using regex (`https?://` boundaries)
2. Each vault page is fetched to extract the `mediaId` from a hidden form field
3. The download server URL is resolved from the form action (handles absolute, protocol-relative, and relative URLs)
4. Files are streamed via `HttpClient` with full Chrome headers (User-Agent, Sec-CH-UA, cookies, etc.)
5. On pause/kill, partial files stay on disk; on resume, an HTTP `Range` header continues from the last byte
6. On completion, file is moved atomically from `downloading/` to `completed/` inside a DB transaction
7. PS3 JB Folder zips are enqueued to the conversion pipeline (non-blocking, next download starts immediately)
8. Random 5-30s delay between downloads to be respectful to the site

## API Reference

<details>
<summary>REST Endpoints</summary>

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/data` | Queue + enriched history (metadata, ISO status, file sizes) |
| POST | `/api/queue` | Add URLs `{"urls":["..."],"format":0}` |
| DELETE | `/api/queue/{id}` | Remove item |
| DELETE | `/api/queue` | Clear queue |
| POST | `/api/queue/move` | Reorder `{"id":1,"direction":"up"}` |
| POST | `/api/queue/format` | Set format `{"id":1,"format":1}` |
| GET | `/api/meta?url=` | Fetch/cache vault metadata (includes format options) |
| GET | `/api/config` | Server info + download state |
| POST | `/api/config/check-path` | Test write permissions |
| GET | `/api/partials` | List files in downloading folder |
| GET | `/api/status` | Running/paused state + recent logs |
| POST | `/api/convert-ps3` | Queue all completed PS3 zips for ISO conversion |
| POST | `/api/convert-ps3/single` | Queue single file for conversion |
| POST | `/api/convert-ps3/abort` | Abort active conversion |
| POST | `/api/convert-ps3/mark-done` | Mark as already converted |
| GET | `/api/version` | Current + latest version info |

</details>

<details>
<summary>SignalR Hub (/hub)</summary>

| Method | Direction | Description |
|--------|-----------|-------------|
| `StartDownload(path)` | Client > Server | Start/resume queue |
| `StartSpecific(path, id)` | Client > Server | Download specific item |
| `PauseDownload()` | Client > Server | Pause current download |
| `StopDownload()` | Client > Server | Stop and reset |
| `Status` | Server > Client | Status messages |
| `Progress` | Server > Client | Progress string |
| `Completed` | Server > Client | File done `{url, filename, filepath}` |
| `ConvertStatus` | Server > Client | PS3 conversion update `{zipName, phase, message, isoFilename}` |
| `Error` | Server > Client | Error message |
| `Done` | Server > Client | Queue finished |

</details>

## Acknowledgements ❤️

This project exists because of the incredible work of others:

- **[Vimm's Lair](https://vimm.net)** ❤️ -- Preserving classic video game history since 1997. A digital museum for the games we grew up with, kept free and accessible for everyone. This project is a love letter to that mission.

- **[bucanero/ps3iso-utils](https://github.com/bucanero/ps3iso-utils)** ❤️ -- The `makeps3iso` and `patchps3iso` tools that make PS3 ISO conversion possible. Without these, the entire conversion pipeline wouldn't exist.

- **[NullShield's VimmsDownloader](https://github.com/NullShield-Official/VimmsDownloader)** ❤️ -- The original Python/Selenium downloader that inspired this project. VIMM // DL is a from-scratch .NET rewrite using plain HTTP, but the idea started there.

Please respect Vimm's Lair -- don't abuse download limits and keep single sessions.

## License

MIT
