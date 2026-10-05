using System.Globalization;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.SharedKernel.Images;

/// <summary>The data a specimen license card shows: the arquebusier's own, never anyone else's.</summary>
public sealed record SpecimenCardData(
    string NationalId,
    string FirstName,
    string LastName,
    DateOnly BirthDate,
    SyntheticLicenseType Type,
    DateOnly IssuedOn,
    DateOnly ExpiresOn);

/// <summary>Draws the two sides of a specimen license card as PNG (realistic-seed-data, design D6).</summary>
public interface ISpecimenCardPainter
{
    byte[] Front(SpecimenCardData card);

    byte[] Back(SpecimenCardData card);
}

public enum SpecimenAlign
{
    Left,
    Center,
    Right,
}

public enum SpecimenInk
{
    Dark,
    Light,
    Watermark,
    Stamp,
}

/// <summary>One text on a card. <paramref name="MaxWidth"/> scales the text down when it is longer; <paramref name="Rotation"/> is in degrees.</summary>
public sealed record SpecimenText(string Text, float X, float Y, float Size, bool Bold, SpecimenAlign Align, SpecimenInk Ink, float MaxWidth = 0, float Rotation = 0);

/// <summary>
/// Where every text of a specimen card goes, on a 1000 × 630 card that follows the layout of the
/// Spanish arms license (design D6). A pure function, so tests read what is drawn. It never holds a
/// coat of arms, a flag emblem, EU stars, "Ministerio del Interior" or "España"; every side carries
/// the <see cref="Watermark"/> several times across the data and an opaque <see cref="Stamp"/> at the
/// edge, so no crop removes them all. The wording is the document's own Spanish, not UI text.
/// </summary>
public static class SpecimenCardLayout
{
    public const int Width = 1000;
    public const int Height = 630;
    public const string Watermark = "MUESTRA – SIN VALIDEZ";
    public const string Stamp = "MUESTRA";

    /// <summary>The back's grid: column edges and row edges, in pixels.</summary>
    public static readonly float[] GridColumns = [60, 220, 480, 740, 940];
    public static readonly float[] GridRows = [40, 95, 150, 205, 260, 315, 370, 425, 505];

    public static IReadOnlyList<SpecimenText> Front(SpecimenCardData card) =>
    [
        new("LICENCIA DE ARMAS", 945, 100, 36, Bold: true, SpecimenAlign.Right, SpecimenInk.Light),
        Label("N.I.F./N.I.E.", 230),
        Value(card.NationalId, 275),
        Label("NOMBRE Y APELLIDOS", 330),
        Value($"{card.FirstName} {card.LastName}".ToUpper(CultureInfo.GetCultureInfo("es-ES")), 375, maxWidth: 560),
        Label("FECHA DE NACIMIENTO", 430),
        Value(Date(card.BirthDate), 475),
        Label("FECHA DE EXPEDICIÓN", 530),
        Value(Date(card.IssuedOn), 575),
        new("EL TITULAR", 730, 470, 20, Bold: true, SpecimenAlign.Left, SpecimenInk.Dark),
        StampText(),
        .. Watermarks(),
    ];

    public static IReadOnlyList<SpecimenText> Back(SpecimenCardData card)
    {
        var columns = GridColumns;
        float Centre(int column) => (columns[column] + columns[column + 1]) / 2;
        return
        [
            .. Enumerable.Range(0, 4).Select(c => new SpecimenText((c + 1).ToString(CultureInfo.InvariantCulture), Centre(c), 80, 30, false, SpecimenAlign.Center, SpecimenInk.Dark)),
            new(card.Type == SyntheticLicenseType.Ae ? "AE" : "A-PROF", Centre(0), 136, 32, Bold: true, SpecimenAlign.Center, SpecimenInk.Dark, MaxWidth: 140),
            new(Date(card.IssuedOn), Centre(1), 134, 26, Bold: false, SpecimenAlign.Center, SpecimenInk.Dark),
            new(Date(card.ExpiresOn), Centre(2), 134, 26, Bold: false, SpecimenAlign.Center, SpecimenInk.Dark),
            new("5", 95, 480, 30, Bold: false, SpecimenAlign.Center, SpecimenInk.Dark),
            Legend("1) Tipo de licencia", 60, 548),
            Legend("2) Válida desde", 60, 578),
            Legend("3) Válida hasta", 60, 608),
            Legend("4) Restricciones", 560, 548),
            Legend("5) Observaciones", 560, 578),
            StampText(),
            .. Watermarks(),
        ];
    }

    private static SpecimenText Label(string text, float y) => new(text, 110, y, 20, Bold: true, SpecimenAlign.Left, SpecimenInk.Dark);

    private static SpecimenText Value(string text, float y, float maxWidth = 0) => new(text, 110, y, 30, Bold: false, SpecimenAlign.Left, SpecimenInk.Dark, maxWidth);

    private static SpecimenText Legend(string text, float x, float y) => new(text, x, y, 22, Bold: false, SpecimenAlign.Left, SpecimenInk.Dark);

    private static SpecimenText StampText() => new(Stamp, 945, 612, 22, Bold: true, SpecimenAlign.Right, SpecimenInk.Stamp);

    private static IEnumerable<SpecimenText> Watermarks() =>
        new (float X, float Y)[] { (250, 170), (750, 200), (500, 340), (250, 500), (760, 540) }
            .Select(p => new SpecimenText(Watermark, p.X, p.Y, 40, Bold: true, SpecimenAlign.Center, SpecimenInk.Watermark, Rotation: -18));

    private static string Date(DateOnly date) => date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
}
