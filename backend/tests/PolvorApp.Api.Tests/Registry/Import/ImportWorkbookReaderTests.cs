using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.ArquebusierRegistry.Import;

namespace PolvorApp.Api.Tests.Registry.Import;

/// <summary>
/// Reading the sheet of an import workbook (spec: Import file reading; design D3): headers are
/// recognised in any language and order, file problems stop the reading, blank rows are skipped.
/// </summary>
public sealed class ImportWorkbookReaderTests
{
    [Theory]
    [InlineData("es-ES")]
    [InlineData("ca-ES-valencia")]
    [InlineData("en")]
    public void The_template_headers_of_every_language_are_recognised(string culture)
    {
        var sheet = Read(ImportWorkbookBuilder.Template(culture).ValidRow());

        Assert.Empty(sheet.IgnoredColumns);
        Assert.Equal(ImportColumns.All, sheet.Rows.Single().Cells.Keys.Order());
    }

    [Fact]
    public void Headers_are_matched_in_any_order_ignoring_case_accents_and_spaces()
    {
        var builder = ImportWorkbookBuilder.WithHeaders("  dni/nie ", "GENERO", "date of birth", "Nom", "cognoms", "Federation  ID")
            .Row("12345678Z", "Mujer", "01/05/1990", "Arcabucera", "Sintética", 900001d);

        var row = Read(builder).Rows.Single();

        Assert.Equal("12345678Z", row.Cells[ImportColumn.NationalId].GetText());
        Assert.Equal("Mujer", row.Cells[ImportColumn.Gender].GetText());
        Assert.Equal("Arcabucera", row.Cells[ImportColumn.FirstName].GetText());
        Assert.Equal("Sintética", row.Cells[ImportColumn.LastName].GetText());
        Assert.Equal(900001d, row.Cells[ImportColumn.FederationId].GetNumber());
    }

    [Fact]
    public void A_missing_required_column_names_it()
    {
        var builder = ImportWorkbookBuilder.WithHeaders("ID Unión", "Apellidos", "Nombre", "Fecha de nacimiento", "Género")
            .Row(900001d, "Sintética", "Arcabucera", "01/05/1990", "Mujer");

        var problem = Problem(builder);

        Assert.Equal("missingColumns", problem.Reason);
        Assert.Equal(["nationalId"], problem.Columns);
    }

    [Fact]
    public void Optional_columns_may_be_left_out()
    {
        var builder = ImportWorkbookBuilder.WithHeaders("ID Unión", "Apellidos", "Nombre", "DNI/NIE", "Fecha de nacimiento", "Género")
            .Row(900001d, "Sintética", "Arcabucera", "12345678Z", "01/05/1990", "Mujer");

        var row = Read(builder).Rows.Single();

        Assert.Equal(6, row.Cells.Count);
    }

    [Fact]
    public void A_repeated_column_names_it_even_across_languages()
    {
        var headers = ImportWorkbookBuilder.Template().Headers.Append("Federation ID").ToArray();

        var problem = Problem(ImportWorkbookBuilder.WithHeaders(headers).ValidRow());

        Assert.Equal("duplicateColumns", problem.Reason);
        Assert.Equal(["federationId"], problem.Columns);
    }

    [Fact]
    public void Unknown_columns_are_ignored_and_listed()
    {
        var headers = ImportWorkbookBuilder.Template().Headers.Append("Observaciones").ToArray();

        var sheet = Read(ImportWorkbookBuilder.WithHeaders(headers).ValidRow());

        Assert.Equal(["Observaciones"], sheet.IgnoredColumns);
        Assert.Single(sheet.Rows);
    }

    [Fact]
    public void A_sheet_with_only_headers_is_empty() =>
        Assert.Equal("empty", Problem(ImportWorkbookBuilder.Template()).Reason);

    [Fact]
    public void A_thousand_rows_are_read()
    {
        var builder = ImportWorkbookBuilder.Template();
        for (var row = 0; row < ImportWorkbookReader.MaxRows; row++)
        {
            builder.ValidRow();
        }

        Assert.Equal(ImportWorkbookReader.MaxRows, Read(builder).Rows.Count);
    }

