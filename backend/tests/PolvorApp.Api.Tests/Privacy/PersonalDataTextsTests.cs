using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.AuditPrivacy.Privacy;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>Design D11: the export's texts exist in every language, fit Excel, and a missing one fails loudly.</summary>
public sealed class PersonalDataTextsTests
{
    private static readonly string[] Cultures = ["es-ES", "ca-ES-valencia", "en"];

    [Fact]
    public async Task Every_sheet_and_column_is_translated_in_every_language_and_fits_excel()
    {
        var keys = NeutralKeys();
        await using var factory = new ApiFactory("Host=offline");
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var culture in Cultures)
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                await using var scope = factory.Services.CreateAsyncScope();
                var packager = scope.ServiceProvider.GetRequiredService<PersonalDataPackager>();
                var sheets = keys.Where(k => k.StartsWith("Sheet.", StringComparison.Ordinal) && k != "Sheet.about")
                    .Select(k => new PersonalDataSheet(
                        k["Sheet.".Length..],
                        [.. keys.Where(c => c.StartsWith("Column.", StringComparison.Ordinal)).Select(c => c["Column.".Length..])],
                        [[]]))
                    .ToList();

                var package = packager.Build(
                    [new PersonalDataExportPart(sheets, []) { Notes = ["missingPhoto:id", "activityCapped"] }], "REQ-PRUEBA");

                Assert.Equal(sheets.Count, package.Sheets.Count);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task A_code_without_a_text_fails_the_export()
    {
        await using var factory = new ApiFactory("Host=offline");
        await using var scope = factory.Services.CreateAsyncScope();
        var packager = scope.ServiceProvider.GetRequiredService<PersonalDataPackager>();

        var error = Assert.Throws<InvalidOperationException>(() => packager.Build(
            [new PersonalDataExportPart([new PersonalDataSheet("unknownSheet", ["nationalId"], [["x"]])], [])], "REQ-PRUEBA"));

        Assert.Contains("Sheet.unknownSheet", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".ca")]
    [InlineData(".en")]
    public void Sheet_names_are_valid_excel_names(string suffix)
    {
        var names = Texts(suffix).Where(t => t.Key.StartsWith("Sheet.", StringComparison.Ordinal)).Select(t => t.Value).ToList();

        Assert.All(names, name => Assert.True(name.Length is > 0 and <= 31 && name.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) < 0, name));
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static List<string> NeutralKeys() => [.. Texts(string.Empty).Select(t => t.Key)];

    private static IEnumerable<KeyValuePair<string, string>> Texts(string suffix)
    {
        var path = Path.Combine(SourceRoot(), "Modules", "AuditPrivacy", "PolvorApp.AuditPrivacy", "Privacy", $"PrivacyTexts{suffix}.resx");
        return XDocument.Load(path).Root!.Elements("data")
            .Select(d => new KeyValuePair<string, string>((string)d.Attribute("name")!, (string)d.Element("value")!));
    }

    private static string SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PolvorApp.slnx")))
            {
                return Path.Combine(directory.FullName, "src");
            }
        }

        throw new InvalidOperationException("PolvorApp.slnx not found above the test output directory.");
    }
}
