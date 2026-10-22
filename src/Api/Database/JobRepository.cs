using Microsoft.Data.Sqlite;

namespace Api.Database;

public class JobRepository
{
    private readonly DatabaseInitializer _db;
    private long _version;

    public JobRepository(DatabaseInitializer db) => _db = db;

    /// <summary>
    /// In-memory change counter, bumped on every schedule-affecting mutation
    /// (insert/update/delete). Lets the scheduler detect "something changed"
    /// without touching the database. Intentionally NOT bumped by
    /// SetLastDispatched, which is a scheduler-internal cursor write.
    /// </summary>
    public long Version => Interlocked.Read(ref _version);

    private void BumpVersion() => Interlocked.Increment(ref _version);

    public List<CronJob> GetAll()
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM jobs ORDER BY name";
        return ReadAll(cmd);
    }

    public CronJob? GetById(long id)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM jobs WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        var rows = ReadAll(cmd);
        return rows.Count == 0 ? null : rows[0];
    }

    public CronJob Insert(CronJob job)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO jobs (name, target_url, cron_expression, timezone, enabled, catchup_days, max_retries, append_date_param, auth_mode, auth_scope, auth_client_id, auth_client_secret, last_dispatched_utc, created_at, updated_at)
            VALUES (@name, @target_url, @cron_expression, @timezone, @enabled, @catchup_days, @max_retries, @append_date_param, @auth_mode, @auth_scope, @auth_client_id, @auth_client_secret, @last_dispatched_utc, @created_at, @updated_at);
            SELECT last_insert_rowid();
            """;
        FillParams(cmd, job);
        job.Id = cmd.ExecuteScalarLong();
        BumpVersion();
        return job;
    }

    public void Update(CronJob job)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE jobs SET name=@name, target_url=@target_url, cron_expression=@cron_expression, timezone=@timezone,
                enabled=@enabled, catchup_days=@catchup_days, max_retries=@max_retries, append_date_param=@append_date_param,
                auth_mode=@auth_mode, auth_scope=@auth_scope, auth_client_id=@auth_client_id, auth_client_secret=@auth_client_secret, updated_at=@updated_at
            WHERE id=@id
            """;
        FillParams(cmd, job);
        cmd.Parameters.AddWithValue("@id", job.Id);
        cmd.ExecuteNonQuery();
        BumpVersion();
    }

    public void SetLastDispatched(long id, DateTime tickUtc)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE jobs SET last_dispatched_utc = @tick WHERE id = @id";
        cmd.Parameters.AddWithValue("@tick", Format(tickUtc));
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM jobs WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        BumpVersion();
    }

    private static void FillParams(SqliteCommand cmd, CronJob job)
    {
        cmd.Parameters.AddWithValue("@name", job.Name);
        cmd.Parameters.AddWithValue("@target_url", job.TargetUrl);
        cmd.Parameters.AddWithValue("@cron_expression", job.CronExpression);
        cmd.Parameters.AddWithValue("@timezone", job.Timezone);
        cmd.Parameters.AddWithValue("@enabled", job.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@catchup_days", job.CatchupDays);
        cmd.Parameters.AddWithValue("@max_retries", job.MaxRetries);
        cmd.Parameters.AddWithValue("@append_date_param", job.AppendDateParam ? 1 : 0);
        cmd.Parameters.AddWithValue("@auth_mode", (int)job.AuthMode);
        cmd.Parameters.AddWithValue("@auth_scope", (object?)job.AuthScope ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@auth_client_id", (object?)job.AuthClientId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@auth_client_secret", (object?)job.AuthClientSecret ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@last_dispatched_utc", (object?)Format(job.LastDispatchedTickUtc) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", Format(job.CreatedAtUtc));
        cmd.Parameters.AddWithValue("@updated_at", Format(job.UpdatedAtUtc));
    }

    private static List<CronJob> ReadAll(SqliteCommand cmd)
    {
        var jobs = new List<CronJob>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            jobs.Add(new CronJob
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                TargetUrl = reader.GetString(reader.GetOrdinal("target_url")),
                CronExpression = reader.GetString(reader.GetOrdinal("cron_expression")),
                Timezone = reader.GetString(reader.GetOrdinal("timezone")),
                Enabled = reader.GetInt64(reader.GetOrdinal("enabled")) != 0,
                CatchupDays = Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("catchup_days"))),
                MaxRetries = Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("max_retries"))),
                AppendDateParam = reader.GetInt64(reader.GetOrdinal("append_date_param")) != 0,
                AuthMode = (TargetAuthMode)Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("auth_mode"))),
                AuthScope = reader.IsDBNull(reader.GetOrdinal("auth_scope")) ? null : reader.GetString(reader.GetOrdinal("auth_scope")),
                AuthClientId = reader.IsDBNull(reader.GetOrdinal("auth_client_id")) ? null : reader.GetString(reader.GetOrdinal("auth_client_id")),
                AuthClientSecret = reader.IsDBNull(reader.GetOrdinal("auth_client_secret")) ? null : reader.GetString(reader.GetOrdinal("auth_client_secret")),
                LastDispatchedTickUtc = reader.IsDBNull(reader.GetOrdinal("last_dispatched_utc"))
                    ? null
                    : Parse(reader.GetString(reader.GetOrdinal("last_dispatched_utc"))),
                CreatedAtUtc = Parse(reader.GetString(reader.GetOrdinal("created_at"))),
                UpdatedAtUtc = Parse(reader.GetString(reader.GetOrdinal("updated_at")))
            });
        }
        return jobs;
    }

    internal static string Format(DateTime utc) => utc.ToString("o");

    internal static string? Format(DateTime? utc) => utc?.ToString("o");

    internal static DateTime Parse(string value) => DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);
}
