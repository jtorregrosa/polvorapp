using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Endpoints;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Guard for BR-12 (design D5, announced by D7 of add-identity-access): every route under
/// <c>/comparsas/{…}</c>, today's and any a later module adds, refuses a FiringChief of another
/// comparsa (403 or 404, never 2xx), while the comparsa's own FiringChief reaches every route that
/// is not Admin-only, which proves the refusal comes from the scope and not from missing data.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed partial class ComparsaScopeGuardTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Every_comparsa_route_refuses_a_firing_chief_of_another_comparsa()
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit);
        using var admin = await host.SignInAsync(await host.CreateUserAsync("admin.guardia@example.test", UserRole.Admin));
        var own = await CreateComparsaAsync(admin, "Comparsa Sintética Guardada");
        var other = await CreateComparsaAsync(admin, "Comparsa Sintética Vecina");
        var owner = await host.CreateUserAsync("jefe.propietario@example.test");
        var outsider = await host.CreateUserAsync("jefe.vecino@example.test");
        await host.AssignAsync(own.Id, owner.Id);
        await host.AssignAsync(other.Id, outsider.Id);
        using var ownerClient = await host.SignInAsync(owner);
        using var outsiderClient = await host.SignInAsync(outsider);

        var routes = ComparsaRoutes(host.Services.GetRequiredService<EndpointDataSource>());

        Assert.Contains(new GuardedRoute("GET", "/api/comparsas/{id:guid}", AdminOnly: false), routes);
        Assert.Contains(new GuardedRoute("PUT", "/api/comparsas/{id:guid}/firing-chiefs/{userId:guid}", AdminOnly: true), routes);
        foreach (var route in routes)
        {
            var refused = await StatusAsync(outsiderClient, route, own.Id, owner.Id);
            Assert.True(refused is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"{route} answered {(int)refused} to a FiringChief of another comparsa.");

            // Positive control. A route that validates a body before the scope may answer 400 here:
            // the guard then fails, erring on the safe side, until it is taught a valid body.
            var reached = await StatusAsync(ownerClient, route, own.Id, owner.Id);
            Assert.True(route.AdminOnly ? reached == HttpStatusCode.Forbidden : (int)reached is >= 200 and < 300, $"{route} answered {(int)reached} to its own FiringChief.");
        }
    }

    [Fact]
    public void The_guard_finds_every_parameterised_comparsa_route_and_knows_which_are_admin_only()
    {
        var source = new DefaultEndpointDataSource(
            Endpoint("/api/comparsas/{comparsaId:guid}/probe", "GET"),
            Endpoint("/api/comparsas/{id:guid}/settings", "POST", new AuthorizeAttribute(AuthorizationPolicies.Admin)),
            Endpoint("/api/comparsas", "GET"),
            Endpoint("/api/weapon-models/{id:guid}", "GET"));

        Assert.Equal(
            [new GuardedRoute("GET", "/api/comparsas/{comparsaId:guid}/probe", AdminOnly: false), new GuardedRoute("POST", "/api/comparsas/{id:guid}/settings", AdminOnly: true)],
            ComparsaRoutes(source));
    }

    [Fact]
    public void The_guard_refuses_to_guess_an_unknown_route_parameter()
    {
        var error = Assert.Throws<InvalidOperationException>(() => PathOf(new GuardedRoute("GET", "/api/comparsas/{id}/members/{memberId}", false), Guid.Empty, Guid.Empty));

        Assert.Contains("memberId", error.Message, StringComparison.Ordinal);
    }

    private static List<GuardedRoute> ComparsaRoutes(EndpointDataSource source) =>
        [.. source.Endpoints.OfType<RouteEndpoint>()
            .Where(e => ComparsaChild().IsMatch(e.RoutePattern.RawText ?? string.Empty))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(method => new GuardedRoute(
                method,
                "/" + e.RoutePattern.RawText!.TrimStart('/'),
                e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == AuthorizationPolicies.Admin))))
            .Distinct()];

    private static string PathOf(GuardedRoute route, Guid comparsaId, Guid userId) =>
        RouteParameter().Replace(route.Template, match => match.Groups["name"].Value switch
        {
            "id" or "comparsaId" => comparsaId.ToString(),
            "userId" => userId.ToString(),
            var other => throw new InvalidOperationException($"Teach the guard a value for route parameter '{other}' in {route.Template}."),
        });

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, GuardedRoute route, Guid comparsaId, Guid userId)
    {
        using var request = new HttpRequestMessage(new HttpMethod(route.Method), PathOf(route, comparsaId, userId));
        if (route.Method is "POST" or "PUT" or "PATCH")
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static async Task<ComparsaResponse> CreateComparsaAsync(HttpClient admin, string name)
    {
        using var response = await admin.PostAsync("/api/comparsas", new { name, side = "MOORISH" });
        return await ReadAsync<ComparsaResponse>(response);
    }

    private static RouteEndpoint Endpoint(string pattern, string method, params object[] metadata) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0, new EndpointMetadataCollection([new HttpMethodMetadata([method]), .. metadata]), pattern);

    /// <summary>Any route parameter directly after <c>/comparsas/</c>, whatever its name.</summary>
    [GeneratedRegex(@"/comparsas/\{[^}]+\}")]
    private static partial Regex ComparsaChild();

    [GeneratedRegex(@"\{(?<name>\w+)(:[^}]*)?\}")]
    private static partial Regex RouteParameter();

    private sealed record GuardedRoute(string Method, string Template, bool AdminOnly);
}
