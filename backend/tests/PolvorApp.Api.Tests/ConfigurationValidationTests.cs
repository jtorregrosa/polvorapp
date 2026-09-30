using Microsoft.Extensions.Options;
using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests;

public sealed class ConfigurationValidationTests
{
    [Fact]
    public async Task Startup_fails_naming_the_missing_database_connection_setting()
    {
        await using var factory = new ApiFactory(connectionString: null);

        var exception = Assert.ThrowsAny<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:Postgres", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_failure_does_not_disclose_the_invalid_setting_value()
    {
        const string invalidValue = "not-a-connection-string-sentinel";
        await using var factory = new ApiFactory(invalidValue);

        var exception = Assert.ThrowsAny<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:Postgres", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidValue, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Logs.Entries, e =>
            e.Message.Contains(invalidValue, StringComparison.Ordinal)
            || (e.Exception?.Contains(invalidValue, StringComparison.Ordinal) ?? false));
    }
}
