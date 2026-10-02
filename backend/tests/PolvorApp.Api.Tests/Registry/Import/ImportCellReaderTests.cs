using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Import;

namespace PolvorApp.Api.Tests.Registry.Import;

/// <summary>
/// Turning the cells of a row into registration fields (spec: Import file reading; design D4) and
/// validating them with the registration's rules, so a row and the form never disagree.
/// </summary>
public sealed class ImportCellReaderTests
{
    private static readonly DateOnly Today = new(2026, 10, 2);

    [Fact]
    public void A_complete_row_is_read()
    {
        var (input, errors) = Read(Valid());

        Assert.Empty(errors);
        Assert.Equal(900001, input!.FederationId);
        Assert.Equal("12345678Z", input.NationalId);
        Assert.Equal("Sintética", input.LastName);
        Assert.Equal(new DateOnly(1990, 5, 1), input.BirthDate);
        Assert.Equal(Gender.Female, input.Gender);
        Assert.Equal(ArquebusierStatus.Active, input.Status);
        Assert.Null(input.License);
    }

    [Theory]
    [InlineData("01/05/1990")]
    [InlineData("1/5/1990")]
    [InlineData(" 1990-05-01 ")]
    public void Dates_are_read_from_text(string text)
    {
        var (input, _) = Read(Valid(ImportColumn.BirthDate, text));

        Assert.Equal(new DateOnly(1990, 5, 1), input!.BirthDate);
    }

    [Fact]
    public void Dates_are_read_from_date_cells() =>
        Assert.Equal(new DateOnly(1990, 5, 1), Read(Valid(ImportColumn.BirthDate, new DateTime(1990, 5, 1, 13, 30, 0))).Input!.BirthDate);

    [Theory]
    [InlineData(1985d)]
    [InlineData(32994d)]
    [InlineData(0.5d)]
    public void A_plain_number_is_not_a_date(double number)
    {
        var errors = Read(Valid(ImportColumn.BirthDate, number).With(ImportColumn.TrainingCompletedOn, number)).Errors;

        Assert.Equal("invalid", errors["birthDate"]);
        Assert.Equal("invalid", errors["trainingCompletedOn"]);
    }

    [Fact]
    public void A_date_cell_before_1900_is_invalid() =>
        Assert.Equal("invalid", Read(Valid(ImportColumn.LicenseType, "AE").With(ImportColumn.LicenseIssuedOn, new DateTime(1899, 12, 31))).Errors["license.issuedOn"]);

    [Theory]
    [InlineData("05/13/1990")]
    [InlineData("1990/05/01")]
    [InlineData("1 de mayo de 1990")]
    public void Other_date_text_is_invalid(string text) =>
        Assert.Equal("invalid", Read(Valid(ImportColumn.BirthDate, text)).Errors["birthDate"]);

    [Fact]
    public void Numbers_are_read_for_the_federation_id_and_the_phone()
    {
        var (input, errors) = Read(Valid(ImportColumn.FederationId, 900002d).With(ImportColumn.Phone, 600123456d));

        Assert.Empty(errors);
        Assert.Equal(900002, input!.FederationId);
        Assert.Equal("600123456", input.Phone);
    }

    [Theory]
    [InlineData(900001.5)]
    [InlineData(-3)]
    [InlineData(1e12)]
    public void A_federation_id_that_is_not_a_whole_number_in_range_is_invalid(double number) =>
        Assert.Equal("invalid", Read(Valid(ImportColumn.FederationId, number)).Errors["federationId"]);

    [Theory]
    [InlineData("900003", 900003)]
    [InlineData(" 900003 ", 900003)]
    public void A_federation_id_typed_as_text_is_read(string text, int expected) =>
        Assert.Equal(expected, Read(Valid(ImportColumn.FederationId, text)).Input!.FederationId);

    [Theory]
    [InlineData("90A001")]
    [InlineData("9000000000000")]
    public void A_federation_id_text_that_is_not_a_number_is_invalid(string text) =>
        Assert.Equal("invalid", Read(Valid(ImportColumn.FederationId, text)).Errors["federationId"]);

    [Theory]
    [InlineData(600123456.5d)]
    [InlineData(-600123456d)]
    public void A_phone_number_that_is_not_whole_and_positive_is_invalid(double number) =>
        Assert.Equal("invalid", Read(Valid(ImportColumn.Phone, number)).Errors["phone"]);

    [Fact]
    public void A_phone_with_no_break_spaces_is_read() =>
        Assert.Equal("600 123 456", Read(Valid(ImportColumn.Phone, "600\u00A0123\u202F456")).Input!.Phone);

    [Theory]
    [InlineData("1234567L", "01234567L")]
    [InlineData("1234567-l", "01234567L")]
    [InlineData("123 P", "00000123P")]
    public void A_dni_without_its_leading_zeros_is_padded(string text, string expected) =>
        Assert.Equal(expected, Read(Valid(ImportColumn.NationalId, text)).Input!.NationalId);

