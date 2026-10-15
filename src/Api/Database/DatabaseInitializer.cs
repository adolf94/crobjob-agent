using Microsoft.Data.Sqlite;

namespace Api.Database;

public class DatabaseInitializer
{
    private readonly string _dbPath;

    public DatabaseInitializer(string dbPath)
    {
        _dbPath = dbPath;
    }

    public string DbPath => _dbPath;

    public void Initialize()
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(_dbPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var conn = OpenConnection();
        conn.Execute("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS jobs (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                target_url TEXT NOT NULL,
                cron_expression TEXT NOT NULL,
                timezone TEXT NOT NULL,
                enabled INTEGER NOT NULL DEFAULT 1,
                catchup_days INTEGER NOT NULL DEFAULT 1,
                max_retries INTEGER NOT NULL DEFAULT 3,
                append_date_param INTEGER NOT NULL DEFAULT 0,
                last_dispatched_utc TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS runs (
                id INTEGER PRIMARY KEY,
                job_id INTEGER NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
                scheduled_for TEXT NOT NULL,
                started_at TEXT NOT NULL,
                finished_at TEXT,
                attempt INTEGER NOT NULL,
                status INTEGER NOT NULL,
                http_status INTEGER,
                error TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_runs_job ON runs(job_id, scheduled_for DESC);
            """);

        Migrate(conn, "jobs", ("auth_mode", "INTEGER NOT NULL DEFAULT 0"), ("auth_scope", "TEXT"),
            ("auth_client_id", "TEXT"), ("auth_client_secret", "TEXT"));
    }

    private static void Migrate(SqliteConnection conn, string table, params (string col, string def)[] columns)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                existing.Add(reader.GetString(1));
        }

        foreach (var (col, def) in columns)
        {
            if (existing.Contains(col))
                continue;
            conn.Execute($"ALTER TABLE {table} ADD COLUMN {col} {def};");
        }
    }

    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("PRAGMA foreign_keys=ON;");
        return conn;
    }
}
