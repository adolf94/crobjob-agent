using Api.Database;
using Api.Scheduler;

namespace Api.Jobs;

public static class JobEndpoints
{
    public static void MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs");

        group.MapGet("/", async (JobRepository jobs, RunRepository runs, TimeProvider time) =>
        {
            var all = jobs.GetAll();
            var now = time.GetUtcNow();
            var dtos = all.Select(j =>
            {
                var next = j.Enabled ? ScheduleMath.GetNextRun(j.CronExpression, j.Timezone, now.UtcDateTime) : null;
                return JobDtos.FromJob(j, next, runs.GetLatestForJob(j.Id));
            });
            return Results.Ok(dtos);
        });

        group.MapGet("/{id:long}", (long id, JobRepository jobs, RunRepository runs, TimeProvider time) =>
        {
            var job = jobs.GetById(id);
            if (job is null)
                return Results.NotFound();

            var next = job.Enabled ? ScheduleMath.GetNextRun(job.CronExpression, job.Timezone, time.GetUtcNow().UtcDateTime) : null;
            return Results.Ok(JobDtos.FromJob(job, next, runs.GetLatestForJob(id)));
        });

        group.MapGet("/{id:long}/runs", (long id, int? limit, RunRepository runs) =>
        {
            return Results.Ok(runs.GetRecentForJob(id, limit is > 0 and <= 200 ? limit.Value : 50));
        });

        group.MapPost("/", (JobInputDto input, JobRepository jobs, TimeProvider time) =>
        {
            var errors = JobValidation.Validate(input);
            if (errors.Length > 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["input"] = errors });

            var job = JobDtos.ToJob(input, time.GetUtcNow().UtcDateTime);
            try
            {
                jobs.Insert(job);
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                return Results.Problem("A job with that name already exists", statusCode: StatusCodes.Status409Conflict);
            }
            return Results.Created($"/api/jobs/{job.Id}", JobDtos.FromJob(job, null, null));
        });

        group.MapPut("/{id:long}", (long id, JobInputDto input, JobRepository jobs, RunRepository runs, TimeProvider time) =>
        {
            var existing = jobs.GetById(id);
            if (existing is null)
                return Results.NotFound();

            if (string.IsNullOrWhiteSpace(input.TargetUrl) || input.TargetUrl.Trim() == JobDtos.MaskTargetUrl(existing.TargetUrl))
                input.TargetUrl = existing.TargetUrl;

            var errors = JobValidation.ValidateWithExisting(input, existing);
            if (errors.Length > 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["input"] = errors });

            var updated = JobDtos.ToJob(input, time.GetUtcNow().UtcDateTime);
            if (updated.AuthMode != TargetAuthMode.ArAuth)
            {
                updated.AuthMode = TargetAuthMode.None;
                updated.AuthScope = null;
                updated.AuthClientId = null;
                updated.AuthClientSecret = null;
            }
            updated.Id = existing.Id;
            updated.CreatedAtUtc = existing.CreatedAtUtc;
            updated.LastDispatchedTickUtc = existing.LastDispatchedTickUtc;

            jobs.Update(updated);
            return Results.Ok(JobDtos.FromJob(updated, null, runs.GetLatestForJob(id)));
        });

        group.MapDelete("/{id:long}", (long id, JobRepository jobs) =>
        {
            if (jobs.GetById(id) is null)
                return Results.NotFound();
            jobs.Delete(id);
            return Results.NoContent();
        });

        group.MapPost("/{id:long}/run", async (long id, JobRepository jobs, JobDispatcher dispatcher) =>
        {
            var job = jobs.GetById(id);
            if (job is null)
                return Results.NotFound();

            var result = await dispatcher.DispatchAsync(job, DateTime.UtcNow, CancellationToken.None);
            return Results.Ok(new
            {
                success = result.Success,
                attempts = result.Attempts,
                http_status = result.HttpStatus,
                error = result.Error
            });
        });
    }
}
