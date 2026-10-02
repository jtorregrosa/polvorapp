using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.ArquebusierRegistry.Import;
using PolvorApp.ComplianceInsights;
using PolvorApp.ComplianceInsights.Contracts;

namespace PolvorApp.Api.Tests.Registry.Import;

/// <summary>
/// The validation report (spec: Import validation report; design D5, D6): counts, the rows listed,
/// the errors named like the registration's, duplicates inside the file and against the registry,
/// and the compliance warnings of valid rows without the photo ones.
/// </summary>
public sealed class ImportValidationTests
{
    private static readonly DateOnly Today = new(2026, 10, 2);
    private static readonly HashSet<string> NoNationalIds = [];
    private static readonly HashSet<int> NoFederationIds = [];

    [Fact]
    public void A_clean_file_has_no_errors_and_lists_no_row()
    {
        var result = Validate(ImportWorkbookBuilder.Template().ValidRow().ValidRow().ValidRow());

        Assert.False(result.HasErrors);
        Assert.Equal(3, result.Report.RowCount);
        Assert.Equal(3, result.Report.ValidCount);
        Assert.Equal(0, result.Report.WarningRowCount);
        Assert.Empty(result.Report.Rows);
        Assert.Equal(3, result.Inputs.Count);
    }

    [Fact]
    public void Every_error_of_a_row_is_listed_in_column_order_with_its_name()
    {
        var result = Validate(ImportWorkbookBuilder.Template()
            .Row(900001d, "Sintética", null, "12345678A", new DateTime(1990, 5, 1), "Mujer", "not-an-email"));

        var row = Assert.Single(result.Report.Rows);
        Assert.Equal(2, row.RowNumber);
        Assert.Equal("Sintética", row.LastName);
        Assert.Null(row.FirstName);
        Assert.Equal(
            [new("firstName", "required"), new("nationalId", "checkLetter"), new("email", "invalid")],
            row.Errors);
        Assert.Empty(row.Warnings);
        Assert.Equal(1, result.Report.ErrorRowCount);
        Assert.Empty(result.Inputs);
    }

    [Fact]
    public void A_value_repeated_in_the_file_is_an_error_on_each_row()
    {
        var result = Validate(ImportWorkbookBuilder.Template()
            .Row(900001d, "Uno", "Arcabucero", "00000011B", new DateTime(1990, 5, 1), "Hombre")
            .Row(900002d, "Dos", "Arcabucero", "00000012N", new DateTime(1990, 5, 1), "Hombre")
            .Row(900001d, "Tres", "Arcabucero", "00000011B", new DateTime(1990, 5, 1), "Hombre"));

        var withErrors = result.Report.Rows.Where(row => row.Errors.Count > 0).ToList();
        Assert.Equal([2, 4], withErrors.Select(row => row.RowNumber));
        Assert.All(withErrors, row => Assert.Empty(row.Warnings));
        Assert.All(withErrors, row => Assert.Equal(
            [new("federationId", "duplicateInFile"), new("nationalId", "duplicateInFile")],
            row.Errors));
        Assert.Equal(1, result.Report.ValidCount);
    }

    [Fact]
    public void A_value_already_registered_is_taken_and_wins_over_a_repetition()
    {
        var result = ImportValidation.Validate(
            Sheet(ImportWorkbookBuilder.Template()
                .Row(900001d, "Uno", "Arcabucero", "00000011B", new DateTime(1990, 5, 1), "Hombre")
                .Row(900002d, "Dos", "Arcabucero", "00000011B", new DateTime(1990, 5, 1), "Hombre")),
            Today,
            new HashSet<string> { "00000011B" },
            new HashSet<int> { 900002 },
            new ComplianceRules());

        Assert.Equal([new ArquebusierImportError("nationalId", "taken")], result.Report.Rows[0].Errors);
        Assert.Equal([new("federationId", "taken"), new("nationalId", "taken")], result.Report.Rows[1].Errors);
    }

