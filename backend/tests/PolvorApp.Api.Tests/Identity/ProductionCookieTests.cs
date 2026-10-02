using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PolvorApp.Api.Platform.Security;
using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>SEC-01 / spec "Sessions": outside local environments every cookie is Secure; rate limits per IPv6 /64.</summary>
public sealed class ProductionCookieTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(
        postgres,
        mailpit,
        new Dictionary<string, string?> { ["Email:Security"] = "StartTls", ["App:PublicBaseUrl"] = "https://polvorapp.example" },
        environment: "Production");

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Every_sign_in_cookie_is_secure_in_production()
    {
        var user = await _host.CreateUserAsync("produccion@example.test");
        // Production cookies are Secure and antiforgery refuses plain http, so talk https to the test server.
        using var client = _host.Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

        using var antiforgery = await client.GetAsync("/api/auth/antiforgery", TestContext.Current.CancellationToken);
        var cookies = antiforgery.Headers.GetValues("Set-Cookie").ToList();
        Assert.All(cookies, cookie => Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase));

        var token = cookies.Single(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..];
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = user.Email, password = IdentityTestHost.Password }),
        };
        request.Headers.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(token));
        request.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => c.Split(';')[0])));
        using var login = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var pending = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("polvorapp.2fa=", StringComparison.Ordinal));
        Assert.Contains("secure", pending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", pending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", pending, StringComparison.OrdinalIgnoreCase);

        // The code step issues the session and remembered-device cookies: same flags.
        using var second = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/second-factor")
        {
            Content = JsonContent.Create(new { code = _host.NextCode(user), rememberDevice = true }),
        };
        second.Headers.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(token));
        second.Headers.Add("Cookie", string.Join("; ", cookies.Append(pending).Select(c => c.Split(';')[0])));
        using var signedIn = await client.SendAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        var issued = signedIn.Headers.GetValues("Set-Cookie").Where(c => !c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Contains(issued, c => c.StartsWith("polvorapp.session=", StringComparison.Ordinal));
        Assert.True(issued.Count >= 2, string.Join(" | ", issued.Select(c => c.Split('=')[0])));
        Assert.All(issued, cookie =>
        {
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData("2001:db8:1:2:aaaa::1", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:bbbb::9", "2001:db8:1:2::/64")]
    [InlineData("::ffff:198.51.100.7", "198.51.100.7")]
    [InlineData("198.51.100.7", "198.51.100.7")]
    public void Rate_limits_group_ipv6_clients_by_their_64_prefix(string address, string key) =>
        Assert.Equal(key, RateLimits.ClientKey(IPAddress.Parse(address)));
}
