using Microsoft.Data.Sqlite;

namespace Api.Database;

public class RunRepository
{
    private readonly DatabaseInitializer _db;

    public RunRepository(DatabaseInitializer db) => _db = db;

    public long Insert(RunRecord run)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO runs (job_id, scheduled_for, started_at, finished_at, attempt, status, http_status, error)
            VALUES (@job_id, @scheduled_for, @started_at, @finished_at, @attempt, @status, @http_status, @error);
            SELECT last_insert_rowid();
            """;
        Fill(cmd, run);
        run.Id = cmd.ExecuteScalarLong();
        return run.Id;
    }

    public void Update(RunRecord run)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE runs SET finished_at=@finished_at, status=@status, http_status=@http_status, error=@error
            WHERE id=@id
            """;
        cmd.Parameters.AddWithValue("@finished_at", (object?)JobRepository.Format(run.FinishedAtUtc) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (int)run.Status);
        cmd.Parameters.AddWithValue("@http_status", (object?)run.HttpStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error", (object?)run.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", run.Id);
        cmd.ExecuteNonQuery();
    }

    public List<RunRecord> GetRecentForJob(long jobId, int limit = 50)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM runs WHERE job_id = @job_id ORDER BY scheduled_for DESC, attempt DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@job_id", jobId);
        cmd.Parameters.AddWithValue("@limit", limit);
        return ReadAll(cmd);
    }

    public RunRecord? GetLatestForJob(long jobId)
    {
        var runs = GetRecentForJob(jobId, 1);
        return runs.Count == 0 ? null : runs[0];
    }

    private static void Fill(SqliteCommand cmd, RunRecord run)
    {
        cmd.Parameters.AddWithValue("@job_id", run.JobId);
        cmd.Parameters.AddWithValue("@scheduled_for", JobRepository.Format(run.ScheduledForUtc));
        cmd.Parameters.AddWithValue("@started_at", JobRepository.Format(run.StartedAtUtc));
        cmd.Parameters.AddWithValue("@finished_at", (object?)JobRepository.Format(run.FinishedAtUtc) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@attempt", run.Attempt);
        cmd.Parameters.AddWithValue("@status", (int)run.Status);
        cmd.Parameters.AddWithValue("@http_status", (object?)run.HttpStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error", (object?)run.Error ?? DBNull.Value);
    }

    private static List<RunRecord> ReadAll(SqliteCommand cmd)
    {
        var runs = new List<RunRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            runs.Add(new RunRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                JobId = reader.GetInt64(reader.GetOrdinal("job_id")),
                ScheduledForUtc = JobRepository.Parse(reader.GetString(reader.GetOrdinal("scheduled_for"))),
                StartedAtUtc = JobRepository.Parse(reader.GetString(reader.GetOrdinal("started_at"))),
                FinishedAtUtc = reader.IsDBNull(reader.GetOrdinal("finished_at"))
                    ? null
                    : JobRepository.Parse(reader.GetString(reader.GetOrdinal("finished_at"))),
                Attempt = Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("attempt"))),
                Status = (RunStatus)Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("status"))),
                HttpStatus = reader.IsDBNull(reader.GetOrdinal("http_status"))
                    ? null
                    : Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("http_status"))),
                Error = reader.IsDBNull(reader.GetOrdinal("error")) ? null : reader.GetString(reader.GetOrdinal("error"))
            });
        }
        return runs;
    }
}
