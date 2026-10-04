using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Exports.Contracts;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>Design D11: the workbook of a personal data export has typed cells and neutralised text.</summary>
public sealed class PersonalDataWorkbookTests
{
    [Fact]
    public async Task Every_sheet_is_written_with_typed_cells_and_formulas_neutralised()
    {
        await using var factory = new ApiFactory("Host=offline");
        var renderer = factory.Services.GetRequiredService<IDocumentRenderer>();

        var file = renderer.RenderWorkbook(new DocumentWorkbook(
            "prueba",
            [
                new DocumentSheet("Ficha", ["Texto", "Número", "Sí/no", "Fecha"], [["=SUMA(1;2)", 7, true, new DateOnly(2030, 3, 1)]]),
                new DocumentSheet("Una hoja con un nombre demasiado largo para Excel", ["A"], [["@peligro"]]),
            ]));

        using var workbook = new XLWorkbook(new MemoryStream(file.Content.ToArray()));
        Assert.Equal(2, workbook.Worksheets.Count);
        var first = workbook.Worksheet("Ficha");
        Assert.Equal("'=SUMA(1;2)", first.Cell(2, 1).GetString());
        Assert.False(first.Cell(2, 1).HasFormula);
        Assert.Equal(7, first.Cell(2, 2).GetValue<int>());
        Assert.True(first.Cell(2, 3).GetBoolean());
        Assert.Equal(new DateTime(2030, 3, 1), first.Cell(2, 4).GetDateTime());
        Assert.Equal(31, workbook.Worksheets.Last().Name.Length);
        Assert.Equal("prueba.xlsx", file.FileName);
    }
}
