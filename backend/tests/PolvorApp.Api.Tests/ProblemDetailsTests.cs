using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests;

[Collection(PostgresGroup.Name)]
public sealed class ProblemDetailsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Unknown_api_route_returns_404_problem_details_with_trace_id()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/no-such-route", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("title").GetString()));
        Assert.Matches("^[0-9a-f]{32}$", body.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Unhandled_exception_in_production_hides_exception_details()
    {
        await using var factory = ThrowingFactory("Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(ThrowingEndpointStartupFilter.Path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(raw);
        Assert.Matches("^[0-9a-f]{32}$", body.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain(ThrowingEndpointStartupFilter.ExceptionMessage, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), raw, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unhandled_exception_in_development_includes_exception_details()
    {
        await using var factory = ThrowingFactory("Development");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ThrowingEndpointStartupFilter.Path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(ThrowingEndpointStartupFilter.ExceptionMessage, raw, StringComparison.Ordinal);
    }

    private ApiFactory ThrowingFactory(string environment) => new(
        postgres.ConnectionString,
        environment,
        configureServices: services => services.AddSingleton<IStartupFilter, ThrowingEndpointStartupFilter>());

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
}
