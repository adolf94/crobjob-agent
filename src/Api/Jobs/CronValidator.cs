using Cronos;

namespace Api.Jobs;

public static class CronValidator
{
    public static bool TryParse(string? expression, out CronExpression? parsed, out string? error)
    {
        parsed = null;
        error = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Cron expression is required";
            return false;
        }

        try
        {
            parsed = CronExpression.Parse(expression.Trim(), CronFormat.IncludeSeconds);
            return true;
        }
        catch (CronFormatException)
        {
            try
            {
                parsed = CronExpression.Parse(expression.Trim());
                return true;
            }
            catch (CronFormatException ex)
            {
                error = $"Invalid cron expression: {ex.Message}";
                return false;
            }
        }
    }
}
