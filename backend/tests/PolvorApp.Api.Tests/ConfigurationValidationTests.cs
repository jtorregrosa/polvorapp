using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests;

public sealed class ConfigurationValidationTests
{
    [Fact]
    public async Task Startup_fails_naming_the_missing_database_connection_setting()
    {
        await using var factory = new ApiFactory(connectionString: null);

        var exception = factory.StartupFailure<OptionsValidationException>();

        Assert.Contains("ConnectionStrings:Postgres", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <see cref="ApiFactory.StartupFailure{TException}"/> falls back on this log entry when the
    /// factory loses the race with the API's own thread and sees disposed services instead.
    /// </summary>
    [Fact]
    public async Task The_host_logs_the_startup_failure_with_its_exception_before_disposing()
    {
        await using var factory = new ApiFactory(connectionString: null);

        var exception = factory.StartupFailure<OptionsValidationException>();

        Assert.Contains(factory.Logs.Entries, e => e.Level == LogLevel.Error && e.Message == "Hosting failed to start" && e.Thrown is OptionsValidationException);
        Assert.Contains("ConnectionStrings:Postgres", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_failure_does_not_disclose_the_invalid_setting_value()
    {
        const string invalidValue = "not-a-connection-string-sentinel";
        await using var factory = new ApiFactory(invalidValue);

        var exception = factory.StartupFailure<OptionsValidationException>();

        Assert.Contains("ConnectionStrings:Postgres", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidValue, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Logs.Entries, e =>
            e.Message.Contains(invalidValue, StringComparison.Ordinal)
            || (e.Exception?.Contains(invalidValue, StringComparison.Ordinal) ?? false));
    }
}
