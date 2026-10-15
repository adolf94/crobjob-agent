using Api.Jobs;

namespace Api.Tests;

public class CronValidatorTests
{
    [Theory]
    [InlineData("0 */5 * * * *")]
    [InlineData("0 0 2 * * *")]
    [InlineData("0 30 2 * * 1-5")]
    [InlineData("*/10 * * * * *")]
    public void Valid_6_field_expression_parses(string expr)
    {
        var ok = CronValidator.TryParse(expr, out var parsed, out var error);
        Assert.True(ok, error);
        Assert.NotNull(parsed);
    }

    [Theory]
    [InlineData("*/5 * * * *")]
    [InlineData("30 2 * * 1")]
    public void Valid_5_field_expression_parses(string expr)
    {
        var ok = CronValidator.TryParse(expr, out var parsed, out var error);
        Assert.True(ok, error);
        Assert.NotNull(parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not cron")]
    [InlineData("99 * * * * *")]
    public void Invalid_expression_returns_error(string? expr)
    {
        var ok = CronValidator.TryParse(expr, out var parsed, out var error);
        Assert.False(ok);
        Assert.Null(parsed);
        Assert.NotNull(error);
    }
}