    [Fact]
    public void An_nie_is_never_padded() =>
        Assert.Equal("invalid", Read(Valid(ImportColumn.NationalId, "X123456L")).Errors["nationalId"]);

    [Fact]
    public void A_dni_stored_as_a_number_is_invalid() =>
        Assert.Equal("invalid", Read(Valid(ImportColumn.NationalId, 12345678d)).Errors["nationalId"]);

    [Fact]
    public void A_padded_dni_still_needs_its_check_letter() =>
        Assert.Equal("checkLetter", Read(Valid(ImportColumn.NationalId, "1234567A")).Errors["nationalId"]);

    [Theory]
    [InlineData("Mujer", Gender.Female)]
    [InlineData("dona", Gender.Female)]
    [InlineData("FEMALE", Gender.Female)]
    [InlineData("Home", Gender.Male)]
    [InlineData("sin especificar", Gender.Unspecified)]
    [InlineData("Unspecified", Gender.Unspecified)]
    public void Gender_is_read_in_any_language_and_as_a_code(string text, Gender expected) =>
        Assert.Equal(expected, Read(Valid(ImportColumn.Gender, text)).Input!.Gender);

    [Theory]
    [InlineData("Reserve", ArquebusierStatus.Reserve)]
    [InlineData("reserva", ArquebusierStatus.Reserve)]
    [InlineData("Actiu", ArquebusierStatus.Active)]
    [InlineData("ACTIVE", ArquebusierStatus.Active)]
    public void Status_is_read_in_any_language_and_as_a_code(string text, ArquebusierStatus expected) =>
        Assert.Equal(expected, Read(Valid(ImportColumn.Status, text)).Input!.Status);

    [Fact]
    public void Unknown_values_are_invalid()
    {
        var errors = Read(Valid(ImportColumn.Gender, "Otro").With(ImportColumn.Status, "Baja").With(ImportColumn.LicenseType, "B")).Errors;

        Assert.Equal("invalid", errors["gender"]);
        Assert.Equal("invalid", errors["status"]);
        Assert.Equal("invalid", errors["license.type"]);
    }

    [Fact]
    public void A_cell_with_an_error_value_is_reported_as_such()
    {
        var errors = Read(Valid(ImportColumn.LastName, XLError.CellReference)
            .With(ImportColumn.FederationId, XLError.DivisionByZero)
            .With(ImportColumn.BirthDate, XLError.NoValueAvailable)
            .With(ImportColumn.LicenseType, XLError.NameNotRecognized)).Errors;

        Assert.Equal("cellError", errors["lastName"]);
        Assert.Equal("cellError", errors["federationId"]);
        Assert.Equal("cellError", errors["birthDate"]);
        Assert.Equal("cellError", errors["license.type"]);
    }

    [Fact]
    public void An_error_in_an_optional_column_still_rejects_the_row()
    {
        var (input, errors) = Read(Valid(ImportColumn.Phone, XLError.CellReference));

        Assert.Null(input);
        Assert.Equal("cellError", errors["phone"]);
    }

    [Theory]
    [MemberData(nameof(WrongKinds))]
    public void A_value_of_the_wrong_kind_is_invalid(string field, object value)
    {
        var column = ImportColumns.All.Single(candidate => ImportColumns.Field(candidate) == field);
        XLCellValue cell = value switch
        {
            double number => number,
            bool logical => logical,
            DateTime date => date,
            _ => throw new ArgumentException("Unexpected value.", nameof(value)),
        };

        Assert.Equal("invalid", Read(Valid(column, cell)).Errors[field]);
    }

    public static TheoryData<string, object> WrongKinds => new()
    {
        { "lastName", 123d },
        { "firstName", new DateTime(2024, 1, 15) },
        { "email", true },
        { "federationId", true },
        { "birthDate", true },
        { "gender", 1d },
    };

    [Fact]
    public void Text_over_the_length_limit_is_invalid() =>
        Assert.Equal("invalid", Read(Valid(ImportColumn.Email, new string('a', 513))).Errors["email"]);

    [Fact]
    public void Text_is_trimmed() =>
        Assert.Equal("Arcabucera", Read(Valid(ImportColumn.FirstName, "  Arcabucera  ")).Input!.FirstName);

    [Fact]
    public void A_missing_optional_column_is_empty()
    {
        var row = new ImportRow(2, new Dictionary<ImportColumn, XLCellValue>
        {
            [ImportColumn.FederationId] = 900001d,
            [ImportColumn.LastName] = "Sintética",
            [ImportColumn.FirstName] = "Arcabucera",
            [ImportColumn.NationalId] = "12345678Z",
            [ImportColumn.BirthDate] = new DateTime(1990, 5, 1),
            [ImportColumn.Gender] = "Mujer",
        });

        var (input, errors) = ImportCellReader.Read(row, Today);

        Assert.Empty(errors);
        Assert.Null(input!.Email);
        Assert.Null(input.License);
        Assert.Null(input.TrainingCompletedOn);
    }

