# VIMM // DL

A lightweight, queue-based download manager for [Vimm's Lair](https://vimm.net) with automatic PS3 ISO conversion, built with .NET Native AOT.

## Quick Start

```bash
docker run -d -p 5000:5000 \
  -v vimm-data:/app/data \
  -v ~/downloads:/downloads \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

Open **http://localhost:5000**, paste vault URLs, done.

## Volumes

| Volume | Type | Required | Purpose |
|--------|------|----------|---------|
| `/app/data` | Named volume | Yes | SQLite database (queue, metadata, events, settings). Use a named volume — bind mounts can conflict with SQLite WAL on some hosts |
| `/downloads` | Bind mount | Yes | All files: `downloading/` (partial), `completed/` (archives + ISOs), `ps3_temp/` (auto-cleaned). Download path auto-detected when this volume is mounted |
| `/sync-target` | Bind mount | No | External drive for Sync feature. Mount your USB/NAS path here, then set sync path to `/sync-target` in Settings |

> **Note:** Sync is in beta and needs more testing. Enable it in Settings under Feature Flags.

**Examples:**

```bash
# Linux / macOS
docker run -d -p 5000:5000 \
  -v vimm-data:/app/data \
  -v ~/Downloads:/downloads \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest

# With sync to external drive
docker run -d -p 5000:5000 \
  -v vimm-data:/app/data \
  -v ~/Downloads:/downloads \
  -v /mnt/usb/PS3ISO:/sync-target \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest

# Windows
docker run -d -p 5000:5000 \
  -v vimm-data:/app/data \
  -v %USERPROFILE%\Downloads:/downloads \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

## Features

- Paste URLs — downloads start automatically with format fallback
- Pause/resume with HTTP Range, auto-resume on restart
- PS3 JB Folder → ISO conversion (parallel pipeline, crash recovery)
- PS3 .dec.iso download + rename (default format, configurable)
- Archive validation, multithreaded extraction, optional archive preservation
- Real-time progress, platform icons, format selection, drag-and-drop queue
- Metrics dashboard — download speed chart, disk usage, system info
- Event audit log — full event history with filters and detail view
- JSON import/export with background metadata fetch
- Feature flags (Beta/Developer) for Sync and Events tabs
- Per-platform settings (PS3 default format, rename rules, parallelism)
- Native AOT — fast startup, small footprint, no runtime needed

## Thanks ❤️

- **[Vimm's Lair](https://vimm.net)** ❤️ — Preserving classic video game history since 1997. A digital museum for the games we grew up with, kept free and accessible for everyone. This project is a love letter to that mission.

- **[bucanero/ps3iso-utils](https://github.com/bucanero/ps3iso-utils)** ❤️ — The `makeps3iso` and `patchps3iso` tools that make PS3 ISO conversion possible. Without these, the entire conversion pipeline wouldn't exist.

- **[NullShield's VimmsDownloader](https://github.com/NullShield-Official/VimmsDownloader)** ❤️ — The original Python/Selenium downloader that inspired this project. VIMM // DL is a from-scratch .NET rewrite using plain HTTP, but the idea started there.

Please respect Vimm's Lair — don't abuse download limits and keep single sessions.

## License

MIT
