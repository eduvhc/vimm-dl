-- Initial schema: queue, completed downloads, metadata cache, settings
CREATE TABLE IF NOT EXISTS queued_urls (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    url TEXT NOT NULL,
    format INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS completed_urls (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    url TEXT NOT NULL,
    filename TEXT NOT NULL,
    filepath TEXT
);

CREATE TABLE IF NOT EXISTS url_meta (
    url TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    platform TEXT NOT NULL,
    size TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS settings (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

INSERT OR IGNORE INTO settings (key, value) VALUES ('rename_fix_the', 'true');
INSERT OR IGNORE INTO settings (key, value) VALUES ('rename_add_serial', 'true');
INSERT OR IGNORE INTO settings (key, value) VALUES ('rename_strip_region', 'true');
INSERT OR IGNORE INTO settings (key, value) VALUES ('ps3_parallelism', '3');
INSERT OR IGNORE INTO settings (key, value) VALUES ('download_path', '');
INSERT OR IGNORE INTO settings (key, value) VALUES ('sync_path', '');
