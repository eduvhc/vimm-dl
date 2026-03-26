# Changelog

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
