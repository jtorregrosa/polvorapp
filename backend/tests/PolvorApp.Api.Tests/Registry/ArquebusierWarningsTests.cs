using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// The registry list and detail carry the compliance warnings (specs "Arquebusier visibility (BR-12)",
/// "Registry screens" and "Compliance warnings (BR-04)"). Dates are relative to the API's today.
/// </summary>
public sealed class ArquebusierWarningsTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;
    private Arquebusier _minor = null!;
    private Arquebusier _compliant = null!;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var today = _registry.Today;

        // 16 years old, no course, no ID photo, an AE license expiring in 3 months with only its front photo.
        _minor = RegistryData.NewArquebusier(_registry.Own.Id, lastName: "Sintética Menor");
        _minor.BirthDate = today.AddYears(-16);
        (_minor.LicenseType, _minor.LicensePending, _minor.LicenseIssuedOn, _minor.LicenseExpiresOn) =
            (LicenseType.Ae, false, today.AddMonths(3).AddYears(-5), today.AddMonths(3));

        _compliant = RegistryData.NewArquebusier(_registry.Own.Id, lastName: "Sintético Completo");
        _compliant.TrainingCompletedOn = today.AddYears(-1);
        (_compliant.LicenseType, _compliant.LicensePending, _compliant.LicenseIssuedOn, _compliant.LicenseExpiresOn) =
            (LicenseType.Ae, false, today.AddYears(-1), today.AddYears(4));

        await _registry.Services.SaveRegistryAsync(
            _minor,
            _compliant,
            RegistryData.NewPhoto(_minor.Id, ArquebusierPhotoKind.LicenseFront),
            RegistryData.NewPhoto(_compliant.Id, ArquebusierPhotoKind.Id),
            RegistryData.NewPhoto(_compliant.Id, ArquebusierPhotoKind.LicenseFront),
            RegistryData.NewPhoto(_compliant.Id, ArquebusierPhotoKind.LicenseBack));
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task List_rows_carry_their_warnings_in_rule_order()
    {
        var rows = await ListAsync(_registry.FiringChief);

        var minor = rows[_minor.Id];
        Assert.Equal(
            ["LICENSE_EXPIRING", "COURSE_MISSING", "UNDER_AGE", "ID_PHOTO_MISSING", "LICENSE_PHOTOS_MISSING"],
            Warnings(minor));
        Assert.Empty(Warnings(rows[_compliant.Id]));
    }

    [Fact]
    public async Task An_expiring_license_is_still_valid()
    {
        var rows = await ListAsync(_registry.Admin);

        Assert.Equal("VALID", rows[_minor.Id].GetProperty("licenseStatus").GetString());
    }

    [Fact]
    public async Task List_rows_carry_no_birth_date()
    {
        var rows = await ListAsync(_registry.Admin);

        Assert.All(rows.Values, row => Assert.False(row.TryGetProperty("birthDate", out _)));
    }

    [Fact]
    public async Task An_arquebusier_with_warnings_can_still_be_edited()
    {
        var detail = await DetailAsync(_registry.FiringChief, _minor.Id);
        var body = ArquebusierEditingTests.EditOf(detail);
        body["phone"] = "+34 600 000 099";

        using var response = await _registry.FiringChief.PutAsJsonAsync($"/api/arquebusiers/{_minor.Id}", body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("UNDER_AGE", Warnings(await ReadAsync<JsonElement>(response)));
    }

    [Fact]
    public async Task The_detail_carries_the_warnings_and_the_age()
    {
        var detail = await DetailAsync(_registry.FiringChief, _minor.Id);

        Assert.Equal(
            ["LICENSE_EXPIRING", "COURSE_MISSING", "UNDER_AGE", "ID_PHOTO_MISSING", "LICENSE_PHOTOS_MISSING"],
            Warnings(detail));
        Assert.Equal(16, detail.GetProperty("age").GetInt32());
        Assert.Empty(Warnings(await DetailAsync(_registry.FiringChief, _compliant.Id)));
    }

    [Fact]
    public async Task The_age_and_the_warning_change_on_the_eighteenth_birthday()
    {
        var almost = RegistryData.NewArquebusier(_registry.Own.Id, lastName: "Sintética Cumpleaños");
        almost.BirthDate = _registry.Today.AddYears(-18).AddDays(1);
        await _registry.Services.SaveRegistryAsync(almost);

        var before = await DetailAsync(_registry.FiringChief, almost.Id);
        _registry.Host.Time.Advance(TimeSpan.FromDays(1));
        using var chief = await _registry.SignInFiringChiefAgainAsync();
        var after = await DetailAsync(chief, almost.Id);

        Assert.Equal(17, before.GetProperty("age").GetInt32());
        Assert.Contains("UNDER_AGE", Warnings(before));
        Assert.Equal(18, after.GetProperty("age").GetInt32());
        Assert.DoesNotContain("UNDER_AGE", Warnings(after));
    }

    [Fact]
    public async Task Adding_the_id_photo_removes_its_warning()
    {
        await _registry.Services.SaveRegistryAsync(RegistryData.NewPhoto(_minor.Id, ArquebusierPhotoKind.Id));

        var detail = await DetailAsync(_registry.FiringChief, _minor.Id);

        Assert.DoesNotContain("ID_PHOTO_MISSING", Warnings(detail));
    }

    public static TheoryData<string> LicenseShapes() => ["none", "pendingWithPhotos", "expired", "valid"];

    /// <summary>The list and the detail derive the same warnings for every license shape (review finding).</summary>
    [Theory]
    [MemberData(nameof(LicenseShapes))]
    public async Task List_and_detail_agree_for_every_license_shape(string shape)
    {
        var today = _registry.Today;
        var arquebusier = RegistryData.NewArquebusier(_registry.Other.Id, lastName: "Sintético " + shape);
        arquebusier.TrainingCompletedOn = today.AddYears(-1);
        (arquebusier.LicenseType, arquebusier.LicensePending, arquebusier.LicenseIssuedOn, arquebusier.LicenseExpiresOn) = shape switch
        {
            "pendingWithPhotos" => (LicenseType.Ae, true, null, null),
            "expired" => (LicenseType.Ae, false, today.AddYears(-6), today.AddDays(-1)),
            "valid" => (LicenseType.Ae, false, today.AddYears(-1), today.AddYears(4)),
            _ => ((LicenseType?)null, false, (DateOnly?)null, (DateOnly?)null),
        };
        object[] photos = shape == "pendingWithPhotos"
            ? [RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.LicenseFront), RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.LicenseBack)]
            : [];
        await _registry.Services.SaveRegistryAsync([arquebusier, RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.Id), .. photos]);

        var row = (await ListAsync(_registry.Admin))[arquebusier.Id];
        var detail = await DetailAsync(_registry.Admin, arquebusier.Id);

        string[] expected = shape switch
        {
            "none" => ["LICENSE_MISSING"],
            "pendingWithPhotos" => ["LICENSE_PENDING"],
            "expired" => ["LICENSE_EXPIRED", "LICENSE_PHOTOS_MISSING"],
            _ => ["LICENSE_PHOTOS_MISSING"],
        };
        Assert.Equal(expected, Warnings(row));
        Assert.Equal(expected, Warnings(detail));
    }

    [Fact]
    public async Task Recording_the_course_removes_its_warning()
    {
        var detail = await DetailAsync(_registry.FiringChief, _minor.Id);
        var body = ArquebusierEditingTests.EditOf(detail);
        body["trainingCompletedOn"] = RegistryTestHost.Iso(_registry.Today);

        using var response = await _registry.FiringChief.PutAsJsonAsync($"/api/arquebusiers/{_minor.Id}", body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("COURSE_MISSING", Warnings(await ReadAsync<JsonElement>(response)));
        Assert.DoesNotContain("COURSE_MISSING", Warnings((await ListAsync(_registry.FiringChief))[_minor.Id]));
    }

    [Fact]
    public async Task A_new_registration_returns_its_warnings()
    {
        var created = await _registry.RegisterAsync(_registry.Own.Id, body => body["trainingCompletedOn"] = null);

        Assert.Equal(["COURSE_MISSING", "ID_PHOTO_MISSING", "LICENSE_PHOTOS_MISSING"], Warnings(created));
        Assert.True(created.GetProperty("age").GetInt32() >= 18);
    }

    private static string?[] Warnings(JsonElement arquebusier) =>
        [.. arquebusier.GetProperty("warnings").EnumerateArray().Select(w => w.GetString())];

    private static async Task<Dictionary<Guid, JsonElement>> ListAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/arquebusiers", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<JsonElement>(response)).EnumerateArray().ToDictionary(r => r.GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }
}
