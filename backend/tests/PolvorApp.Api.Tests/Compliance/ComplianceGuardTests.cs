using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Compliance;

/// <summary>
/// Cross-cutting rules of the insights: the registry list and the summary agree (design D3), viewing
/// records nothing (spec "Insights are read-only"), and every compliance route is scoped (BR-12).
/// </summary>
public sealed class ComplianceGuardTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var today = _registry.Today;

        // Every warning at least once, spread over the three comparsas of the host.
        await _registry.Services.SaveRegistryAsync(
        [
            .. ComplianceData.Arquebusier(_registry.Own.Id, today),
            .. ComplianceData.Arquebusier(_registry.Own.Id, today, age: 16, course: false, idPhoto: false, license: LicenseCase.Expiring, licensePhotos: false),
            .. ComplianceData.Arquebusier(_registry.Own.Id, today, gender: Gender.Female, status: ArquebusierStatus.Reserve, license: LicenseCase.Pending),
            .. ComplianceData.Arquebusier(_registry.Inactive.Id, today, license: LicenseCase.Expired, licensePhotos: false),
            .. ComplianceData.Arquebusier(_registry.Other.Id, today, license: LicenseCase.None, course: false),
            .. ComplianceData.Arquebusier(_registry.Other.Id, today, gender: Gender.Male),
        ]);
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task The_registry_list_and_the_summary_agree()
    {
        foreach (var client in new[] { _registry.Admin, _registry.FiringChief })
        {
            var rows = (await GetAsync(client, "/api/arquebusiers")).EnumerateArray().ToList();
            var summary = await GetAsync(client, "/api/compliance/summary");

            var rowWarnings = rows.Select(r => r.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToList()).ToList();
            Assert.Equal(rows.Count(r => r.GetProperty("status").GetString() == "ACTIVE"), summary.GetProperty("active").GetInt32());
            Assert.Equal(rows.Count(r => r.GetProperty("status").GetString() == "RESERVE"), summary.GetProperty("reserve").GetInt32());
            Assert.Equal(rowWarnings.Count(w => w.Count > 0), summary.GetProperty("withWarnings").GetInt32());
            foreach (var warning in summary.GetProperty("warnings").EnumerateArray())
            {
                var code = warning.GetProperty("code").GetString()!;
                Assert.Equal(rowWarnings.Count(w => w.Contains(code)), warning.GetProperty("count").GetInt32());
            }
        }
    }

    [Fact]
    public async Task Every_warning_appears_in_the_Admin_summary()
    {
        var summary = await GetAsync(_registry.Admin, "/api/compliance/summary");

        Assert.All(summary.GetProperty("warnings").EnumerateArray(), w => Assert.True(w.GetProperty("count").GetInt32() > 0, w.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task Viewing_the_insights_records_no_audit_entry()
    {
        var before = await AuditCountAsync();

        foreach (var client in new[] { _registry.Admin, _registry.FiringChief })
        {
            await GetAsync(client, "/api/compliance/summary");
            await GetAsync(client, "/api/compliance/statistics");
            await GetAsync(client, $"/api/compliance/statistics?comparsaId={_registry.Own.Id}&status=ACTIVE");
            await GetAsync(client, "/api/compliance/trends");
        }

        Assert.Equal(before, await AuditCountAsync());
    }

    /// <summary>
    /// Every route under /compliance is reviewed here: a new one fails until its scoping is classified.
    /// All are reads scoped by the caller's comparsas; an outsider sees none of another comparsa.
    /// </summary>
    [Fact]
    public async Task Every_compliance_route_is_classified_and_scoped()
    {
        string[] reviewed = ["GET api/compliance/summary", "GET api/compliance/statistics", "GET api/compliance/trends"];
        var routes = _registry.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("compliance", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => $"{m} {e.RoutePattern.RawText!.Trim('/')}"))
            .Order()
            .ToList();

        Assert.Equal(reviewed.Order(), routes);

        var outsider = await _registry.Host.CreateUserAsync("jefe.ajeno.avisos@example.test", UserRole.FiringChief);
        var otherComparsa = RegistryData.NewComparsa("Comparsa Sintética Vacía");
        await _registry.Services.SaveCatalogAsync(otherComparsa);
        await _registry.Host.AssignAsync(otherComparsa.Id, outsider.Id);
        using var outsiderClient = await _registry.Host.SignInAsync(outsider);
        using var anonymous = _registry.Host.Factory.CreateClient();

        var summary = await GetAsync(outsiderClient, "/api/compliance/summary");
        var statistics = await GetAsync(outsiderClient, "/api/compliance/statistics");
        using var foreign = await outsiderClient.GetAsync($"/api/compliance/statistics?comparsaId={_registry.Own.Id}", TestContext.Current.CancellationToken);
        var trends = await GetAsync(outsiderClient, "/api/compliance/trends");
        using var foreignTrends = await outsiderClient.GetAsync($"/api/compliance/trends?comparsaId={_registry.Own.Id}", TestContext.Current.CancellationToken);

        Assert.Equal((0, 0, 0), (summary.GetProperty("active").GetInt32(), summary.GetProperty("reserve").GetInt32(), summary.GetProperty("withWarnings").GetInt32()));
        Assert.Equal(0, statistics.GetProperty("total").GetInt32());
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "compliance.comparsaNotFound");
        Assert.All(trends.GetProperty("rows").EnumerateArray(), row => Assert.Equal(0, row.GetProperty("active").GetInt32()));
        await AssertProblemAsync(foreignTrends, HttpStatusCode.NotFound, "compliance.comparsaNotFound");
        foreach (var route in reviewed)
        {
            using var response = await anonymous.GetAsync("/" + route.Split(' ')[1], TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    /// <summary>
    /// Spec "Warnings follow the date": the reference date is today in Europe/Madrid, so a license
    /// expiring a year minus a day after a Madrid midnight becomes "expiring soon" exactly at that
    /// midnight, not at UTC midnight, in the list, the summary and the statistics alike. The clock
    /// only moves forward, so the midnight is two days ahead of the host's time.
    /// </summary>
    [Fact]
    public async Task Warnings_follow_the_date_in_Europe_Madrid_everywhere()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(registry.Host.Time.GetUtcNow(), madrid).DateTime).AddDays(2);
        var midnight = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), madrid.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
        var expiresOn = day.AddMonths(12).AddDays(-1);
        var comparsa = RegistryData.NewComparsa("Comparsa Sintética Medianoche");
        await registry.Services.SaveCatalogAsync(comparsa);
        var arquebusier = RegistryData.NewArquebusier(comparsa.Id);
        arquebusier.TrainingCompletedOn = new DateOnly(2020, 1, 1);
        (arquebusier.LicenseType, arquebusier.LicensePending, arquebusier.LicenseIssuedOn, arquebusier.LicenseExpiresOn) =
            (LicenseType.Ae, false, expiresOn.AddYears(-5), expiresOn);
        await registry.Services.SaveRegistryAsync(
            arquebusier,
            RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.Id),
            RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.LicenseFront),
            RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.LicenseBack));

        async Task<(string[] Row, int Summary, int Statistics)> ReadAsync(DateTimeOffset at)
        {
            registry.Host.Time.SetUtcNow(at);
            using var admin = await registry.Host.SignInAsync(await registry.Host.CreateUserAsync($"admin.{at.ToUnixTimeSeconds()}@example.test", UserRole.Admin));
            var row = (await GetAsync(admin, "/api/arquebusiers")).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == arquebusier.Id);
            var summary = await GetAsync(admin, "/api/compliance/summary");
            var statistics = await GetAsync(admin, $"/api/compliance/statistics?comparsaId={comparsa.Id}");
            return (
                [.. row.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!)],
                summary.GetProperty("warnings").EnumerateArray().Single(w => w.GetProperty("code").GetString() == "LICENSE_EXPIRING").GetProperty("count").GetInt32(),
                statistics.GetProperty("licenses").GetProperty("expiring").GetInt32());
        }

        var before = await ReadAsync(midnight.AddMinutes(-1));
        var after = await ReadAsync(midnight);

        Assert.Empty(before.Row);
        Assert.Equal(0, before.Statistics);
        Assert.Equal(["LICENSE_EXPIRING"], after.Row);
        Assert.Equal(before.Summary + 1, after.Summary);
        Assert.Equal(1, after.Statistics);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Set<AuditEntry>().CountAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }
}
