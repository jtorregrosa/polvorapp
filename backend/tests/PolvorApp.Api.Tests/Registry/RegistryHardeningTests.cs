using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Endpoints;
using PolvorApp.ArquebusierRegistry.Import;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Time;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Design D5, D10 (locking and race rules) and D11: lost races map to their blocking outcomes, the row
/// locks conflict exactly as designed, a lock timeout is retryable, and oversized or too frequent
/// requests are refused.
/// </summary>
public sealed class RegistryHardeningTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Two_registrations_of_the_same_federation_id_at_once_store_one()
    {
        BarrierAuditTrail barrier = null!;
        await using var registry = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => barrier = BarrierAuditTrail.Decorate(services, "ArquebusierRegistered", parties: 2));
        var first = RegistryTestHost.NewArquebusier(registry.Own.Id);
        var second = RegistryTestHost.NewArquebusier(registry.Own.Id);
        second["federationId"] = first["federationId"];

        var responses = await Task.WhenAll(new[] { first, second }.Select(body => registry.Admin.PostAsync("/api/arquebusiers", body)));

        Assert.Equal(2, barrier.Arrived);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        await AssertProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.Created), HttpStatusCode.Conflict, "arquebusiers.federationIdTaken");
    }

    [Fact]
    public async Task Two_weapons_with_the_same_guide_added_at_once_store_one()
    {
        BarrierAuditTrail barrier = null!;
        await using var registry = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => barrier = BarrierAuditTrail.Decorate(services, "OwnedWeaponAdded", parties: 2));
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO CARRERA");
        await registry.Services.SaveCatalogAsync(model);
        var owners = new[] { await registry.RegisterAsync(registry.Own.Id), await registry.RegisterAsync(registry.Other.Id) };

        var responses = await Task.WhenAll(owners.Select(owner => registry.Admin.PostAsJsonAsync(
            $"/api/arquebusiers/{owner.GetProperty("id").GetGuid()}/owned-weapons",
            new { weaponModelId = model.Id, weaponNumber = "1", ownershipGuideNumber = "SINT-RACE" },
            TestContext.Current.CancellationToken)));

        Assert.Equal(2, barrier.Arrived);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        await AssertProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.Created), HttpStatusCode.Conflict, "ownedWeapons.guideTaken");
    }

    [Fact]
    public async Task A_comparsa_deleted_between_the_catalog_lookup_and_the_insert_is_not_found()
    {
        DeletingCatalogDirectory deleting = null!;
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit, services => deleting = DeletingCatalogDirectory.Decorate(services));
        deleting.Target = registry.Own.Id;

        using var response = await registry.Admin.PostAsync("/api/arquebusiers", RegistryTestHost.NewArquebusier(registry.Own.Id));

        Assert.True(deleting.Deleted);
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "arquebusiers.comparsaNotFound");
    }

    [Theory]
    [InlineData("update", "FOR NO KEY UPDATE NOWAIT", true)]
    [InlineData("update", "FOR KEY SHARE NOWAIT", true)]
    [InlineData("change", "FOR NO KEY UPDATE NOWAIT", true)]
    [InlineData("change", "FOR KEY SHARE NOWAIT", false)]
    [InlineData("keyShare", "FOR UPDATE NOWAIT", true)]
    [InlineData("keyShare", "FOR NO KEY UPDATE NOWAIT", false)]
    [InlineData("keyShare", "FOR KEY SHARE NOWAIT", false)]
    public async Task The_row_locks_conflict_as_designed(string held, string attempted, bool conflicts)
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var arquebusier = RegistryData.NewArquebusier(registry.Own.Id);
        await registry.Services.SaveRegistryAsync(arquebusier);
        var ct = TestContext.Current.CancellationToken;

        await using var scope = registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        await using var transaction = await db.BeginWriteAsync(ct);
        var locked = held switch
        {
            "update" => await db.LockArquebusierForUpdateAsync(arquebusier.Id, ComparsaAccess.All, ct),
            "change" => await db.LockArquebusierForChangeAsync(arquebusier.Id, ComparsaAccess.All, ct),
            _ => await db.LockArquebusierForKeyShareAsync(arquebusier.Id, ComparsaAccess.All, ct),
        };
        Assert.True(locked);

        await using var connection = await scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand($"SELECT 1 FROM registry.arquebusiers WHERE id = '{arquebusier.Id}' {attempted}", connection);
        var error = await Record.ExceptionAsync(() => command.ExecuteScalarAsync(ct));

        Assert.Equal(conflicts, error is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable });
        if (!conflicts)
        {
            Assert.Null(error);
        }
    }

    [Fact]
    public async Task A_row_outside_the_scope_is_never_locked()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var arquebusier = RegistryData.NewArquebusier(registry.Other.Id);
        await registry.Services.SaveRegistryAsync(arquebusier);

        await using var scope = registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        await using var transaction = await db.BeginWriteAsync(TestContext.Current.CancellationToken);

        Assert.False(await db.LockArquebusierForUpdateAsync(arquebusier.Id, ComparsaAccess.Only([registry.Own.Id]), TestContext.Current.CancellationToken));
        Assert.True(await db.LockArquebusierForUpdateAsync(arquebusier.Id, ComparsaAccess.Only([registry.Other.Id]), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_row_held_past_the_lock_timeout_answers_a_retryable_503()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var created = await registry.RegisterAsync(registry.Own.Id);
        var ct = TestContext.Current.CancellationToken;
        await using var scope = registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        await using var holder = await scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using var transaction = await holder.BeginTransactionAsync(ct);
        await using (var hold = new NpgsqlCommand($"SELECT 1 FROM registry.arquebusiers WHERE id = '{created.GetProperty("id").GetGuid()}' FOR UPDATE", holder, transaction))
        {
            await hold.ExecuteScalarAsync(ct);
        }

        var body = ArquebusierEditingTests.EditOf(created);
        body["phone"] = "+34 677 000 008";
        using var response = await registry.FiringChief.PutAsJsonAsync($"/api/arquebusiers/{created.GetProperty("id").GetGuid()}", body, ct);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "registry.busy");
    }

    [Fact]
    public async Task Every_registry_route_limits_the_body_size()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);

        // Kestrel enforces the limit from this metadata; the in-memory test server does not, so the
        // metadata is what can be asserted here.
        var routes = registry.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("arquebusiers", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(routes);
        Assert.All(routes, e =>
        {
            // The photo upload takes a 10 MB image plus its multipart framing (add-arquebusier-photos, D5),
            // and the import a 2 MB workbook plus its framing (add-registry-import, D3).
            var methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            var photo = e.RoutePattern.RawText!.Contains("/photos/", StringComparison.Ordinal) && methods.Contains("PUT");
            var import = e.RoutePattern.RawText!.Contains("/import", StringComparison.Ordinal) && methods.Contains("POST");
            var expected = photo ? PhotoEndpoints.MaxRequestBytes
                : import ? ImportWorkbookReader.MaxFileBytes + (64 * 1024)
                : 64 * 1024;
            Assert.Equal(expected, e.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
        });
    }

    [Fact]
    public async Task Far_too_long_fields_are_refused_before_they_are_normalised()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var body = RegistryTestHost.NewArquebusier(registry.Own.Id);
        body["firstName"] = new string('a', 50_000);
        body["lastName"] = new string('a', 1_000);
        body["phone"] = new string('1', 1_000);

        using var response = await registry.Admin.PostAsync("/api/arquebusiers", body);

        var errors = await ErrorsAsync(response);
        Assert.Equal(("tooLong", "tooLong", "tooLong"), (errors["firstName"], errors["lastName"], errors["phone"]));
    }

    [Fact]
    public async Task Registry_writes_are_throttled_per_user()
    {
        await using var host = await IdentityTestHost.StartAsync(
            postgres, mailpit, settings: new Dictionary<string, string?> { ["RateLimits:PersonalDataWrites:PermitLimit"] = "2" });
        var comparsa = RegistryData.NewComparsa("Comparsa Sintética Limitada");
        await host.Services.SaveCatalogAsync(comparsa);
        using var first = await host.SignInAsync(await host.CreateUserAsync("admin.limite.uno@example.test", UserRole.Admin));
        using var second = await host.SignInAsync(await host.CreateUserAsync("admin.limite.dos@example.test", UserRole.Admin));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await first.PostAsync("/api/arquebusiers", RegistryTestHost.NewArquebusier(comparsa.Id));
            statuses.Add(response.StatusCode);
        }

        using var other = await second.PostAsync("/api/arquebusiers", RegistryTestHost.NewArquebusier(comparsa.Id));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    [Fact]
    public async Task A_license_expires_at_midnight_in_Madrid()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var today = registry.Today;
        var created = await registry.RegisterAsync(registry.Own.Id, body => body["license"] = new Dictionary<string, object?>
        {
            ["type"] = "AE",
            ["pending"] = false,
            ["issuedOn"] = RegistryTestHost.Iso(today.AddYears(-5)),
            ["expiresOn"] = RegistryTestHost.Iso(today),
        });
        var id = created.GetProperty("id").GetGuid();
        var madrid = TimeZoneInfo.FindSystemTimeZoneById(FederationCalendar.TimeZoneId);
        var midnight = TimeZoneInfo.ConvertTimeToUtc(today.AddDays(1).ToDateTime(TimeOnly.MinValue), madrid);

        // A jump of up to a day outlives the session: sign in again at the new time.
        registry.Host.Time.SetUtcNow(new DateTimeOffset(midnight).AddMinutes(-1));
        using var chief = await registry.SignInFiringChiefAgainAsync();
        var lastMinute = await StatusAsync(chief, id);
        registry.Host.Time.SetUtcNow(new DateTimeOffset(midnight));
        var nextDay = await StatusAsync(chief, id);

        Assert.Equal(("VALID", "EXPIRED"), (lastMinute, nextDay));
    }

    private static async Task<string?> StatusAsync(HttpClient client, Guid id)
    {
        var detail = await ReadAsync<JsonElement>(await client.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken));
        return detail.GetProperty("license").GetProperty("status").GetString();
    }
}
