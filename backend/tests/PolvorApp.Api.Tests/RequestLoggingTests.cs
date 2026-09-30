using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests;

[Collection(PostgresGroup.Name)]
public sealed class RequestLoggingTests(PostgresFixture postgres)
{
    private const string RequestLogCategory = "PolvorApp.Api.Platform.Diagnostics.RequestLoggingMiddleware";

    [Fact]
    public async Task Response_trace_id_header_matches_the_request_log_entry()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/system/info", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var traceId = Assert.Single(response.Headers.GetValues("X-Trace-Id"));
        Assert.Matches("^[0-9a-f]{32}$", traceId);
        var entry = Assert.Single(factory.Logs.Entries, e => e.Category == RequestLogCategory);
        Assert.Equal(traceId, entry.State["TraceId"]);
        Assert.Equal("GET", entry.State["Method"]);
        Assert.Equal("/api/system/info", entry.State["Path"]);
        Assert.Equal(200, entry.State["StatusCode"]);
    }

    [Fact]
    public async Task Query_string_values_are_never_logged()
    {
        const string sentinel = "query-value-sentinel";
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        await client.GetAsync(new Uri($"/api/system/info?nationalId={sentinel}", UriKind.Relative), TestContext.Current.CancellationToken);
        await client.GetAsync(new Uri($"/api/unknown/{sentinel}?q={sentinel}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Contains(factory.Logs.Entries, e => e.Category == RequestLogCategory);
        Assert.DoesNotContain(factory.Logs.Entries, e =>
            e.Message.Contains(sentinel, StringComparison.Ordinal)
            || e.State.Values.Any(v => v?.ToString()?.Contains(sentinel, StringComparison.Ordinal) ?? false));
    }

    [Fact]
    public async Task Log_scopes_never_carry_the_raw_request_path()
    {
        const string sentinel = "scope-path-sentinel";
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        await client.GetAsync(new Uri($"/api/unknown/{sentinel}", UriKind.Relative), TestContext.Current.CancellationToken);

        var entry = Assert.Single(factory.Logs.Entries, e => e.Category == RequestLogCategory);
        Assert.Contains(entry.Scopes, scope => scope.ContainsKey("TraceId"));
        Assert.DoesNotContain(factory.Logs.Entries, e =>
            e.Scopes.Any(scope => scope.Values.Any(v => v?.ToString()?.Contains(sentinel, StringComparison.Ordinal) ?? false)));
    }

    [Fact]
    public async Task Requests_that_throw_are_logged_as_500_in_development()
    {
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            configureServices: services => services.AddSingleton<IStartupFilter, ThrowingEndpointStartupFilter>());
        using var client = factory.CreateClient();

        await client.GetAsync(new Uri(ThrowingEndpointStartupFilter.Path, UriKind.Relative), TestContext.Current.CancellationToken);

        var entry = Assert.Single(factory.Logs.Entries, e => e.Category == RequestLogCategory);
        Assert.Equal(500, entry.State["StatusCode"]);
    }

    [Fact]
    public async Task Unmatched_paths_are_logged_without_the_raw_path()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        await client.GetAsync(new Uri("/api/unknown/path-segment-sentinel", UriKind.Relative), TestContext.Current.CancellationToken);

        var entry = Assert.Single(factory.Logs.Entries, e => e.Category == RequestLogCategory);
        Assert.Equal("(unmatched)", entry.State["Path"]);
        Assert.Equal(404, entry.State["StatusCode"]);
    }
}