    [Fact]
    public void A_license_type_without_dates_is_pending()
    {
        var license = Read(Valid(ImportColumn.LicenseType, "AE")).Input!.License!;

        Assert.True(license.Pending);
        Assert.Equal(LicenseType.Ae, license.Type);
        Assert.Null(license.IssuedOn);
    }

    [Theory]
    [InlineData("A-PROF")]
    [InlineData("a_prof")]
    public void An_issued_license_gets_the_default_expiry(string type)
    {
        var license = Read(Valid(ImportColumn.LicenseType, type).With(ImportColumn.LicenseIssuedOn, "10/03/2026")).Input!.License!;

        Assert.False(license.Pending);
        Assert.Equal(LicenseType.AProf, license.Type);
        Assert.Equal(new DateOnly(2027, 3, 10), license.ExpiresOn);
    }

    [Fact]
    public void A_given_expiry_is_kept()
    {
        var license = Read(Valid(ImportColumn.LicenseType, "AE")
            .With(ImportColumn.LicenseIssuedOn, new DateTime(2024, 3, 10))
            .With(ImportColumn.LicenseExpiresOn, new DateTime(2028, 1, 31))).Input!.License!;

        Assert.Equal(new DateOnly(2028, 1, 31), license.ExpiresOn);
    }

    [Fact]
    public void A_license_date_without_a_type_needs_the_type() =>
        Assert.Equal("required", Read(Valid(ImportColumn.LicenseIssuedOn, "10/03/2024")).Errors["license.type"]);

    [Fact]
    public void An_expiry_without_a_type_or_an_issue_date_needs_both()
    {
        var errors = Read(Valid(ImportColumn.LicenseExpiresOn, "10/03/2029")).Errors;

        Assert.Equal("required", errors["license.type"]);
        Assert.Equal("required", errors["license.issuedOn"]);
    }

    [Fact]
    public void An_expiry_without_an_issue_date_needs_the_issue_date() =>
        Assert.Equal("required", Read(Valid(ImportColumn.LicenseType, "AE").With(ImportColumn.LicenseExpiresOn, "10/03/2029")).Errors["license.issuedOn"]);

    [Fact]
    public void Every_problem_of_a_row_is_reported_at_once()
    {
        var errors = Read(Valid(ImportColumn.NationalId, "12345678A").With(ImportColumn.FirstName, null).With(ImportColumn.Email, "not-an-email")).Errors;

        Assert.Equal("checkLetter", errors["nationalId"]);
        Assert.Equal("required", errors["firstName"]);
        Assert.Equal("invalid", errors["email"]);
    }

    [Fact]
    public void A_course_date_in_the_future_is_reported() =>
        Assert.Equal("future", Read(Valid(ImportColumn.TrainingCompletedOn, new DateTime(2027, 1, 1))).Errors["trainingCompletedOn"]);

    [Fact]
    public void A_formula_is_read_by_its_saved_value()
    {
        var builder = ImportWorkbookBuilder.Template().Row(
            new ImportWorkbookBuilder.Formula("900000+4"), "Sintética", "Arcabucera", "12345678Z", new DateTime(1990, 5, 1), "Mujer");
        var row = ImportWorkbookReader.Read(builder.Build(), NullLogger.Instance).Sheet!.Rows.Single();

        Assert.Equal(900004, ImportCellReader.Read(row, Today).Input!.FederationId);
    }

    private static (PolvorApp.ArquebusierRegistry.Arquebusiers.ArquebusierInput? Input, IReadOnlyDictionary<string, string> Errors) Read(RowCells cells) =>
        ImportCellReader.Read(new ImportRow(2, cells.Cells), Today);

    private static RowCells Valid() => new(new Dictionary<ImportColumn, XLCellValue>
    {
        [ImportColumn.FederationId] = 900001d,
        [ImportColumn.LastName] = "Sintética",
        [ImportColumn.FirstName] = "Arcabucera",
        [ImportColumn.NationalId] = "12345678Z",
        [ImportColumn.BirthDate] = new DateTime(1990, 5, 1),
        [ImportColumn.Gender] = "Mujer",
        [ImportColumn.Email] = Blank.Value,
        [ImportColumn.Phone] = Blank.Value,
        [ImportColumn.Status] = Blank.Value,
        [ImportColumn.LicenseType] = Blank.Value,
        [ImportColumn.LicenseIssuedOn] = Blank.Value,
        [ImportColumn.LicenseExpiresOn] = Blank.Value,
        [ImportColumn.TrainingCompletedOn] = Blank.Value,
    });

    private static RowCells Valid(ImportColumn column, XLCellValue? value) => Valid().With(column, value);

    private sealed record RowCells(Dictionary<ImportColumn, XLCellValue> Cells)
    {
        public RowCells With(ImportColumn column, XLCellValue? value) =>
            new(new Dictionary<ImportColumn, XLCellValue>(Cells) { [column] = value ?? Blank.Value });
    }
}
