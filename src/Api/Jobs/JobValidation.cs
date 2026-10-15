using Api.Database;
using TimeZoneConverter;

namespace Api.Jobs;

public static class JobValidation
{
    public static string[] Validate(JobInputDto input) => Validate(input, null);

    public static string[] ValidateWithExisting(JobInputDto input, CronJob? existing) => Validate(input, existing);

    private static string[] Validate(JobInputDto input, CronJob? existing)    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(input.Name))
            errors.Add("Name is required");

        if (string.IsNullOrWhiteSpace(input.TargetUrl) ||
            !Uri.TryCreate(input.TargetUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            errors.Add("Target URL must be a valid absolute http(s) URL");

        if (!CronValidator.TryParse(input.CronExpression, out _, out var cronError))
            errors.Add(cronError ?? "Invalid cron expression");

        if (string.IsNullOrWhiteSpace(input.Timezone) || !IsKnownTimeZone(input.Timezone.Trim()))
            errors.Add("Timezone must be a valid IANA or Windows timezone id");

        if (input.CatchupDays < -1 || input.CatchupDays > 30)
            errors.Add("Catch-up must be -1 (catch up missed runs once), 0 (skip missed runs), or 1-30 (back-fill window days)");

        if (input.MaxRetries is < 0 or > 20)
            errors.Add("Max retries must be between 0 and 20");

        if (string.Equals(input.AuthMode, "arauth", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(input.AuthScope))
            errors.Add("Auth scope is required for arauth mode (e.g. api://target-client/run:job)");

        return errors.ToArray();
    }

    private static bool IsKnownTimeZone(string id)
    {
        try
        {
            TZConvert.GetTimeZoneInfo(id);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
