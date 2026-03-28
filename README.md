# VIMM // DL

A self-hosted game preservation toolkit that automates downloading, converting, and organizing console ROMs from [Vimm's Lair](https://vimm.net). Built with .NET Native AOT.

> **Note:** This project respects Vimm's Lair and its mission. There are no mechanisms to bypass or circumvent the download rate limit. Vimm has kept this archive alive for nearly 30 years — the least we can do is play by the rules. Don't be the reason another preservation site goes dark like Myrient.

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
- Archive validation, multithreaded extraction, optional archive preservation
- Real-time progress, platform icons, format selection, drag-and-drop queue
- Metrics dashboard — download speed chart, disk usage, system info
- Event audit log — full event history with filters and detail view
- JSON import/export with background metadata fetch
- Feature flags (Beta/Developer) for Sync and Events tabs
- Native AOT — fast startup, small footprint, no runtime needed

### PS3 (active focus)

- JB Folder → ISO conversion (parallel pipeline, crash recovery)
- .dec.iso download + rename (default format, configurable)
- ISO filename formatting — fix "The" placement, append serial, strip region
- Per-platform settings (default format, rename rules, parallelism, archive preservation)

> The pipeline architecture (`IPipeline`) is designed for multi-console support. PS3 is the current focus — contributions for other consoles are welcome.

## Thanks

This project wouldn't exist without the people who decided that preserving games was worth the effort.

**[Vimm's Lair](https://vimm.net)** has been keeping classic games accessible since 1997 — long before anyone called it "digital preservation." This project is a love letter to that mission.

**[bucanero/ps3iso-utils](https://github.com/bucanero/ps3iso-utils)** gave us `makeps3iso` and `patchps3iso`. The entire PS3 conversion pipeline exists because of this work.

**[NullShield's VimmsDownloader](https://github.com/NullShield-Official/VimmsDownloader)** was the original Python/Selenium downloader that planted the seed. VIMM // DL is a from-scratch .NET rewrite, but the idea started there.

## License

MIT © 2026 eduvhc
