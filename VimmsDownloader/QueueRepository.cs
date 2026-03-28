using System.Net;
using Microsoft.Data.Sqlite;

class QueueRepository
{
    private string _connStr = "Data Source=queue.db";

    public async Task<SqliteConnection> OpenAsync()
    {
        var conn = new SqliteConnection(_connStr);
        await conn.OpenAsync();
        return conn;
    }

    public async Task InitAsync(string? configConnStr, ILogger logger)
    {
        if (!string.IsNullOrEmpty(configConnStr))
        {
            _connStr = configConnStr;
            var dbPath = configConnStr.Replace("Data Source=", "").Trim();
            var dbDir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDir)) Directory.CreateDirectory(dbDir);

            // Derive download path from DB location: data/ and downloads/ are siblings
            // e.g., /vimms/data/queue.db → /vimms/downloads
            var baseDir = Path.GetDirectoryName(dbDir);
            if (!string.IsNullOrEmpty(baseDir))
            {
                var downloadsDir = Path.Combine(baseDir, "downloads");
                Directory.CreateDirectory(downloadsDir);
                _downloadPath = downloadsDir;
            }
        }

        await using var db = await OpenAsync();
        await ExecAsync(db, "PRAGMA journal_mode=WAL");
        await DatabaseMigrator.MigrateAsync(db, logger);
    }

    public async Task<Dictionary<string, string>> GetAllSettingsAsync()
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM settings";
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new Dictionary<string, string>();
        while (await r.ReadAsync()) result[r.GetString(0)] = r.GetString(1);
        return result;
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return await cmd.ExecuteScalarAsync() as string;
    }

    public async Task SaveSettingAsync(string key, string value)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO settings (key, value) VALUES ($key, $value)";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        await cmd.ExecuteNonQueryAsync();
    }

    private string? _downloadPath;

    public string GetDownloadPath() => _downloadPath
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public async Task<string> GetSyncPathAsync() => await GetSettingAsync("sync_path") ?? "";

    public async Task<bool> HasQueuedUrlsAsync()
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM queued_urls LIMIT 1)";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1;
    }

    public async Task<List<QueuedItem>> GetQueuedItemsAsync()
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT q.id, q.url, q.format, m.title, m.platform, m.size, m.formats
            FROM queued_urls q LEFT JOIN url_meta m ON q.url = m.url
            ORDER BY q.id
        """;
        var items = new List<QueuedItem>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            items.Add(new QueuedItem(r.GetInt32(0), r.GetString(1), r.GetInt32(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return items;
    }

    public async Task<List<QueueIdRow>> GetQueueIdsAsync()
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, url, format FROM queued_urls";
        var items = new List<QueueIdRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            items.Add(new QueueIdRow(r.GetInt32(0), r.GetString(1), r.GetInt32(2)));
        return items;
    }

    public record DuplicateDbMatch(string Url, string Source, string? ConvPhase, string? Title, string? Filename, string? IsoFilename, string? Platform);

    public async Task<List<DuplicateDbMatch>> CheckDuplicatesAsync(List<string> urls)
    {
        if (urls.Count == 0) return [];

        var results = new List<DuplicateDbMatch>();
        var normalized = urls.Select(u => u.ToLowerInvariant()).Distinct().ToList();
        var placeholders = string.Join(",", normalized.Select((_, i) => $"$u{i}"));

        await using var db = await OpenAsync();

        // Check queued_urls
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT DISTINCT q.url, m.title, m.platform
                FROM queued_urls q LEFT JOIN url_meta m ON q.url = m.url
                WHERE LOWER(q.url) IN ({placeholders})
            """;
            for (int i = 0; i < normalized.Count; i++)
                cmd.Parameters.AddWithValue($"$u{i}", normalized[i]);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var url = r.GetString(0);
                var title = r.IsDBNull(1) ? null : r.GetString(1);
                var platform = r.IsDBNull(2) ? null : r.GetString(2);
                results.Add(new DuplicateDbMatch(url, "queued", null, title, null, null, platform));
            }
        }

        // Check completed_urls — pick best row per URL (done > active > null)
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT c.url, c.conv_phase, c.iso_filename, c.filename, m.title, m.platform
                FROM completed_urls c LEFT JOIN url_meta m ON c.url = m.url
                WHERE LOWER(c.url) IN ({placeholders})
            """;
            for (int i = 0; i < normalized.Count; i++)
                cmd.Parameters.AddWithValue($"$u{i}", normalized[i]);

            var byUrl = new Dictionary<string, DuplicateDbMatch>(StringComparer.OrdinalIgnoreCase);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var url = r.GetString(0);
                var phase = r.IsDBNull(1) ? null : r.GetString(1);
                var iso = r.IsDBNull(2) ? null : r.GetString(2);
                var filename = r.IsDBNull(3) ? null : r.GetString(3);
                var title = r.IsDBNull(4) ? null : r.GetString(4);
                var platform = r.IsDBNull(5) ? null : r.GetString(5);

                if (!byUrl.TryGetValue(url, out var existing) || RankPhase(phase) > RankPhase(existing.ConvPhase))
                    byUrl[url] = new DuplicateDbMatch(url, "completed", phase, title, filename, iso, platform);
            }

            results.AddRange(byUrl.Values);
        }

        return results;
    }

    private static int RankPhase(string? phase) => phase switch
    {
        "done" => 3,
        "error" => 1,
        null => 0,
        _ => 2,
    };

    public async Task AddToQueueAsync(string url, int format)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO queued_urls (url, format) VALUES ($url, $format)";
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$format", format);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteFromQueueAsync(int id)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM queued_urls WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> MoveInQueueAsync(int id, string direction)
    {
        await using var db = await OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        try
        {
            var ids = new List<int>();
            await using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = (SqliteTransaction)tx;
                cmd.CommandText = "SELECT id FROM queued_urls ORDER BY id";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
            }

            var idx = ids.IndexOf(id);
            if (idx < 0) { await tx.RollbackAsync(); return false; }
            var targetIdx = direction == "up" ? idx - 1 : idx + 1;
            if (targetIdx < 0 || targetIdx >= ids.Count) { await tx.RollbackAsync(); return true; }

            var otherId = ids[targetIdx];
            await ExecTxAsync(db, (SqliteTransaction)tx, "UPDATE queued_urls SET id = -999 WHERE id = $id", ("$id", id));
            await ExecTxAsync(db, (SqliteTransaction)tx, "UPDATE queued_urls SET id = $newId WHERE id = $otherId", ("$newId", id), ("$otherId", otherId));
            await ExecTxAsync(db, (SqliteTransaction)tx, "UPDATE queued_urls SET id = $newId WHERE id = -999", ("$newId", otherId));
            await tx.CommitAsync();
            return true;
        }
        catch { await tx.RollbackAsync(); return false; }
    }

    public async Task ReorderQueueAsync(List<int> orderedIds)
    {
        await using var db = await OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        try
        {
            // Move all to negative IDs first to avoid conflicts
            for (int i = 0; i < orderedIds.Count; i++)
                await ExecTxAsync(db, (SqliteTransaction)tx, "UPDATE queued_urls SET id = $newId WHERE id = $oldId",
                    ("$newId", -(i + 1)), ("$oldId", orderedIds[i]));

            // Assign sequential positive IDs in new order
            for (int i = 0; i < orderedIds.Count; i++)
                await ExecTxAsync(db, (SqliteTransaction)tx, "UPDATE queued_urls SET id = $newId WHERE id = $oldId",
                    ("$newId", i + 1), ("$oldId", -(i + 1)));

            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    public async Task ClearQueueAsync()
    {
        await using var db = await OpenAsync();
        await ExecAsync(db, "DELETE FROM queued_urls");
    }

    public async Task SetFormatAsync(int id, int format)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE queued_urls SET format = $format WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$format", format);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<MetaResponse?> GetMetaAsync(string url)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT title, platform, size, formats, serial FROM url_meta WHERE url = $url";
        cmd.Parameters.AddWithValue("$url", url);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync() || r.IsDBNull(0)) return null;

        var title = WebUtility.HtmlDecode(r.GetString(0));
        var platform = WebUtility.HtmlDecode(r.GetString(1));
        var size = r.GetString(2);
        var formats = r.IsDBNull(3) ? null : r.GetString(3);
        var serial = r.IsDBNull(4) ? null : r.GetString(4);

        if (title != r.GetString(0) || platform != r.GetString(1))
        {
            await using var upd = db.CreateCommand();
            upd.CommandText = "UPDATE url_meta SET title=$t, platform=$p WHERE url=$url";
            upd.Parameters.AddWithValue("$t", title);
            upd.Parameters.AddWithValue("$p", platform);
            upd.Parameters.AddWithValue("$url", url);
            await upd.ExecuteNonQueryAsync();
        }

        return new MetaResponse(title, platform, size, formats, serial);
    }

    public async Task SaveMetaAsync(string url, string title, string platform, string size, string? formats, string? serial)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO url_meta (url, title, platform, size, formats, serial)
            VALUES ($url, $title, $platform, $size, $formats, $serial)
        """;
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$platform", platform);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$formats", (object?)formats ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$serial", (object?)serial ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(int Id, string Url, int Format)?> GetNextQueueItemAsync()
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, url, format FROM queued_urls ORDER BY id LIMIT 1";
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (r.GetInt32(0), r.GetString(1), r.GetInt32(2));
    }

    public async Task CompleteItemAsync(int id, string url, string filename, string filepath)
    {
        await using var db = await OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        try
        {
            await ExecTxAsync(db, (SqliteTransaction)tx, "DELETE FROM queued_urls WHERE id = $id", ("$id", id));
            await using var ins = db.CreateCommand();
            ins.Transaction = (SqliteTransaction)tx;
            ins.CommandText = "INSERT INTO completed_urls (url, filename, filepath, completed_at) VALUES ($url, $filename, $filepath, datetime('now'))";
            ins.Parameters.AddWithValue("$url", url);
            ins.Parameters.AddWithValue("$filename", filename);
            ins.Parameters.AddWithValue("$filepath", filepath);
            await ins.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task MoveToFrontAsync(int queueId)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT MIN(id) FROM queued_urls";
        var minId = await cmd.ExecuteScalarAsync();
        if (minId is long min && queueId != min)
        {
            await using var upd = db.CreateCommand();
            upd.CommandText = "UPDATE queued_urls SET id = $newId WHERE id = $queueId";
            upd.Parameters.AddWithValue("$newId", min - 1);
            upd.Parameters.AddWithValue("$queueId", queueId);
            await upd.ExecuteNonQueryAsync();
        }
    }

    public async Task<(string? Filepath, string? Filename, string? IsoFilename)?> GetCompletedByIdAsync(int id)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT filepath, filename, iso_filename FROM completed_urls WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (r.IsDBNull(0) ? null : r.GetString(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2));
    }

    public async Task DeleteCompletedAsync(int id)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM completed_urls WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<CompletedItem>> GetCompletedItemsEnrichedAsync()
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT c.id, c.url, c.filename, c.filepath, c.completed_at,
                   m.title, m.platform, m.size,
                   c.conv_phase, c.conv_message, c.iso_filename
            FROM completed_urls c
            LEFT JOIN url_meta m ON c.url = m.url
            ORDER BY c.id DESC
        """;
        var items = new List<CompletedItem>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            items.Add(new CompletedItem(
                r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? null : r.GetString(10)));
        return items;
    }

    public async Task<EventsResponse> GetEventsAsync(int limit = 200, int offset = 0, string? type = null, string? item = null)
    {
        await using var db = await OpenAsync();

        // Count
        await using var countCmd = db.CreateCommand();
        var where = BuildEventWhere(countCmd, type, item);
        countCmd.CommandText = $"SELECT COUNT(*) FROM events{where}";
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        // Rows
        await using var cmd = db.CreateCommand();
        var whereRows = BuildEventWhere(cmd, type, item);
        cmd.CommandText = $"SELECT id, item_name, event_type, phase, message, data, timestamp, correlation_id FROM events{whereRows} ORDER BY id DESC LIMIT $limit OFFSET $offset";
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        var events = new List<EventRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            events.Add(new EventRow(
                r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7)));
        return new EventsResponse(events, total);
    }

    private static string BuildEventWhere(SqliteCommand cmd, string? type, string? item)
    {
        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(type))
        {
            // Prefix match for category filters (e.g. "download" matches "download_status", "download_error", etc.)
            // Exact match if the type contains an underscore (e.g. "download_error")
            if (type.Contains('_'))
            {
                clauses.Add("event_type = $type");
                cmd.Parameters.AddWithValue("$type", type);
            }
            else
            {
                clauses.Add("event_type LIKE $type");
                cmd.Parameters.AddWithValue("$type", $"{type}%");
            }
        }
        if (!string.IsNullOrEmpty(item))
        {
            clauses.Add("item_name LIKE $item");
            cmd.Parameters.AddWithValue("$item", $"%{item}%");
        }
        return clauses.Count > 0 ? $" WHERE {string.Join(" AND ", clauses)}" : "";
    }

    public async Task PruneEventsAsync(int maxRows = 50_000, int retentionDays = 7)
    {
        await using var db = await OpenAsync();

        // Delete events older than retention period
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM events WHERE timestamp < datetime('now', $days)";
            cmd.Parameters.AddWithValue("$days", $"-{retentionDays} days");
            await cmd.ExecuteNonQueryAsync();
        }

        // Cap total rows — keep newest, delete oldest
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                DELETE FROM events WHERE id NOT IN (
                    SELECT id FROM events ORDER BY id DESC LIMIT $max
                )
            """;
            cmd.Parameters.AddWithValue("$max", maxRows);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task AppendEventAsync(string itemName, string eventType, string? phase, string? message, string? data, string? correlationId = null)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO events (item_name, event_type, phase, message, data, timestamp, correlation_id)
            VALUES ($item, $type, $phase, $msg, $data, datetime('now'), $cid)
        """;
        cmd.Parameters.AddWithValue("$item", itemName);
        cmd.Parameters.AddWithValue("$type", eventType);
        cmd.Parameters.AddWithValue("$phase", (object?)phase ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$msg", (object?)message ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$data", (object?)data ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cid", (object?)correlationId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Get phase transitions with timestamps for a pipeline run.
    /// Used to compute step durations in the trace.
    /// </summary>
    public async Task<List<(string Phase, string Timestamp)>> GetPipelineTimingsAsync(string itemName, string? correlationId)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();

        if (correlationId != null)
        {
            cmd.CommandText = """
                SELECT phase, timestamp FROM events
                WHERE correlation_id = $cid AND event_type = 'pipeline_status' AND phase IS NOT NULL
                ORDER BY id
            """;
            cmd.Parameters.AddWithValue("$cid", correlationId);
        }
        else
        {
            cmd.CommandText = """
                SELECT phase, timestamp FROM events
                WHERE item_name = $item AND event_type = 'pipeline_status' AND phase IS NOT NULL
                ORDER BY id
            """;
            cmd.Parameters.AddWithValue("$item", itemName);
        }

        var results = new List<(string, string)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            results.Add((r.GetString(0), r.GetString(1)));
        return results;
    }

    public async Task SaveConversionStateAsync(string filename, string phase, string message, string? isoFilename)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE completed_urls
            SET conv_phase = $phase, conv_message = $message, iso_filename = $isoFilename
            WHERE filename = $filename
        """;
        cmd.Parameters.AddWithValue("$filename", filename);
        cmd.Parameters.AddWithValue("$phase", phase);
        cmd.Parameters.AddWithValue("$message", message);
        cmd.Parameters.AddWithValue("$isoFilename", (object?)isoFilename ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> IsConvertedAsync(string filename)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM completed_urls WHERE filename = $f AND conv_phase = 'done')";
        cmd.Parameters.AddWithValue("$f", filename);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1;
    }

    public async Task<HashSet<string>> GetConvertedFilenamesAsync()
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT filename FROM completed_urls WHERE conv_phase = 'done'";
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) set.Add(r.GetString(0));
        return set;
    }

    public async Task MigratePs3ConvertedFileAsync(string downloadBasePath)
    {
        var convertedFile = Path.Combine(downloadBasePath, "completed", ".ps3converted");
        if (!File.Exists(convertedFile)) return;

        var lines = await File.ReadAllLinesAsync(convertedFile);
        await using var db = await OpenAsync();
        foreach (var line in lines)
        {
            var name = line.Trim();
            if (name.Length == 0) continue;
            await using var cmd = db.CreateCommand();
            cmd.CommandText = """
                UPDATE completed_urls SET conv_phase = 'done', conv_message = 'Previously converted'
                WHERE filename = $f AND conv_phase IS NULL
            """;
            cmd.Parameters.AddWithValue("$f", name);
            await cmd.ExecuteNonQueryAsync();
        }

        File.Move(convertedFile, convertedFile + ".migrated");
    }

    private static async Task ExecAsync(SqliteConnection db, string sql)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ExecTxAsync(SqliteConnection db, SqliteTransaction tx, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
