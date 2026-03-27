using System.Net;
using Microsoft.Data.Sqlite;

class QueueRepository
{
    private string _connStr = "Data Source=queue.db";

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connStr);
        conn.Open();
        return conn;
    }

    public void Init(string? configConnStr)
    {
        if (!string.IsNullOrEmpty(configConnStr))
        {
            _connStr = configConnStr;
            var dbPath = configConnStr.Replace("Data Source=", "").Trim();
            var dbDir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDir)) Directory.CreateDirectory(dbDir);
        }

        using var db = Open();
        Exec(db, "PRAGMA journal_mode=WAL");
        Exec(db, """
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
                size TEXT NOT NULL,
                formats TEXT
            );
        """);
        try { Exec(db, "ALTER TABLE queued_urls ADD COLUMN format INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec(db, "ALTER TABLE url_meta ADD COLUMN formats TEXT"); } catch { }
        try { Exec(db, "ALTER TABLE completed_urls ADD COLUMN completed_at TEXT"); } catch { }
        Exec(db, "CREATE INDEX IF NOT EXISTS idx_completed_url ON completed_urls(url)");
    }

    public bool HasQueuedUrls()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM queued_urls LIMIT 1)";
        return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
    }

    public List<QueuedItem> GetQueuedItems()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT q.id, q.url, q.format, m.title, m.platform, m.size, m.formats
            FROM queued_urls q LEFT JOIN url_meta m ON q.url = m.url
            ORDER BY q.id
        """;
        var items = new List<QueuedItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            items.Add(new QueuedItem(r.GetInt32(0), r.GetString(1), r.GetInt32(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return items;
    }

    public List<QueueIdRow> GetQueueIds()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, url, format FROM queued_urls";
        var items = new List<QueueIdRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            items.Add(new QueueIdRow(r.GetInt32(0), r.GetString(1), r.GetInt32(2)));
        return items;
    }

    public void AddToQueue(string url, int format)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO queued_urls (url, format) VALUES ($url, $format)";
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$format", format);
        cmd.ExecuteNonQuery();
    }

    public void DeleteFromQueue(int id)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM queued_urls WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public bool MoveInQueue(int id, string direction)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        try
        {
            var ids = new List<int>();
            using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT id FROM queued_urls ORDER BY id";
                using var r = cmd.ExecuteReader();
                while (r.Read()) ids.Add(r.GetInt32(0));
            }

            var idx = ids.IndexOf(id);
            if (idx < 0) { tx.Rollback(); return false; }
            var targetIdx = direction == "up" ? idx - 1 : idx + 1;
            if (targetIdx < 0 || targetIdx >= ids.Count) { tx.Rollback(); return true; }

            var otherId = ids[targetIdx];
            ExecTx(db, tx, "UPDATE queued_urls SET id = -999 WHERE id = $id", ("$id", id));
            ExecTx(db, tx, "UPDATE queued_urls SET id = $newId WHERE id = $otherId", ("$newId", id), ("$otherId", otherId));
            ExecTx(db, tx, "UPDATE queued_urls SET id = $newId WHERE id = -999", ("$newId", otherId));
            tx.Commit();
            return true;
        }
        catch { tx.Rollback(); return false; }
    }

    public void ClearQueue()
    {
        using var db = Open();
        Exec(db, "DELETE FROM queued_urls");
    }

    public void SetFormat(int id, int format)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE queued_urls SET format = $format WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$format", format);
        cmd.ExecuteNonQuery();
    }

    public MetaResponse? GetMeta(string url)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT title, platform, size, formats FROM url_meta WHERE url = $url";
        cmd.Parameters.AddWithValue("$url", url);
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r.IsDBNull(0)) return null;

        var title = WebUtility.HtmlDecode(r.GetString(0));
        var platform = WebUtility.HtmlDecode(r.GetString(1));
        var size = r.GetString(2);
        var formats = r.IsDBNull(3) ? null : r.GetString(3);

        if (title != r.GetString(0) || platform != r.GetString(1))
        {
            using var upd = db.CreateCommand();
            upd.CommandText = "UPDATE url_meta SET title=$t, platform=$p WHERE url=$url";
            upd.Parameters.AddWithValue("$t", title);
            upd.Parameters.AddWithValue("$p", platform);
            upd.Parameters.AddWithValue("$url", url);
            upd.ExecuteNonQuery();
        }

        return new MetaResponse(title, platform, size, formats);
    }

    public void SaveMeta(string url, string title, string platform, string size, string? formats)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO url_meta (url, title, platform, size, formats)
            VALUES ($url, $title, $platform, $size, $formats)
        """;
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$platform", platform);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$formats", (object?)formats ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public (int Id, string Url, int Format)? GetNextQueueItem()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, url, format FROM queued_urls ORDER BY id LIMIT 1";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return (r.GetInt32(0), r.GetString(1), r.GetInt32(2));
    }

    public void CompleteItem(int id, string url, string filename, string filepath)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        try
        {
            ExecTx(db, tx, "DELETE FROM queued_urls WHERE id = $id", ("$id", id));
            using var ins = db.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO completed_urls (url, filename, filepath, completed_at) VALUES ($url, $filename, $filepath, datetime('now'))";
            ins.Parameters.AddWithValue("$url", url);
            ins.Parameters.AddWithValue("$filename", filename);
            ins.Parameters.AddWithValue("$filepath", filepath);
            ins.ExecuteNonQuery();
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void MoveToFront(int queueId)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT MIN(id) FROM queued_urls";
        var minId = cmd.ExecuteScalar();
        if (minId is long min && queueId != min)
        {
            using var upd = db.CreateCommand();
            upd.CommandText = "UPDATE queued_urls SET id = $newId WHERE id = $queueId";
            upd.Parameters.AddWithValue("$newId", min - 1);
            upd.Parameters.AddWithValue("$queueId", queueId);
            upd.ExecuteNonQuery();
        }
    }

    public void DeleteCompleted(int id)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM completed_urls WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public List<CompletedItem> GetCompletedItemsEnriched()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT c.id, c.url, c.filename, c.filepath, c.completed_at,
                   m.title, m.platform, m.size
            FROM completed_urls c
            LEFT JOIN url_meta m ON c.url = m.url
            ORDER BY c.id DESC
        """;
        var items = new List<CompletedItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            items.Add(new CompletedItem(
                r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7)));
        return items;
    }

    private static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void ExecTx(SqliteConnection db, SqliteTransaction tx, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}
