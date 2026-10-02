using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests;

public sealed class SecurityHeadersTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("/api/system/info")]
    [InlineData("/api/health/live")]
    [InlineData("/api/does-not-exist")]
    public async Task Api_responses_carry_the_security_headers(string path)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        var csp = Header(response, "Content-Security-Policy");
        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Equal("camera=(self), geolocation=(), microphone=()", Header(response, "Permissions-Policy"));
    }

    [Fact]
    public async Task Cross_origin_requests_get_no_cors_headers()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/system/info");
        request.Headers.Add("Origin", "https://attacker.example");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Cross_origin_preflight_is_not_allowed()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/system/info");
        request.Headers.Add("Origin", "https://attacker.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static string Header(HttpResponseMessage response, string name) =>
        string.Join(",", response.Headers.TryGetValues(name, out var values) ? values : []);
}