    [Fact]
    public void More_than_a_thousand_rows_are_too_many()
    {
        var builder = ImportWorkbookBuilder.Template();
        for (var row = 0; row <= ImportWorkbookReader.MaxRows; row++)
        {
            builder.ValidRow();
        }

        Assert.Equal("tooManyRows", Problem(builder).Reason);
    }

    [Fact]
    public void Blank_rows_are_skipped_and_rows_keep_their_sheet_number()
    {
        var builder = ImportWorkbookBuilder.Template().ValidRow().BlankRow().ValidRow()
            .Row(null, "   ", null);

        var sheet = Read(builder);

        Assert.Equal([2, 4], sheet.Rows.Select(row => row.RowNumber));
    }

    [Fact]
    public void A_row_with_content_only_in_ignored_columns_is_blank()
    {
        var headers = ImportWorkbookBuilder.Template().Headers.Append("Observaciones").ToArray();
        var cells = new object?[headers.Length];
        cells[^1] = "Solo una nota";

        var problem = Problem(ImportWorkbookBuilder.WithHeaders(headers).Row(cells));

        Assert.Equal("empty", problem.Reason);
    }

    [Fact]
    public void Only_the_first_sheet_is_read()
    {
        var builder = ImportWorkbookBuilder.Template().ValidRow()
            .WithSecondSheet(900009d, "Otra Hoja", "Arcabucero", "00000009D", new DateTime(1990, 5, 1), "Hombre");

        var sheet = Read(builder);

        Assert.Equal("Sintético Importado", sheet.Rows.Single().Cells[ImportColumn.LastName].GetText());
    }

    [Fact]
    public void A_header_with_a_typographic_apostrophe_is_recognised()
    {
        var headers = ImportWorkbookBuilder.Template("ca-ES-valencia").Headers
            .Select(header => header == "Data d'expedició" ? "Data d\u2019expedició" : header)
            .ToArray();

        var sheet = Read(ImportWorkbookBuilder.WithHeaders(headers).ValidRow());

        Assert.Empty(sheet.IgnoredColumns);
    }

    [Fact]
    public void A_formula_saved_without_its_value_is_an_error_cell()
    {
        var builder = ImportWorkbookBuilder.Template()
            .Row(new ImportWorkbookBuilder.Formula("900000+4"), "Sintética", "Arcabucera", "12345678Z", new DateTime(1990, 5, 1), "Mujer")
            .WithoutFormulaValues();

        var row = Read(builder).Rows.Single();

        Assert.True(row.Cells[ImportColumn.FederationId].IsError);
    }

    [Fact]
    public void A_truncated_workbook_is_invalid()
    {
        var content = ImportWorkbookBuilder.Template().ValidRow().Build();

        Assert.Equal("invalid", ImportWorkbookReader.Read(content[..(content.Length / 2)], NullLogger.Instance).Problem!.Reason);
    }

    [Fact]
    public void A_package_that_is_not_a_workbook_is_invalid() =>
        Assert.Equal("invalid", ImportWorkbookReader.Read(Encoding.ASCII.GetBytes("not a workbook"), NullLogger.Instance).Problem!.Reason);

    [Fact]
    public void A_workbook_that_cannot_be_loaded_is_invalid()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var types = archive.CreateEntry("[Content_Types].xml").Open();
            types.Write("""<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/></Types>"""u8);
        }

        var (sheet, problem) = ImportWorkbookReader.Read(stream.ToArray(), NullLogger.Instance);

        Assert.Null(sheet);
        Assert.Equal("invalid", problem!.Reason);
    }

    private static ImportSheet Read(ImportWorkbookBuilder builder)
    {
        var (sheet, problem) = ImportWorkbookReader.Read(builder.Build(), NullLogger.Instance);
        Assert.Null(problem);
        return sheet!;
    }

    private static ImportFileProblem Problem(ImportWorkbookBuilder builder)
    {
        var (sheet, problem) = ImportWorkbookReader.Read(builder.Build(), NullLogger.Instance);
        Assert.Null(sheet);
        return problem!;
    }
}
