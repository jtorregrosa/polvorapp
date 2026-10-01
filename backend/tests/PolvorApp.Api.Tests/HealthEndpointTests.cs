using System.Net;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests;

[Collection(PostgresGroup.Name)]
public sealed class HealthEndpointTests(PostgresFixture postgres)
{
    // Nothing listens on port 1: the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=polvorapp;Username=polvorapp;Password=unreachable-sentinel;Timeout=2";

    // Nothing listens on port 1 either: the storage is unreachable.
    private const string UnreachableStorage = "http://127.0.0.1:1";

    [Fact]
    public async Task Readiness_is_healthy_when_the_database_and_the_storage_are_reachable()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        var checks = body.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
        Assert.Equal(new Dictionary<string, string?> { ["database"] = "Healthy", ["storage"] = "Healthy" }, checks);
    }

    [Fact]
    public async Task Readiness_is_unhealthy_naming_the_storage_check_without_host_or_bucket()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, settings: new Dictionary<string, string?>
        {
            ["Storage:ServiceUrl"] = UnreachableStorage,
            ["Storage:Bucket"] = "bucket-name-sentinel",
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(raw);
        Assert.Equal("Unhealthy", body.RootElement.GetProperty("status").GetString());
        var storage = body.RootElement.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "storage");
        Assert.Equal("Unhealthy", storage.GetProperty("status").GetString());
        Assert.DoesNotContain("127.0.0.1", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("bucket-name-sentinel", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("exception", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Liveness_is_healthy_even_when_the_storage_is_unreachable()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, settings: new Dictionary<string, string?> { ["Storage:ServiceUrl"] = UnreachableStorage });
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_is_unhealthy_naming_the_database_check_without_connection_details()
    {
        await using var factory = new ApiFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(raw);
        Assert.Equal("Unhealthy", body.RootElement.GetProperty("status").GetString());
        var check = body.RootElement.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "database");
        Assert.Equal("Unhealthy", check.GetProperty("status").GetString());
        Assert.Equal(2, check.EnumerateObject().Count());
        Assert.DoesNotContain("127.0.0.1", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("unreachable-sentinel", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("exception", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Liveness_is_healthy_even_when_the_database_is_unreachable()
    {
        await using var factory = new ApiFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Empty(body.RootElement.GetProperty("checks").EnumerateArray());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
}
