using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Security;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Spec "Sign-in with two-factor authentication": sign-in endpoints are rate limited per client address.</summary>
public sealed class RateLimitTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private const int Limit = 3;
    private const int EmailLimit = 2;

    /// <summary>Endpoints under the sign-in limit (spec: sign-in, enrolment, invitation, reset and step-ups).</summary>
    private static readonly string[] AuthLimited =
    [
        "POST /api/auth/login", "POST /api/auth/login/second-factor", "POST /api/auth/login/recovery-code",
        "GET /api/auth/enrolment", "POST /api/auth/enrolment", "GET /api/auth/invitations/validate",
        "POST /api/auth/invitations/accept", "POST /api/auth/password/reset",
        "POST /api/account/password", "POST /api/account/recovery-codes",
    ];
    private const string PeerHeader = "X-Test-Peer";
    private IdentityTestHost _host = null!;

    /// <summary>TestServer has no peer address; this sets one before the real pipeline runs.</summary>
    private sealed class PeerAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[PeerHeader], out var peer))
                {
                    context.Connection.RemoteIpAddress = peer;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit, new Dictionary<string, string?>
        {
            ["RateLimits:Auth:PermitLimit"] = Limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimits:AuthEmail:PermitLimit"] = EmailLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ForwardedHeaders:KnownNetworks"] = "10.0.0.0/8",
        }, services => services.AddSingleton<IStartupFilter, PeerAddressStartupFilter>());
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Exceeding_the_sign_in_limit_answers_429_problem_details()
    {
        using var client = await _host.NewClientAsync();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt <= Limit; attempt++)
        {
            using var response = await LoginAsync(client, peer: "198.51.100.1", forwardedFor: null);
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            }
        }

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.Unauthorized, Limit), HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_each_forwarded_client_has_its_own_limit()
    {
        using var client = await _host.NewClientAsync();

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            using var exhaust = await LoginAsync(client, peer: "10.0.0.5", forwardedFor: "203.0.113.10");
        }

        using var sameClient = await LoginAsync(client, peer: "10.0.0.5", forwardedFor: "203.0.113.10");
        using var otherClient = await LoginAsync(client, peer: "10.0.0.5", forwardedFor: "203.0.113.11");
        Assert.Equal(HttpStatusCode.TooManyRequests, sameClient.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherClient.StatusCode);
    }

    [Fact]
    public async Task An_untrusted_peer_cannot_escape_the_limit_with_a_forged_header()
    {
        using var client = await _host.NewClientAsync();

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            using var exhaust = await LoginAsync(client, peer: "198.51.100.7", forwardedFor: $"203.0.113.{attempt}");
        }

        using var forged = await LoginAsync(client, peer: "198.51.100.7", forwardedFor: "203.0.113.99");
        Assert.Equal(HttpStatusCode.TooManyRequests, forged.StatusCode);
    }

    /// <summary>Every sign-in, recovery and step-up endpoint carries its limit, not only the login.</summary>
    [Fact]
    public void Every_sign_in_and_recovery_endpoint_is_rate_limited()
    {
        var policies = _host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => (Name: $"{method} {endpoint.RoutePattern.RawText}", Policy: endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName)))
            .ToDictionary(e => e.Name, e => e.Policy, StringComparer.Ordinal);

        Assert.Multiple(
            [.. AuthLimited.Select(name => (Action)(() => Assert.Equal((name, RateLimitPolicies.Auth), (name, policies.GetValueOrDefault(name))))),
            () => Assert.Equal(RateLimitPolicies.AuthEmail, policies.GetValueOrDefault("POST /api/auth/password/forgot"))]);
    }

    [Fact]
    public async Task Password_reset_requests_have_their_own_stricter_limit()
    {
        using var client = await _host.NewClientAsync();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt <= EmailLimit; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/password/forgot")
            {
                Content = JsonContent.Create(new { email = $"nadie{attempt}@example.test" }),
            };
            request.Headers.Add(PeerHeader, "198.51.100.44");
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.Accepted, EmailLimit), HttpStatusCode.TooManyRequests], statuses);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string peer, string? forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "nadie@example.test", password = "no-importa-1234" }),
        };
        request.Headers.Add(PeerHeader, peer);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
