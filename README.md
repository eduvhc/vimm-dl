# VIMM // DL

A self-hosted game preservation toolkit that automates downloading, converting, and organizing console ROMs from [Vimm's Lair](https://vimm.net). Built with .NET Native AOT.

> We owe a lot to Vimm’s Lair for keeping the history of gaming accessible for nearly 30 years. To honor that legacy, this project does not—and will not—provide ways to bypass their download limits. Following the rules is a small price to pay to keep these archives online. Let’s learn from the loss of sites like Myrient and show our gratitude to Vimm by being responsible users.

## Quick Start

```bash
docker run -d -p 5000:5000 \
  -v ~/vimm:/vimms \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

Open **http://localhost:5000**, paste vault URLs, done.

## Volume

Everything lives under a single `/vimms` mount:

```
/vimms/
├── data/          ← SQLite database (queue, metadata, events, settings)
└── downloads/     ← All files: downloading/, completed/, ps3_temp/
```

| Path | Purpose |
|------|---------|
| `/vimms/data/` | Database — persists queue, history, settings, events |
| `/vimms/downloads/` | Files — partial downloads, archives, ISOs, temp conversion |

> **Important:** Without the volume mount, everything is lost on container update. See [UPDATE.md](UPDATE.md).

**Examples:**

```bash
# Linux / macOS
docker run -d -p 5000:5000 \
  -v ~/vimm:/vimms \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest

# With sync to external drive
docker run -d -p 5000:5000 \
  -v ~/vimm:/vimms \
  -v /mnt/usb/PS3ISO:/sync-target \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest

# Windows
docker run -d -p 5000:5000 \
  -v %USERPROFILE%\vimm:/vimms \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

> **Note:** Sync is in beta. Enable it in Settings under Feature Flags. The target drive must be bind-mounted (e.g., `/sync-target`).

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
