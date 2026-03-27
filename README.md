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

| Volume | Type | Purpose |
|--------|------|---------|
| `/app/data` | Named volume | SQLite database (queue, metadata cache). Use a named volume — bind mounts can conflict with SQLite WAL on some hosts |
| `/downloads` | Bind mount | Your files. Contains `downloading/`, `completed/` (archives + ISOs), and `ps3_temp/` (auto-cleaned) |

**Bind mount examples for `/downloads`:**

```bash
# Linux / macOS
-v ~/Downloads:/downloads

# Windows (PowerShell)
-v ${HOME}\Downloads:/downloads

# Windows (CMD)
-v %USERPROFILE%\Downloads:/downloads

# Custom path
-v /mnt/games:/downloads
```

## Features

- Paste URLs in any format — downloads start automatically
- Pause/resume with HTTP Range, auto-resume on restart
- PS3 JB Folder → ISO conversion (parallel pipeline, crash recovery)
- Archive validation, multithreaded extraction
- Real-time progress, platform icons, format selection
- Native AOT — fast startup, small footprint, no runtime needed

## Thanks ❤️

- **[Vimm's Lair](https://vimm.net)** ❤️ — Preserving classic video game history since 1997. A digital museum for the games we grew up with, kept free and accessible for everyone. This project is a love letter to that mission.

- **[bucanero/ps3iso-utils](https://github.com/bucanero/ps3iso-utils)** ❤️ — The `makeps3iso` and `patchps3iso` tools that make PS3 ISO conversion possible. Without these, the entire conversion pipeline wouldn't exist.

- **[NullShield's VimmsDownloader](https://github.com/NullShield-Official/VimmsDownloader)** ❤️ — The original Python/Selenium downloader that inspired this project. VIMM // DL is a from-scratch .NET rewrite using plain HTTP, but the idea started there.

Please respect Vimm's Lair — don't abuse download limits and keep single sessions.

## License

MIT
