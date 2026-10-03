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

        // The logo route reads an existing logo: without one, its own FiringChief would get 404 too.
        using (var logo = await LogoRequests.UploadAsync(admin, own.Id, TestImages.Png(800, 400)))
        {
            Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        }

        // The comparsa list export (add-exports) reads the comparsa's order of an edition in progress.
        var edition = OrderData.NewEdition(2031, FestivalEditions.Contracts.EditionStatus.InProgress);
        await host.Services.SaveEditionsAsync(edition);
        await host.Services.SaveOrdersAsync(OrderData.NewOrder(edition, own.Id));
        var values = new RouteValues(own.Id, owner.Id, edition.Id);

        var routes = ComparsaRoutes(host.Services.GetRequiredService<EndpointDataSource>());

        Assert.Contains(new GuardedRoute("GET", "/api/comparsas/{id:guid}", AdminOnly: false), routes);
        Assert.Contains(new GuardedRoute("PUT", "/api/comparsas/{id:guid}/firing-chiefs/{userId:guid}", AdminOnly: true), routes);
        Assert.Contains(new GuardedRoute("GET", "/api/comparsas/{id:guid}/logo", AdminOnly: false), routes);
        Assert.Contains(new GuardedRoute("PUT", "/api/comparsas/{id:guid}/logo", AdminOnly: true) { Multipart = true }, routes);
        Assert.Contains(new GuardedRoute("GET", "/api/exports/editions/{editionId:guid}/comparsas/{comparsaId:guid}/{format}", AdminOnly: false), routes);
        Assert.Contains(new GuardedRoute("GET", "/api/distribution/editions/{editionId:guid}/comparsas/{comparsaId:guid}/proxy-candidates", AdminOnly: false), routes);
        foreach (var route in routes)
        {
            var refused = await StatusAsync(outsiderClient, route, values);
            Assert.True(refused is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"{route} answered {(int)refused} to a FiringChief of another comparsa.");

            // Positive control. A route that validates a body before the scope may answer 400 here:
            // the guard then fails, erring on the safe side, until it is taught a valid body.
            var reached = await StatusAsync(ownerClient, route, values);
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
        var error = Assert.Throws<InvalidOperationException>(() => PathOf(new GuardedRoute("GET", "/api/comparsas/{id}/members/{memberId}", false), new RouteValues(Guid.Empty, Guid.Empty, Guid.Empty)));

        Assert.Contains("memberId", error.Message, StringComparison.Ordinal);
    }

    private static List<GuardedRoute> ComparsaRoutes(EndpointDataSource source) =>
        [.. source.Endpoints.OfType<RouteEndpoint>()
            .Where(e => ComparsaChild().IsMatch(e.RoutePattern.RawText ?? string.Empty))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(method => new GuardedRoute(
                method,
                "/" + e.RoutePattern.RawText!.TrimStart('/'),
                e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == AuthorizationPolicies.Admin))
            {
                // Routing refuses another content type (415) before authorisation runs.
                Multipart = e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>()?.ContentTypes.Contains("multipart/form-data") == true,
            }))
            .Distinct()];

    private static string PathOf(GuardedRoute route, RouteValues values) =>
        RouteParameter().Replace(route.Template, match => match.Groups["name"].Value switch
        {
            "id" or "comparsaId" => values.ComparsaId.ToString(),
            "userId" => values.UserId.ToString(),
            "editionId" => values.EditionId.ToString(),
            "format" => "xlsx",
            var other => throw new InvalidOperationException($"Teach the guard a value for route parameter '{other}' in {route.Template}."),
        });

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, GuardedRoute route, RouteValues values)
    {
        using var request = new HttpRequestMessage(new HttpMethod(route.Method), PathOf(route, values));
        if (route.Multipart)
        {
            request.Content = new MultipartFormDataContent { { new ByteArrayContent(TestImages.Png(800, 400)), "file", "image.png" } };
        }
        else if (route.Method is "POST" or "PUT" or "PATCH")
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

    /// <summary>The values the guard puts in the route parameters it knows.</summary>
    private sealed record RouteValues(Guid ComparsaId, Guid UserId, Guid EditionId);

    /// <summary>A route to probe; <see cref="Multipart"/> routes are sent an image upload instead of JSON.</summary>
    private sealed record GuardedRoute(string Method, string Template, bool AdminOnly)
    {
        public bool Multipart { get; init; }
    }
}
