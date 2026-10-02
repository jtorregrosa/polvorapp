using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Guard for BR-12 on the registry (design D6): every route under <c>/arquebusiers/{…}</c>, today's
/// and any later one, answers 404 (403 when Admin-only) to a FiringChief of another comparsa and to one
/// without assignments, while the arquebusier's own FiringChief reaches every route that is not
/// Admin-only. Write routes validate their body before the scope, so the guard sends a valid body;
/// a new write route fails here until it is taught one.
/// </summary>
public sealed partial class RegistryScopeGuardTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Every_arquebusier_route_refuses_firing_chiefs_outside_the_scope()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO GUARDADO");
        await registry.Services.SaveCatalogAsync(model);
        var target = await registry.RegisterAsync(registry.Own.Id);
        var weapon = RegistryData.NewOwnedWeapon(target.GetProperty("id").GetGuid(), model.Id, "SINT-0900");
        await registry.Services.SaveRegistryAsync(weapon);

        // An ID photo, so its own FiringChief can read it (add-arquebusier-photos).
        using (var upload = await PhotoRequests.UploadAsync(registry.Admin, target.GetProperty("id").GetGuid(), "id", TestImages.Jpeg(600, 800)))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }
        var ids = new Dictionary<string, Guid>
        {
            ["id"] = target.GetProperty("id").GetGuid(),
            ["weaponId"] = weapon.Id,
            ["modelId"] = model.Id,
        };

        var outsider = await registry.Host.CreateUserAsync("jefe.ajeno@example.test", UserRole.FiringChief);
        await registry.Host.AssignAsync(registry.Other.Id, outsider.Id);
        using var outsiderClient = await registry.Host.SignInAsync(outsider);
        using var unassignedClient = await registry.Host.SignInAsync(await registry.Host.CreateUserAsync("jefe.sin.asignar@example.test", UserRole.FiringChief));

        var routes = ArquebusierRoutes(registry.Services.GetRequiredService<EndpointDataSource>());

        Assert.Contains(new GuardedRoute("GET", "/api/arquebusiers/{id:guid}", AdminOnly: false), routes);
        Assert.Contains(new GuardedRoute("PUT", "/api/arquebusiers/{id:guid}", AdminOnly: false), routes);
        foreach (var route in routes)
        {
            foreach (var (client, who) in new[] { (outsiderClient, "a FiringChief of another comparsa"), (unassignedClient, "a FiringChief without assignments") })
            {
                // Out of scope looks like unknown (404), so existence never leaks; only an Admin-only
                // route refuses every FiringChief up front (403).
                var refused = await StatusAsync(client, registry, route, ids);
                var expected = route.AdminOnly ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound;
                Assert.True(refused == expected, $"{route} answered {(int)refused} to {who}, not {(int)expected}.");
            }

            var reached = await StatusAsync(registry.FiringChief, registry, route, ids);
            Assert.True(
                route.AdminOnly ? reached == HttpStatusCode.Forbidden : (int)reached is >= 200 and < 300,
                $"{route} answered {(int)reached} to the arquebusier's own FiringChief.");
        }
    }

    /// <summary>
    /// Routes under /arquebusiers without an arquebusier id are not exercised by the guard above, so
    /// each must be classified here: a new one (e.g. an import) fails until its scoping is reviewed.
    /// </summary>
    [Fact]
    public async Task Every_collection_level_arquebusier_route_is_classified()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        string[] reviewed =
        [
            "GET /api/arquebusiers/",   // scoped list: filtered by the caller's comparsas
            "POST /api/arquebusiers/",  // registration: the comparsa must be in the caller's scope
        ];

        var unclassified = registry.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("arquebusiers", StringComparison.Ordinal) == true
                && !ArquebusierChild().IsMatch(e.RoutePattern.RawText))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => $"{method} /{e.RoutePattern.RawText!.Trim('/')}/"))
            .Except(reviewed)
            .ToList();

        Assert.Empty(unclassified);
    }

    /// <summary>
    /// Every parameterised route, deletions last (the arquebusier's own one at the very end) so the
    /// positive control of one route does not remove what the next one needs.
    /// </summary>
    private static List<GuardedRoute> ArquebusierRoutes(EndpointDataSource source) =>
        [.. source.Endpoints.OfType<RouteEndpoint>()
            .Where(e => ArquebusierChild().IsMatch(e.RoutePattern.RawText ?? string.Empty))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(method => new GuardedRoute(
                method,
                "/" + e.RoutePattern.RawText!.Trim('/'),
                e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == AuthorizationPolicies.Admin))))
            .Distinct()
            .OrderBy(r => r.Method == "DELETE")
            .ThenByDescending(r => r.Template.Length)];

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, RegistryTestHost registry, GuardedRoute route, Dictionary<string, Guid> ids)
    {
        using var request = new HttpRequestMessage(new HttpMethod(route.Method), PathOf(route, ids));
        if (route.Method == "PUT" && route.Template.Contains("/photos/", StringComparison.Ordinal))
        {
            // A photo upload is a multipart form with a valid image.
            var file = new ByteArrayContent(TestImages.Jpeg(600, 800));
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            request.Content = new MultipartFormDataContent { { file, "file", "photo.jpg" } };
        }
        else if (route.Method is "POST" or "PUT" or "PATCH")
        {
            request.Content = JsonContent.Create(await ValidBodyAsync(registry, route, ids));
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    /// <summary>A body the route accepts from the arquebusier's own FiringChief, built from current data.</summary>
    private static async Task<object> ValidBodyAsync(RegistryTestHost registry, GuardedRoute route, Dictionary<string, Guid> ids)
    {
        var current = await ReadAsync<JsonElement>(await registry.Admin.GetAsync($"/api/arquebusiers/{ids["id"]}", TestContext.Current.CancellationToken));
        var key = $"{route.Method} {route.Template}";
        switch (key)
        {
            case "PUT /api/arquebusiers/{id:guid}":
                var edit = ArquebusierEditingTests.EditOf(current);
                edit["phone"] = "+34 600 000 " + Random.Shared.Next(100, 999).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return edit;
            case "POST /api/arquebusiers/{id:guid}/transfer":
                return new { comparsaId = registry.Other.Id };
            case "POST /api/arquebusiers/{id:guid}/owned-weapons":
                return new { weaponModelId = ids["modelId"], weaponNumber = "1", ownershipGuideNumber = "GUARD-" + Guid.NewGuid().ToString("N")[..8] };
            case "PUT /api/arquebusiers/{id:guid}/owned-weapons/{weaponId:guid}":
                var weapon = current.GetProperty("ownedWeapons").EnumerateArray().Single(w => w.GetProperty("id").GetGuid() == ids["weaponId"]);
                return new
                {
                    weaponModelId = ids["modelId"],
                    weaponNumber = Random.Shared.Next(1, 9999).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ownershipGuideNumber = weapon.GetProperty("ownershipGuideNumber").GetString(),
                    version = weapon.GetProperty("version").GetUInt32(),
                };
            default:
                throw new InvalidOperationException($"Teach the guard a valid body for {key}.");
        }
    }

    private static string PathOf(GuardedRoute route, Dictionary<string, Guid> ids) =>
        RouteParameter().Replace(route.Template, match => match.Groups["name"].Value switch
        {
            // The photo kind slug: the ID photo, which the guard uploads first.
            "kind" => "id",
            var name when ids.TryGetValue(name, out var id) => id.ToString(),
            var name => throw new InvalidOperationException($"Teach the guard a value for route parameter '{name}' in {route.Template}."),
        });

    /// <summary>Any route parameter directly after <c>/arquebusiers/</c>, whatever its name.</summary>
    [GeneratedRegex(@"/arquebusiers/\{[^}]+\}")]
    private static partial Regex ArquebusierChild();

    [GeneratedRegex(@"\{(?<name>\w+)(:[^}]*)?\}")]
    private static partial Regex RouteParameter();

    private sealed record GuardedRoute(string Method, string Template, bool AdminOnly);
}
