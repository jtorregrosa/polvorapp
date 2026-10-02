using System.Net;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Compliance;

/// <summary>Spec "Warning summary": counts within the caller's scope (BR-12), every warning present.</summary>
[Collection(PostgresGroup.Name)]
public sealed class ComplianceSummaryTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private static readonly string[] Codes =
        ["LICENSE_MISSING", "LICENSE_PENDING", "LICENSE_EXPIRED", "LICENSE_EXPIRING", "COURSE_MISSING", "UNDER_AGE", "ID_PHOTO_MISSING", "LICENSE_PHOTOS_MISSING"];

    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var today = _registry.Today;
        await _registry.Services.SaveRegistryAsync(
        [
            // Own (assigned to the FiringChief): one compliant, one with COURSE_MISSING and UNDER_AGE.
            .. ComplianceData.Arquebusier(_registry.Own.Id, today),
            .. ComplianceData.Arquebusier(_registry.Own.Id, today, age: 16, course: false),

            // Inactive (also assigned): a reserve arquebusier with an expired license.
            .. ComplianceData.Arquebusier(_registry.Inactive.Id, today, status: ArquebusierStatus.Reserve, license: LicenseCase.Expired),

            // Other (outside the FiringChief's scope): no license and no ID photo.
            .. ComplianceData.Arquebusier(_registry.Other.Id, today, license: LicenseCase.None, idPhoto: false),
        ]);
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task An_Admin_counts_every_comparsa_active_or_not()
    {
        var summary = await SummaryAsync(_registry.Admin);

        Assert.Equal(3, summary.GetProperty("active").GetInt32());
        Assert.Equal(1, summary.GetProperty("reserve").GetInt32());
        Assert.Equal(3, summary.GetProperty("withWarnings").GetInt32());
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["LICENSE_MISSING"] = 1,
                ["LICENSE_PENDING"] = 0,
                ["LICENSE_EXPIRED"] = 1,
                ["LICENSE_EXPIRING"] = 0,
                ["COURSE_MISSING"] = 1,
                ["UNDER_AGE"] = 1,
                ["ID_PHOTO_MISSING"] = 1,
                ["LICENSE_PHOTOS_MISSING"] = 0,
            },
            Counts(summary));
    }

    [Fact]
    public async Task A_FiringChief_counts_only_their_comparsas()
    {
        var summary = await SummaryAsync(_registry.FiringChief);

        Assert.Equal(2, summary.GetProperty("active").GetInt32());
        Assert.Equal(1, summary.GetProperty("reserve").GetInt32());
        Assert.Equal(2, summary.GetProperty("withWarnings").GetInt32());
        Assert.Equal(0, Counts(summary)["LICENSE_MISSING"]);
        Assert.Equal(0, Counts(summary)["ID_PHOTO_MISSING"]);
    }

    [Fact]
    public async Task Several_warnings_count_once_as_an_arquebusier_and_once_per_code()
    {
        var summary = await SummaryAsync(_registry.FiringChief);

        Assert.Equal(1, Counts(summary)["COURSE_MISSING"]);
        Assert.Equal(1, Counts(summary)["UNDER_AGE"]);
        Assert.Equal(1, Counts(summary)["LICENSE_EXPIRED"]);
        Assert.Equal(2, summary.GetProperty("withWarnings").GetInt32());
    }

    [Fact]
    public async Task Every_code_is_present_in_rule_order()
    {
        var summary = await SummaryAsync(_registry.FiringChief);

        Assert.Equal(Codes, summary.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task A_FiringChief_without_assignments_gets_zeros()
    {
        using var unassigned = await _registry.Host.SignInAsync(await _registry.Host.CreateUserAsync("jefe.resumen.vacio@example.test", UserRole.FiringChief));

        var summary = await SummaryAsync(unassigned);

        Assert.Equal(0, summary.GetProperty("active").GetInt32());
        Assert.Equal(0, summary.GetProperty("reserve").GetInt32());
        Assert.Equal(0, summary.GetProperty("withWarnings").GetInt32());
        Assert.All(Counts(summary).Values, count => Assert.Equal(0, count));
        Assert.Equal(Codes, Counts(summary).Keys);
    }

    [Fact]
    public async Task A_request_without_a_session_is_unauthorized()
    {
        using var anonymous = _registry.Host.Factory.CreateClient();

        using var response = await anonymous.GetAsync("/api/compliance/summary", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Dictionary<string, int> Counts(JsonElement summary) =>
        summary.GetProperty("warnings").EnumerateArray()
            .ToDictionary(w => w.GetProperty("code").GetString()!, w => w.GetProperty("count").GetInt32());

    private static async Task<JsonElement> SummaryAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/compliance/summary", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }
}
