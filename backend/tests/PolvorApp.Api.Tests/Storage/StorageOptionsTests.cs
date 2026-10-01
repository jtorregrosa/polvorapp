using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PolvorApp.Api.Platform.Storage;
using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests.Storage;

/// <summary>Spec: Private object storage (configuration validated at startup, values never logged).</summary>
public sealed class StorageOptionsTests
{
    private const string SecretSentinel = "storage-secret-sentinel";

    [Theory]
    [InlineData("Storage:ServiceUrl")]
    [InlineData("Storage:Bucket")]
    [InlineData("Storage:AccessKey")]
    [InlineData("Storage:SecretKey")]
    public async Task Startup_fails_naming_a_missing_storage_setting(string setting)
    {
        await using var factory = new ApiFactory(connectionString: null, settings: new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=polvorapp;Username=polvorapp;Password=x",
            [setting] = null,
        });

        var exception = factory.StartupFailure<OptionsValidationException>();

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Storage:ServiceUrl", "storage:9000")]
    [InlineData("Storage:ServiceUrl", "ftp://storage:9000")]
    [InlineData("Storage:ServiceUrl", "http://user:password@storage:9000")]
    [InlineData("Storage:Bucket", "Bad_Bucket")]
    [InlineData("Storage:Bucket", "ab")]
    [InlineData("Storage:ForcePathStyle", "sometimes")]
    [InlineData("Storage:ServerSideEncryption", "ROT13")]
    [InlineData("Storage:SweepIntervalMinutes", "0")]
    [InlineData("Storage:SweepIntervalMinutes", "often")]
    public async Task Startup_fails_naming_an_invalid_storage_setting_without_its_value(string setting, string value)
    {
        await using var factory = new ApiFactory(connectionString: null, settings: new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=polvorapp;Username=polvorapp;Password=x",
            [setting] = value,
        });

        var exception = factory.StartupFailure<OptionsValidationException>();

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plain_http_storage_is_refused_outside_local_environments()
    {
        await using var factory = new ApiFactory(
            connectionString: "Host=localhost;Database=polvorapp;Username=polvorapp;Password=x",
            environment: "Production",
            settings: new Dictionary<string, string?> { ["Storage:ServiceUrl"] = "http://storage.example:9000" });

        var exception = factory.StartupFailure<OptionsValidationException>();

        Assert.Contains("Storage:ServiceUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plain_http_to_a_storage_on_the_same_machine_is_allowed_everywhere()
    {
        await using var factory = new ApiFactory(
            connectionString: "Host=localhost;Database=polvorapp;Username=polvorapp;Password=x",
            environment: "Production",
            settings: new Dictionary<string, string?> { ["Storage:ServiceUrl"] = "http://127.0.0.1:9000" });

        var options = factory.Services.GetRequiredService<IOptions<StorageOptions>>().Value;

        Assert.Equal(new Uri("http://127.0.0.1:9000"), options.ServiceUrl);
    }

    [Fact]
    public async Task The_secret_key_never_reaches_the_failure_or_the_log()
    {
        await using var factory = new ApiFactory(connectionString: null, settings: new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=polvorapp;Username=polvorapp;Password=x",
            ["Storage:SecretKey"] = SecretSentinel,
            ["Storage:Bucket"] = "Invalid Bucket",
        });

        var exception = factory.StartupFailure<OptionsValidationException>();

        Assert.DoesNotContain(SecretSentinel, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Logs.Entries, e =>
            e.Message.Contains(SecretSentinel, StringComparison.Ordinal)
            || (e.Exception?.Contains(SecretSentinel, StringComparison.Ordinal) ?? false));
    }
}
