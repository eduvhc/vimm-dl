# Roadmap

Planned features and architectural improvements. Each item describes the problem, the solution, and what it unblocks.

---

## Pipeline Identity: Vault URL + Format

**Status:** Next up

### Problem

The pipeline uses the **filename** as the item identity everywhere:
- `PipelineState.Statuses` dictionary key
- `PipelineStatusEvent.ItemName`
- `events.item_name` column
- `completed_urls.filename` for conversion state tracking
- Duplicate detection matches by filename

This breaks in several real scenarios:

1. **Same game, two formats.** Downloading Uncharted as `.dec.iso` (format 1) and as JB Folder `.7z` (format 0) produces different filenames. The system treats them as completely unrelated — no duplicate warning, no shared event history, no way to see they're the same vault item.

2. **Filename collisions.** Two different games could produce archives with similar names. The pipeline would confuse their status events.

3. **Event correlation across retries.** If a user downloads, deletes, and re-downloads the same game, events from all attempts share the same `item_name` but have no vault-level grouping.

4. **Cross-format duplicate detection.** The current duplicate check matches by URL only. If the user downloads `vault/12345` as format 0, then tries format 1, the URL matches but the system can't tell the user "you already have this game in a different format."

### Solution

Replace filename with **vault URL + format** as the pipeline's item identity.

- The vault URL (`https://vimm.net/vault/12345`) uniquely identifies the game
- The format (0, 1, ...) identifies which pipeline path it takes
- Together they form a composite key: one game can have multiple pipeline runs (one per format)
- The filename remains for display and filesystem operations — it just stops being the identity key

### What changes

| Area | Current | After |
|------|---------|-------|
| `PipelineState.Statuses` key | filename | vault URL or vault ID + format |
| `PipelineStatusEvent.ItemName` | filename | vault URL or composite key |
| `events.item_name` | filename | vault URL or composite key |
| `completed_urls` tracking | matched by filename | matched by URL + format |
| Duplicate detection | URL match only | URL match + cross-format awareness |
| Frontend event grouping | filter by filename | filter by vault item, distinguish formats |

### What it unblocks

- Cross-format duplicate detection ("you already have this as JB Folder")
- Accurate event timeline per vault item per format
- Foundation for multi-format download support (download both formats of the same game)
- Cleaner correlation ID grouping (correlation per vault item + format + run)

### Migration

- Add `vault_url` and `format` columns to `events` table (or use a composite `item_key`)
- Backfill from `completed_urls.url` where possible
- Old events with filename-only `item_name` remain queryable but lack vault grouping

---

## Future Console Support

**Status:** Architecture ready, waiting for demand

The `IPipeline` interface supports any console. Adding a new one requires:

1. `Module.{Console}Tools` — console-specific tools (e.g., ISO conversion, patching)
2. `Module.{Console}Pipeline` — implements `IPipeline` with `BuildFlow`, `CheckDuplicate`, `GetStepDurations`
3. `{Console}Phase` — console-specific sub-phases extending `PipelinePhase`
4. Host wiring — register in DI, add to `DownloadQueue.GetPipeline()`, add platform check in `HandlePostDownload`

The pipeline-owned flow system means no endpoint changes needed — the host delegates generically.

### Candidates

- **PS2** — simpler than PS3, likely just extraction + rename
- **PSP** — ISO handling, CSO compression
- **Wii** — WBFS format handling
- **GameCube** — ISO/GCZ handling