    [Fact]
    public void A_repeated_value_is_reported_even_when_the_row_has_other_errors()
    {
        var result = Validate(ImportWorkbookBuilder.Template()
            .Row(900001d, "Uno", "Arcabucero", "00000011B", new DateTime(1990, 5, 1), "Otro")
            .Row(900001d, "Dos", "Arcabucero", "00000012N", new DateTime(1990, 5, 1), "Hombre"));

        Assert.Contains(new ArquebusierImportError("federationId", "duplicateInFile"), result.Report.Rows[0].Errors);
        Assert.Contains(new ArquebusierImportError("gender", "invalid"), result.Report.Rows[0].Errors);
    }

    [Fact]
    public void Warnings_are_listed_in_rule_order_without_the_photo_ones_and_do_not_block()
    {
        // 17 years old on 2026-10-02, license expired, no course.
        var result = Validate(ImportWorkbookBuilder.Template()
            .Row(900001d, "Joven", "Arcabucero", "00000011B", new DateTime(2009, 5, 1), "Hombre",
                null, null, null, "AE", new DateTime(2020, 3, 10), new DateTime(2025, 3, 10), null));

        Assert.False(result.HasErrors);
        var row = Assert.Single(result.Report.Rows);
        Assert.Equal([ComplianceWarning.LicenseExpired, ComplianceWarning.CourseMissing, ComplianceWarning.UnderAge], row.Warnings);
        Assert.Empty(row.Errors);
        Assert.Equal(1, result.Report.WarningRowCount);
        Assert.Single(result.Inputs);
    }

    [Fact]
    public void A_pending_license_is_a_warning()
    {
        var result = Validate(ImportWorkbookBuilder.Template()
            .Row(900001d, "Sintética", "Arcabucera", "00000011B", new DateTime(1990, 5, 1), "Mujer",
                null, null, null, "AE", null, null, new DateTime(2023, 11, 4)));

        Assert.Equal([ComplianceWarning.LicensePending], Assert.Single(result.Report.Rows).Warnings);
    }

    [Fact]
    public void The_reference_date_is_the_one_given()
    {
        var builder = ImportWorkbookBuilder.Template()
            .Row(900001d, "Sintética", "Arcabucera", "00000011B", new DateTime(1990, 5, 1), "Mujer",
                null, null, null, "AE", new DateTime(2022, 3, 10), new DateTime(2027, 6, 30), new DateTime(2023, 11, 4));

        Assert.Equal([ComplianceWarning.LicenseExpiring], Assert.Single(Validate(builder).Report.Rows).Warnings);
        Assert.Empty(ImportValidation.Validate(Sheet(builder), new DateOnly(2026, 1, 1), NoNationalIds, NoFederationIds, new ComplianceRules()).Report.Rows);
    }

    [Fact]
    public void The_identities_of_the_file_are_collected_once()
    {
        var (nationalIds, federationIds) = ImportValidation.Identities(Sheet(ImportWorkbookBuilder.Template()
            .Row(900001d, "Uno", "Arcabucero", "11B", new DateTime(1990, 5, 1), "Hombre")
            .Row(900001d, "Dos", "Arcabucero", "00000011B", new DateTime(1990, 5, 1), "Hombre")
            .Row("no", "Tres", "Arcabucero", "12345678A", new DateTime(1990, 5, 1), "Hombre")));

        Assert.Equal(["00000011B"], nationalIds);
        Assert.Equal([900001], federationIds);
    }

    [Fact]
    public void Ignored_columns_are_reported()
    {
        var headers = ImportWorkbookBuilder.Template().Headers.Append("Observaciones").ToArray();

        var result = Validate(ImportWorkbookBuilder.WithHeaders(headers).ValidRow());

        Assert.Equal(["Observaciones"], result.Report.IgnoredColumns);
    }

    private static ImportValidationResult Validate(ImportWorkbookBuilder builder) =>
        ImportValidation.Validate(Sheet(builder), Today, NoNationalIds, NoFederationIds, new ComplianceRules());

    private static ImportSheet Sheet(ImportWorkbookBuilder builder) =>
        ImportWorkbookReader.Read(builder.Build(), NullLogger.Instance).Sheet!;
}
