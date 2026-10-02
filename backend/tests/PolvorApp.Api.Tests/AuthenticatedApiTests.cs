using System.Net;
using System.Net.Http.Json;
using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests;

/// <summary>Spec platform "Authenticated API by default" and identity-access "Sessions" (anti-forgery).</summary>
public sealed class AuthenticatedApiTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_protected_endpoint_without_a_session_answers_401_problem_details()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.GetAsync("/api/account", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestContext.Current.CancellationToken);
        Assert.True(problem!.ContainsKey("traceId"));
        Assert.False(response.Headers.Contains("Location"));
    }

    [Theory]
    [InlineData("/api/health/live")]
    [InlineData("/api/health/ready")]
    [InlineData("/api/system/info")]
    [InlineData("/api/auth/antiforgery")]
    [InlineData("/api/openapi/v1.json")]
    public async Task Anonymous_endpoints_stay_open(string path)
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"{path} answered {(int)response.StatusCode}");
    }

    [Fact]
    public async Task An_unknown_api_route_reveals_no_data()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.GetAsync("/api/no-such-route", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_state_changing_request_without_an_antiforgery_token_is_rejected()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/logout", new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestContext.Current.CancellationToken);
        Assert.Equal("antiforgery.invalid", problem!["code"].ToString());
    }

    [Fact]
    public async Task A_state_changing_request_with_the_token_passes_the_antiforgery_check()
    {
        using var client = await _host.NewClientAsync();

        using var response = await client.PostAsJsonAsync("/api/auth/logout", new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task The_antiforgery_token_is_readable_by_the_ui_but_the_cookie_token_is_not()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.GetAsync("/api/auth/antiforgery", TestContext.Current.CancellationToken);

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var requestToken = Assert.Single(cookies, c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        Assert.DoesNotContain("httponly", requestToken, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", requestToken, StringComparison.OrdinalIgnoreCase);
        var cookieToken = Assert.Single(cookies, c => c.StartsWith("polvorapp.af=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookieToken, StringComparison.OrdinalIgnoreCase);
    }
}
