-- Conversion state columns on completed_urls (projection from pipeline events)
ALTER TABLE completed_urls ADD COLUMN conv_phase TEXT;
ALTER TABLE completed_urls ADD COLUMN conv_message TEXT;
ALTER TABLE completed_urls ADD COLUMN iso_filename TEXT;

CREATE INDEX IF NOT EXISTS idx_completed_url ON completed_urls(url);
CREATE INDEX IF NOT EXISTS idx_completed_filename ON completed_urls(filename);
