-- Append-only event log for all module events (download, pipeline, sync)
CREATE TABLE IF NOT EXISTS events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    item_name TEXT NOT NULL,
    event_type TEXT NOT NULL,
    phase TEXT,
    message TEXT,
    data TEXT,
    timestamp TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_events_item ON events(item_name);
CREATE INDEX IF NOT EXISTS idx_events_type ON events(event_type);
