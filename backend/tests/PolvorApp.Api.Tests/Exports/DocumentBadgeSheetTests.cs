using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Exports.Contracts;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>
/// The rules a badge sheet enforces when it is built (add-badges, design D2): one to 200 badges, six
/// labelled fields on every badge, a file name without personal data, and descriptions that never
/// print a value.
/// </summary>
public sealed class DocumentBadgeSheetTests
{
    private static readonly string[] Labels = ["Apellidos", "Nombre", "DNI/NIE", "Código", "Fecha de caducidad", "Comparsa"];

    [Fact]
    public void A_sheet_keeps_its_parts()
    {
        var logo = DocumentImage.FromPng(TestImages.Png(40, 40));
        var sheet = Sheet([Badge("Sintético Pérez"), Badge("Ejemplo Ruiz")], logo: logo);

        Assert.Equal("polvorapp-badges-selection-2-20310302", sheet.FileStem);
        Assert.Equal("ARCABUCERO", sheet.HeaderWord);
        Assert.Equal(Labels, sheet.Labels);
        Assert.Equal(2, sheet.Badges.Count);
        Assert.Same(logo, sheet.Logo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(DocumentBadgeSheet.MaxBadges + 1)]
    public void A_sheet_holds_one_to_200_badges(int count)
    {
        var badges = Enumerable.Range(0, count).Select(_ => Badge("Sintético")).ToList();

        Assert.Throws<ArgumentException>(() => Sheet(badges));
    }

    [Fact]
    public void A_sheet_of_200_badges_is_accepted()
    {
        var badges = Enumerable.Range(0, DocumentBadgeSheet.MaxBadges).Select(_ => Badge("Sintético")).ToList();

        Assert.Equal(DocumentBadgeSheet.MaxBadges, Sheet(badges).Badges.Count);
    }

    [Fact]
    public void A_sheet_has_six_non_empty_labels()
    {
        Assert.Throws<ArgumentException>(() => Sheet([Badge("Sintético")], labels: Labels[..5]));
        Assert.Throws<ArgumentException>(() => Sheet([Badge("Sintético")], labels: [.. Labels[..5], " "]));
    }

    [Fact]
    public void A_sheet_refuses_a_file_stem_that_is_not_a_slug() =>
        Assert.Throws<ArgumentException>(() => Sheet([Badge("Sintético")], stem: "carnets de Ana"));

    [Fact]
    public void A_sheet_needs_its_header()
    {
        Assert.Throws<ArgumentException>(() => new DocumentBadgeSheet("stem", "Carnets", " ", "Federación", Labels, [Badge("S")], null));
        Assert.Throws<ArgumentException>(() => new DocumentBadgeSheet("stem", " ", "ARCABUCERO", "Federación", Labels, [Badge("S")], null));
    }

    [Fact]
    public void A_badge_has_six_values_and_null_is_an_empty_line()
    {
        var badge = new DocumentBadge(null, ["Sintético", "Ana", "00000000T", "1", null, "Comparsa Sintética"]);

        Assert.Null(badge.Values[4]);
        Assert.Null(badge.Photo);
        Assert.Throws<ArgumentException>(() => new DocumentBadge(null, ["Sintético", "Ana"]));
        Assert.Null(new DocumentBadge(null, ["Sintético", "Ana", "00000000T", "1", " ", "Comparsa"]).Values[4]);
    }

    [Fact]
    public void Descriptions_print_no_value()
    {
        var badge = new DocumentBadge(DocumentImage.FromJpeg(TestImages.Jpeg(300, 400)), ["Sintético", "Ana", "00000000T", "123", "31/05/2033", "Norte"]);
        var sheet = Sheet([badge]);

        foreach (var text in new[] { badge.ToString(), sheet.ToString() })
        {
            Assert.DoesNotContain("Sintético", text, StringComparison.Ordinal);
            Assert.DoesNotContain("00000000T", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Ana", text, StringComparison.Ordinal);
        }

        Assert.Equal("DocumentBadge with photo", badge.ToString());
        Assert.Equal("DocumentBadgeSheet polvorapp-badges-selection-2-20310302, 1 badge(s)", sheet.ToString());
    }

    private static DocumentBadge Badge(string lastName) =>
        new(null, [lastName, "Ana", "00000000T", "123", "31/05/2033", "Comparsa Sintética Norte"]);

    private static DocumentBadgeSheet Sheet(
        IReadOnlyList<DocumentBadge> badges, string stem = "polvorapp-badges-selection-2-20310302", IReadOnlyList<string>? labels = null, DocumentImage? logo = null) =>
        new(stem, "Carnets de arcabucero", "ARCABUCERO", "Federación Sintética", labels ?? Labels, badges, logo);
}
