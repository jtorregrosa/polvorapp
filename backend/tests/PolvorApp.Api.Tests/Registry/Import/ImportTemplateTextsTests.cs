using System.Globalization;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Import;

namespace PolvorApp.Api.Tests.Registry.Import;

/// <summary>
/// The template texts in the three languages (spec: Import template; design D8). The reader
/// recognises headers and values from the same texts, so they must be complete and unambiguous.
/// </summary>
public sealed class ImportTemplateTextsTests
{
    public static TheoryData<string> Cultures => ["es-ES", "ca-ES-valencia", "en"];

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Every_text_exists(string culture)
    {
        var texts = ImportTemplateTexts.For(CultureInfo.GetCultureInfo(culture));

        Assert.All(Enum.GetValues<ImportColumn>(), column =>
        {
            Assert.False(string.IsNullOrWhiteSpace(texts.Headers[column]));
            Assert.False(string.IsNullOrWhiteSpace(texts.Descriptions[column]));
        });
        Assert.All(Enum.GetValues<Gender>(), gender => Assert.False(string.IsNullOrWhiteSpace(texts.Genders[gender])));
        Assert.All(Enum.GetValues<ArquebusierStatus>(), status => Assert.False(string.IsNullOrWhiteSpace(texts.Statuses[status])));
        Assert.All(
            new[] { texts.DataSheet, texts.InstructionsSheet, texts.ListsSheet, texts.ColumnHeading, texts.RequiredHeading, texts.DescriptionHeading, texts.Yes, texts.No },
            text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.NotEmpty(texts.Notes);
    }

    [Theory]
    [InlineData("es-ES", "es-ES")]
    [InlineData("es", "es-ES")]
    [InlineData("ca-ES-valencia", "ca-ES-valencia")]
    [InlineData("ca", "ca-ES-valencia")]
    [InlineData("en", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("fr-FR", "es-ES")]
    public void The_texts_follow_the_language(string culture, string expected) =>
        Assert.Equal(expected, ImportTemplateTexts.For(CultureInfo.GetCultureInfo(culture)).Culture);

    [Fact]
    public void No_two_columns_share_a_header_in_any_language()
    {
        var owners = ImportTemplateTexts.All
            .SelectMany(texts => texts.Headers.Select(header => (Key: ImportText.Normalise(header.Value), Column: header.Key)))
            .GroupBy(entry => entry.Key)
            .Where(group => group.Select(entry => entry.Column).Distinct().Count() > 1)
            .Select(group => group.Key);

        Assert.Empty(owners);
    }

    [Fact]
    public void No_two_values_share_a_label_in_any_language()
    {
        Assert.Empty(Ambiguous(ImportTemplateTexts.All.SelectMany(texts => texts.Genders)));
        Assert.Empty(Ambiguous(ImportTemplateTexts.All.SelectMany(texts => texts.Statuses)));
    }

    [Fact]
    public void Sheet_names_fit_excel_rules()
    {
        Assert.All(ImportTemplateTexts.All, texts => Assert.All(
            new[] { texts.DataSheet, texts.InstructionsSheet, texts.ListsSheet },
            name =>
            {
                Assert.InRange(name.Length, 1, 31);
                Assert.DoesNotContain(name, character => "[]:*?/\\".Contains(character));
            }));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_texts_state_the_limits_the_reader_applies(string culture)
    {
        var texts = ImportTemplateTexts.For(CultureInfo.GetCultureInfo(culture));
        var all = string.Join(" ", texts.Notes.Concat(texts.Descriptions.Values));

        Assert.Contains($"{ImportWorkbookReader.MaxFileBytes / (1024 * 1024)} MB", all, StringComparison.Ordinal);
        Assert.Contains(ImportWorkbookReader.MaxRows.ToString(CultureInfo.InvariantCulture), all, StringComparison.Ordinal);
        Assert.Contains($"100 ", texts.Descriptions[ImportColumn.LastName], StringComparison.Ordinal);
        Assert.Contains("999999999", texts.Descriptions[ImportColumn.FederationId], StringComparison.Ordinal);
        Assert.Contains("20 ", texts.Descriptions[ImportColumn.Phone], StringComparison.Ordinal);
        Assert.Equal(100, PolvorApp.ArquebusierRegistry.Arquebusiers.Arquebusier.NameMaxLength);
        Assert.Equal(999_999_999, PolvorApp.ArquebusierRegistry.Arquebusiers.RegistryInput.MaxFederationId);
        Assert.Equal(20, PolvorApp.ArquebusierRegistry.Arquebusiers.Arquebusier.PhoneMaxLength);
    }

    [Theory]
    [InlineData("  Fecha de  nacimiento ", "fecha de nacimiento")]
    [InlineData("TELÉFONO", "telefono")]
    [InlineData("Gènere", "genere")]
    [InlineData("A-PROF", "a-prof")]
    public void Normalising_ignores_case_accents_and_spacing(string text, string expected) =>
        Assert.Equal(expected, ImportText.Normalise(text));

    private static IEnumerable<string> Ambiguous<TValue>(IEnumerable<KeyValuePair<TValue, string>> labels)
        where TValue : struct, Enum =>
        labels
            .GroupBy(label => ImportText.Normalise(label.Value))
            .Where(group => group.Select(label => label.Key).Distinct().Count() > 1)
            .Select(group => group.Key);
}
