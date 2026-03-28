-- Store the download format in completed_urls so it's not lost after queue removal.
ALTER TABLE completed_urls ADD COLUMN format INTEGER;
