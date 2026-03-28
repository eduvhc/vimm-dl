-- Feature flags: beta and developer features, disabled by default
INSERT OR IGNORE INTO settings (key, value) VALUES ('feature_sync', 'false');
INSERT OR IGNORE INTO settings (key, value) VALUES ('feature_events', 'false');
