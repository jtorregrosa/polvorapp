namespace PolvorApp.Exports.Contracts;

/// <summary>
/// Arquebusier badges to print and cut by hand (add-badges, design D2): ID-1 cards on A4 sheets, each
/// with a header band (<see cref="HeaderWord"/>, <see cref="HeaderLine"/> and the optional logo), a
/// photo and six labelled values. The builder chooses the words, the values and their order; the
/// renderer owns the geometry. The constructor checks the parts and keeps its own copy of the lists.
/// </summary>
public sealed class DocumentBadgeSheet
{
    /// <summary>The most badges in one document (spec: Badge batches).</summary>
    public const int MaxBadges = 200;

    /// <summary>The labelled values on every badge.</summary>
    public const int FieldCount = 6;

    /// <param name="fileStem">The file name without extension; see <see cref="DocumentFileStem"/>.</param>
    /// <param name="title">The PDF's title, without personal data.</param>
    /// <param name="headerWord">The word in the header band, e.g. "ARCABUCERO".</param>
    /// <param name="headerLine">The line under it, e.g. the Federation's name.</param>
    /// <param name="labels">The <see cref="FieldCount"/> field labels, in print order.</param>
    /// <param name="badges">One to <see cref="MaxBadges"/> badges, in print order.</param>
    /// <param name="logo">The logo printed in the header band, or null for none.</param>
    public DocumentBadgeSheet(
        string fileStem,
        string title,
        string headerWord,
        string headerLine,
        IReadOnlyList<string> labels,
        IReadOnlyList<DocumentBadge> badges,
        DocumentImage? logo)
    {
        DocumentFileStem.Check(fileStem, nameof(fileStem));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(headerWord);
        ArgumentException.ThrowIfNullOrWhiteSpace(headerLine);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(badges);
        if (labels.Count != FieldCount || labels.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"A badge sheet has {FieldCount} non-empty labels.", nameof(labels));
        }

        if (badges.Count is 0 or > MaxBadges)
        {
            throw new ArgumentException($"A badge sheet has one to {MaxBadges} badges.", nameof(badges));
        }

        FileStem = fileStem;
        Title = title;
        HeaderWord = headerWord;
        HeaderLine = headerLine;
        Labels = [.. labels];
        Badges = [.. badges.Select(badge => badge ?? throw new ArgumentNullException(nameof(badges)))];
        Logo = logo;
    }

    public string FileStem { get; }

    public string Title { get; }

    public string HeaderWord { get; }

    public string HeaderLine { get; }

    public IReadOnlyList<string> Labels { get; }

    public IReadOnlyList<DocumentBadge> Badges { get; }

    public DocumentImage? Logo { get; }

    /// <summary>The file stem and the count only: the badges are personal data.</summary>
    public override string ToString() => $"{nameof(DocumentBadgeSheet)} {FileStem}, {Badges.Count} badge(s)";
}

/// <summary>One badge: its photo and its <see cref="DocumentBadgeSheet.FieldCount"/> values.</summary>
public sealed class DocumentBadge
{
    /// <param name="photo">The ID photo, or null for an empty frame.</param>
    /// <param name="values">One value per label of the sheet; null, empty or blank prints an empty line to fill in by hand.</param>
    public DocumentBadge(DocumentImage? photo, IReadOnlyList<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != DocumentBadgeSheet.FieldCount)
        {
            throw new ArgumentException($"A badge has {DocumentBadgeSheet.FieldCount} values.", nameof(values));
        }

        Photo = photo;
        Values = [.. values.Select(value => string.IsNullOrWhiteSpace(value) ? null : value)];
    }

    public DocumentImage? Photo { get; }

    public IReadOnlyList<string?> Values { get; }

    /// <summary>Whether it has a photo only: the values are personal data.</summary>
    public override string ToString() => $"{nameof(DocumentBadge)} {(Photo is null ? "without" : "with")} photo";
}
