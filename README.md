# VIMM // DL

A lightweight, queue-based download manager for [Vimm's Lair](https://vimm.net), built with .NET minimal APIs. No browser automation, no Selenium -- just HTTP.

## Features

- **Paste and go** -- paste one or many URLs (even concatenated like `https://...https://...`), downloads start automatically
- **Queue management** -- reorder with arrows, start any item directly, pause/resume individual downloads
- **Pause/Resume with HTTP Range** -- pause mid-download, restart the app, resume from where you left off
- **Auto-resume on restart** -- killed the app? rebooted? it picks up automatically on next launch
- **Two-folder system** -- active downloads in `downloading/`, finished files moved to `completed/`
- **Metadata** -- fetches game title, platform, and file size from vault pages, cached in SQLite
- **Platform icons** -- colored SVG icons per platform (PlayStation, Nintendo, Sega, Xbox)
- **Real-time progress** -- live percentage (2 decimal places), inline progress bar, page title updates
- **Background safe** -- downloads continue even if you close the browser tab
- **Crash recovery** -- detects fully downloaded files stuck in `downloading/` and recovers them
- **Race condition safe** -- queue mutations are locked and wrapped in transactions

## Stack

| | |
|---|---|
| Backend | ASP.NET Core minimal APIs (.NET 10) |
| Database | SQLite + Dapper (WAL mode) |
| Real-time | SignalR |
| Downloads | HttpClient with browser-mimicking headers |
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

## Usage

1. Paste vault URLs into the input bar -- any format works:
   - One per line
   - Space/comma separated
   - Concatenated (`https://vimm.net/vault/123https://vimm.net/vault/456`)
   - Mixed with text
2. Downloads start automatically
3. Use the per-item buttons to pause, resume, skip, reorder, or remove
4. Files download to `{path}/downloading/` and move to `{path}/completed/` when done
5. Close the browser -- it keeps going. Close the app -- it resumes on restart.

## Configuration

### Docker

| Env var | Default | Description |
|---------|---------|-------------|
| `DownloadPath` | `/downloads` | Where files are saved |
| `ConnectionStrings__Default` | `Data Source=/app/data/queue.db` | SQLite database path |
| `ASPNETCORE_URLS` | `http://+:5000` | Listen address |

| Volume | Purpose |
|--------|---------|
| `/app/data` | SQLite database (queue state, metadata cache) |
| `/downloads` | Download files (`downloading/` + `completed/` subfolders) |

Use a **named volume** for `/app/data` (SQLite WAL + bind mounts can conflict on some hosts). Use a **bind mount** for `/downloads` so you can access files from the host.

### From Source

Edit `appsettings.json`:

```json
{
  "DownloadPath": ""
}
```

Leave empty to default to `~/Downloads`. Two subfolders are created automatically:
- `downloading/` -- active/partial downloads
- `completed/` -- finished files

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
  Program.cs              # APIs, SignalR hub, download service (single file)
  wwwroot/
    index.html            # Full UI
    icons/
      playstation3.svg    # Platform icons (add more as needed)
  appsettings.json
  queue.db                # Auto-created SQLite database
  Dockerfile

MockServer/
  Program.cs              # Fake Vimm's Lair for testing

.github/
  workflows/
    publish.yml           # CI: Docker image to ghcr.io
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
7. Random 5-30s delay between downloads to be respectful to the site

## API Reference

<details>
<summary>REST Endpoints</summary>

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/data` | Queue + completed lists with joined metadata |
| POST | `/api/queue` | Add URLs `{"urls":["..."]}` |
| DELETE | `/api/queue/{id}` | Remove item |
| DELETE | `/api/queue` | Clear queue |
| POST | `/api/queue/move` | Reorder `{"id":1,"direction":"up"}` |
| GET | `/api/meta?url=` | Fetch/cache vault metadata |
| GET | `/api/config` | Server info + download state |
| POST | `/api/config/check-path` | Test write permissions |
| GET | `/api/partials` | List files in downloading folder |
| GET | `/api/status` | Running/paused state + recent logs |
| GET | `/api/check-exists?filename=` | Check completed folder |
| GET | `/api/browse?path=` | Browse directories |

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
| `Completed` | Server > Client | File done (url, filename, filepath) |
| `Error` | Server > Client | Error message |
| `Done` | Server > Client | Queue finished |

</details>

## ❤️ A Note on Donations

Vimm's Lair unfortunately does not accept donations. So you don't need to donate -- just enjoy.

Vimm's Lair has been preserving classic video game history since 1997. Its mission is to keep retro games accessible for everyone -- a digital museum for the games we grew up with. This project exists to make that experience a little smoother.

Made with ❤️ for the community.

## License

MIT

## Credits

Inspired by [NullShield's VimmsDownloader](https://github.com/NullShield-Official/VimmsDownloader) (Python/Selenium). This is a from-scratch .NET rewrite using plain HTTP instead of browser automation.

Please respect Vimm's Lair -- don't abuse download limits and keep single sessions.
