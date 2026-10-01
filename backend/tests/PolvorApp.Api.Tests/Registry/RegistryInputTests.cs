using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Licenses;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Spec "Arquebusier data", "National ID validation (BR-01)", "Current license (UC-02, BR-03)",
/// "Training course (UC-03)" and "Active and Reserve status (UC-05)": every blocking field rule is
/// reported by field name with a reason code, and accepted values are normalised.
/// </summary>
public sealed class RegistryInputTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static readonly ArquebusierFields Valid = new(
        FederationId: 900001,
        NationalId: " 12345678-z ",
        FirstName: "  Arcabucera ",
        LastName: "Sintética Uno",
        BirthDate: "1990-05-01",
        Email: " Arcabucera.Uno@PolvorApp.example ",
        Phone: " +34  600 000   001 ",
        Gender: "FEMALE",
        Status: null,
        TrainingCompletedOn: "2025-11-15",
        License: new LicenseFields("AE", Pending: false, IssuedOn: "2024-03-10", ExpiresOn: null));

    [Fact]
    public void Valid_fields_are_normalised()
    {
        var (input, errors) = RegistryInput.Read(Valid, Today, statusRequired: false);

        Assert.Empty(errors);
        Assert.NotNull(input);
        Assert.Equal(900001, input.FederationId);
        Assert.Equal("12345678Z", input.NationalId);
        Assert.Equal(("Arcabucera", "Sintética Uno"), (input.FirstName, input.LastName));
        Assert.Equal(new DateOnly(1990, 5, 1), input.BirthDate);
        Assert.Equal("arcabucera.uno@polvorapp.example", input.Email);
        Assert.Equal("+34 600 000 001", input.Phone);
        Assert.Equal(Gender.Female, input.Gender);
        Assert.Equal(ArquebusierStatus.Active, input.Status);
        Assert.Equal(new DateOnly(2025, 11, 15), input.TrainingCompletedOn);
        Assert.Equal(new License(LicenseType.Ae, Pending: false, new DateOnly(2024, 3, 10), new DateOnly(2029, 3, 10)), input.License);
    }

    [Fact]
    public void Optional_fields_may_be_absent_or_blank()
    {
        var (input, errors) = RegistryInput.Read(
            Valid with { Email = "  ", Phone = null, TrainingCompletedOn = null, License = null, Status = "RESERVE" }, Today, statusRequired: true);

        Assert.Empty(errors);
        Assert.Equal((null, null, null, null, ArquebusierStatus.Reserve), (input!.Email, input.Phone, input.TrainingCompletedOn, input.License, input.Status));
    }

    [Fact]
    public void Every_invalid_field_is_reported_at_once()
    {
        var fields = new ArquebusierFields(
            FederationId: 0, NationalId: "12345678A", FirstName: " ", LastName: new string('a', 101), BirthDate: "2026-10-02",
            Email: "not-an-email", Phone: "600-000-001", Gender: "OTHER", Status: "INACTIVE", TrainingCompletedOn: "01/10/2025", License: null);

        var (input, errors) = RegistryInput.Read(fields, Today, statusRequired: false);

        Assert.Null(input);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["federationId"] = "invalid",
                ["nationalId"] = "checkLetter",
                ["firstName"] = "required",
                ["lastName"] = "tooLong",
                ["birthDate"] = "future",
                ["email"] = "invalid",
                ["phone"] = "invalid",
                ["gender"] = "invalid",
                ["status"] = "invalid",
                ["trainingCompletedOn"] = "invalid",
            },
            errors);
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData(1_000_000_000, "invalid")]
    [InlineData(-5, "invalid")]
    public void Federation_id_is_between_1_and_999999999(int? federationId, string reason)
    {
        Assert.Equal(reason, ErrorsFor(Valid with { FederationId = federationId })["federationId"]);
    }

    [Fact]
    public void Federation_id_bounds_are_accepted()
    {
        Assert.Empty(ErrorsFor(Valid with { FederationId = 1 }));
        Assert.Empty(ErrorsFor(Valid with { FederationId = 999_999_999 }));
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("", "required")]
    [InlineData("1899-12-31", "tooOld")]
    [InlineData("2026-10-02", "future")]
    [InlineData("2026-02-30", "invalid")]
    [InlineData("1990-5-1", "invalid")]
    public void Birth_date_rules(string? birthDate, string reason)
    {
        Assert.Equal(reason, ErrorsFor(Valid with { BirthDate = birthDate })["birthDate"]);
    }

    [Fact]
    public void Birth_date_today_and_in_1900_are_accepted()
    {
        Assert.Empty(ErrorsFor(Valid with { BirthDate = "2026-10-01" }));
        Assert.Empty(ErrorsFor(Valid with { BirthDate = "1900-01-01" }));
    }

    [Theory]
    [InlineData("a@b", "invalid")]
    [InlineData("ana lópez@polvorapp.example", "invalid")]
    [InlineData("ana@polvo\nrapp.example", "invalid")]
    public void Email_must_be_a_plain_address(string email, string reason)
    {
        Assert.Equal(reason, ErrorsFor(Valid with { Email = email })["email"]);
    }

    [Fact]
    public void Email_longer_than_254_characters_is_too_long()
    {
        Assert.Equal("tooLong", ErrorsFor(Valid with { Email = new string('a', 250) + "@x.es" })["email"]);
    }

    [Theory]
    [InlineData("+34 600 000 001 0001", null)]
    [InlineData("+34 600 000 001 00012", "tooLong")]
    [InlineData("600+000", "invalid")]
    [InlineData("+", "invalid")]
    [InlineData("\uFF16\uFF10\uFF10", "invalid")]
    public void Phone_rules(string phone, string? reason)
    {
        var errors = ErrorsFor(Valid with { Phone = phone });

        Assert.Equal(reason, errors.GetValueOrDefault("phone"));
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("12345678A", "checkLetter")]
    [InlineData("\u04251234567L", "invalid")]
    public void National_id_reasons_come_from_BR_01(string? nationalId, string reason)
    {
        Assert.Equal(reason, ErrorsFor(Valid with { NationalId = nationalId })["nationalId"]);
    }

    [Theory]
    [InlineData("Ana\u200BMaría")]
    [InlineData("Ana\nMaría")]
    public void Names_with_hidden_characters_are_invalid(string name)
    {
        Assert.Equal("invalid", ErrorsFor(Valid with { FirstName = name })["firstName"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Gender_is_required(string? gender)
    {
        Assert.Equal("required", ErrorsFor(Valid with { Gender = gender })["gender"]);
    }

    [Fact]
    public void Status_is_required_when_editing()
    {
        Assert.Equal("required", ErrorsFor(Valid with { Status = null }, statusRequired: true)["status"]);
    }

    [Fact]
    public void Training_date_cannot_be_in_the_future()
    {
        Assert.Equal("future", ErrorsFor(Valid with { TrainingCompletedOn = "2026-10-02" })["trainingCompletedOn"]);
        Assert.Empty(ErrorsFor(Valid with { TrainingCompletedOn = "2026-10-01" }));
    }

    [Theory]
    [InlineData("AE", "2024-03-10", "2029-03-10")]
    [InlineData("A_PROF", "2026-02-01", "2027-02-01")]
    [InlineData("AE", "2024-02-29", "2029-02-28")]
    [InlineData("A_PROF", "2024-02-29", "2025-02-28")]
    public void Expiry_defaults_by_license_type(string type, string issuedOn, string expected)
    {
        var (input, errors) = RegistryInput.Read(Valid with { License = new LicenseFields(type, false, issuedOn, null) }, Today, false);

        Assert.Empty(errors);
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), input!.License!.ExpiresOn);
    }

    [Fact]
    public void An_edited_expiry_is_kept()
    {
        var (input, _) = RegistryInput.Read(Valid with { License = new LicenseFields("AE", false, "2024-03-10", "2028-12-31") }, Today, false);

        Assert.Equal(new DateOnly(2028, 12, 31), input!.License!.ExpiresOn);
    }

    [Fact]
    public void A_pending_license_has_no_dates()
    {
        var (input, errors) = RegistryInput.Read(Valid with { License = new LicenseFields("A_PROF", true, null, null) }, Today, false);

        Assert.Empty(errors);
        Assert.Equal(new License(LicenseType.AProf, Pending: true, null, null), input!.License);
    }

    [Theory]
    [InlineData(null, false, "2024-03-10", null, "license.type", "required")]
    [InlineData("B", false, "2024-03-10", null, "license.type", "invalid")]
    [InlineData("AE", null, "2024-03-10", null, "license.pending", "required")]
    [InlineData("AE", true, "2024-03-10", null, "license.issuedOn", "datesWhilePending")]
    [InlineData("AE", true, null, "2029-03-10", "license.expiresOn", "datesWhilePending")]
    [InlineData("AE", false, null, null, "license.issuedOn", "required")]
    [InlineData("AE", false, "2026-10-02", null, "license.issuedOn", "future")]
    [InlineData("AE", false, "2024-03-10", "2024-03-10", "license.expiresOn", "notAfterIssued")]
    [InlineData("AE", false, "2024-03-10", "2024-03-09", "license.expiresOn", "notAfterIssued")]
    [InlineData("AE", false, "10/03/2024", null, "license.issuedOn", "invalid")]
    public void License_rules(string? type, bool? pending, string? issuedOn, string? expiresOn, string field, string reason)
    {
        var errors = ErrorsFor(Valid with { License = new LicenseFields(type, pending, issuedOn, expiresOn) });

        Assert.Equal(reason, errors[field]);
    }

    [Theory]
    [InlineData("2029-03-10", "VALID")]
    [InlineData("2026-10-01", "VALID")]
    [InlineData("2026-09-30", "EXPIRED")]
    public void License_status_is_derived_from_today(string expiresOn, string status)
    {
        var license = new License(LicenseType.Ae, Pending: false, new DateOnly(2021, 9, 30), DateOnly.Parse(expiresOn, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(status, PolvorApp.SharedKernel.Codes.EnumCodes.ToCode(license.StatusOn(Today)));
    }

    [Fact]
    public void A_pending_license_is_pending_whatever_the_date()
    {
        Assert.Equal(LicenseStatus.Pending, new License(LicenseType.Ae, Pending: true, null, null).StatusOn(Today));
    }

    private static IReadOnlyDictionary<string, string> ErrorsFor(ArquebusierFields fields, bool statusRequired = false) =>
        RegistryInput.Read(fields, Today, statusRequired).Errors;
}
