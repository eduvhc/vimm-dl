-- Correlation ID links all events for a single pipeline run.
-- New runs for the same item get a new ID (e.g., retry after error).
ALTER TABLE events ADD COLUMN correlation_id TEXT;

CREATE INDEX IF NOT EXISTS idx_events_correlation ON events(correlation_id);
