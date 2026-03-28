-- Add format column to url_meta, completed_at to completed_urls, serial to url_meta
-- Note: queued_urls.format is in the initial CREATE TABLE (DEFAULT 0), but older DBs may lack url_meta.formats

-- url_meta.formats for alternative download format options
ALTER TABLE url_meta ADD COLUMN formats TEXT;

-- completed_urls.completed_at for timestamp tracking
ALTER TABLE completed_urls ADD COLUMN completed_at TEXT;

-- url_meta.serial for PS3 game serial numbers
ALTER TABLE url_meta ADD COLUMN serial TEXT;
