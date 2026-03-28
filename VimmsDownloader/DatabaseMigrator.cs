using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

/// <summary>
/// Runs embedded SQL migration files in order, tracking which have been executed.
/// Each migration must be idempotent (safe to re-run if the tracking table is lost).
/// Files are named NNN_description.sql and embedded as resources.
/// </summary>
static class DatabaseMigrator
{
    private const string MigrationsTable = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            name TEXT PRIMARY KEY,
            executed_at TEXT NOT NULL DEFAULT (datetime('now'))
        )
    """;

    public static async Task MigrateAsync(SqliteConnection db, ILogger logger)
    {
        // Ensure tracking table exists
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = MigrationsTable;
            await cmd.ExecuteNonQueryAsync();
        }

        var executed = await GetExecutedAsync(db);
        var migrations = LoadMigrations();

        foreach (var (name, sql) in migrations)
        {
            if (executed.Contains(name))
                continue;

            logger.LogInformation("Running migration: {Name}", name);
            await using var tx = await db.BeginTransactionAsync();
            try
            {
                // Execute each statement in the migration separately
                // (SQLite ALTER TABLE can't be batched with other ALTERs in one exec)
                foreach (var statement in SplitStatements(sql))
                {
                    try
                    {
                        await using var cmd = db.CreateCommand();
                        cmd.Transaction = (SqliteTransaction)tx;
                        cmd.CommandText = statement;
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (SqliteException ex) when (IsIgnorable(ex))
                    {
                        // Idempotent: column already exists, table already exists, etc.
                        logger.LogDebug("Skipped (already applied): {Message}", ex.Message);
                    }
                }

                // Mark as executed
                await using (var mark = db.CreateCommand())
                {
                    mark.Transaction = (SqliteTransaction)tx;
                    mark.CommandText = "INSERT OR IGNORE INTO schema_migrations (name) VALUES ($name)";
                    mark.Parameters.AddWithValue("$name", name);
                    await mark.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                logger.LogInformation("Completed migration: {Name}", name);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }

    private static async Task<HashSet<string>> GetExecutedAsync(SqliteConnection db)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT name FROM schema_migrations";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) set.Add(r.GetString(0));
        return set;
    }

    private static List<(string Name, string Sql)> LoadMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = "VimmsDownloader.Migrations.";
        var resources = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix) && n.EndsWith(".sql"))
            .OrderBy(n => n)
            .ToList();

        var migrations = new List<(string, string)>();
        foreach (var resource in resources)
        {
            var name = resource[prefix.Length..]; // e.g. "001_initial_schema.sql"
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            migrations.Add((name, reader.ReadToEnd()));
        }
        return migrations;
    }

    private static IEnumerable<string> SplitStatements(string sql)
    {
        // Strip comment-only lines before splitting, so comments between statements don't
        // cause a statement to start with "--" and get filtered out
        var cleaned = string.Join('\n', sql.Split('\n')
            .Select(line => line.TrimStart().StartsWith("--") ? "" : line));

        return cleaned.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s));
    }

    private static bool IsIgnorable(SqliteException ex)
    {
        var msg = ex.Message;
        // "duplicate column name" — ALTER TABLE ADD COLUMN on existing column
        // "already exists" — CREATE TABLE/INDEX on existing object
        return msg.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("already exists", StringComparison.OrdinalIgnoreCase);
    }
}
