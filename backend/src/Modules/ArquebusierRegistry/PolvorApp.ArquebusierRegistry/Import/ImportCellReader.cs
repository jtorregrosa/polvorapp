using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>
/// Turns the cells of an import row into the raw registration fields and validates them with
/// <see cref="RegistryInput.Read(ArquebusierFields, DateOnly, bool)"/> (spec: Import file reading;
/// design D4), so a row follows exactly the rules of the registration form. Only what a spreadsheet
/// adds is handled here: date and number cells, error cells, labels in any language, DNIs that lost
/// their leading zeros, and the license spread over three columns. A cell of the wrong kind (a
/// number in a name or date column, a date in a text column) is invalid rather than converted.
/// </summary>
internal static partial class ImportCellReader
{
    /// <summary>A cell holding an Excel error value such as <c>#REF!</c>.</summary>
    public const string CellError = "cellError";

    private const string IsoDate = "yyyy-MM-dd";

    /// <summary>Longest cell text read; longer text in any column is invalid.</summary>
    private const int MaxTextLength = 512;

    /// <summary>Earlier date cells are spreadsheet artefacts (Excel's day 0 is 1899-12-30), never real dates.</summary>
    private static readonly DateOnly EarliestDate = new(1900, 1, 1);

    private static readonly string[] DateTextFormats = ["dd/MM/yyyy", "d/M/yyyy", IsoDate];

    private static readonly FrozenDictionary<string, string> GenderCodes = Codes(texts => texts.Genders);

    private static readonly FrozenDictionary<string, string> StatusCodes = Codes(texts => texts.Statuses);

    private static readonly FrozenDictionary<string, string> LicenseTypeCodes = new Dictionary<string, string>
    {
        [ImportText.Normalise(EnumCodes.ToCode(LicenseType.Ae))] = EnumCodes.ToCode(LicenseType.Ae),
        [ImportText.Normalise(EnumCodes.ToCode(LicenseType.AProf))] = EnumCodes.ToCode(LicenseType.AProf),
        ["a-prof"] = EnumCodes.ToCode(LicenseType.AProf),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The validated row, or every error of the row by field name and reason.</summary>
    public static (ArquebusierInput? Input, IReadOnlyDictionary<string, string> Errors) Read(ImportRow row, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(row);
        var cellErrors = new Dictionary<string, string>(StringComparer.Ordinal);
        var cells = new Cells(row, cellErrors);

        var hasLicense = cells.Has(ImportColumn.LicenseType) || cells.Has(ImportColumn.LicenseIssuedOn) || cells.Has(ImportColumn.LicenseExpiresOn);
        var hasDates = cells.Has(ImportColumn.LicenseIssuedOn) || cells.Has(ImportColumn.LicenseExpiresOn);
        var fields = new ArquebusierFields(
            FederationId: cells.WholeNumber(ImportColumn.FederationId),
            NationalId: cells.NationalId(),
            FirstName: cells.Text(ImportColumn.FirstName),
            LastName: cells.Text(ImportColumn.LastName),
            BirthDate: cells.Date(ImportColumn.BirthDate),
            Email: cells.Text(ImportColumn.Email),
            Phone: cells.Digits(ImportColumn.Phone),
            Gender: cells.Code(ImportColumn.Gender, GenderCodes),
            Status: cells.Code(ImportColumn.Status, StatusCodes),
            TrainingCompletedOn: cells.Date(ImportColumn.TrainingCompletedOn),
            License: hasLicense
                ? new LicenseFields(
                    cells.Code(ImportColumn.LicenseType, LicenseTypeCodes),
                    Pending: !hasDates,
                    cells.Date(ImportColumn.LicenseIssuedOn),
                    cells.Date(ImportColumn.LicenseExpiresOn))
                : null);

        var (input, found) = RegistryInput.Read(fields, today, statusRequired: false);
        if (cellErrors.Count == 0)
        {
            return (input, found);
        }

        // What the cell reader found is the real cause: an empty field after a #REF! is not "required".
        var errors = new Dictionary<string, string>(found, StringComparer.Ordinal);
        foreach (var (field, reason) in cellErrors)
        {
            errors[field] = reason;
        }

        return (null, errors);
    }

    /// <summary>
    /// The row's normalised nationalId and its federationId when each is valid on its own, even if
    /// other fields of the row are not: duplicates are reported on every row that has them.
    /// </summary>
    public static (string? NationalId, int? FederationId) Identity(ImportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var cells = new Cells(row, new Dictionary<string, string>(StringComparer.Ordinal));
        var nationalId = NationalIds.NationalId.Parse(cells.NationalId()).Value;
        var federationId = cells.WholeNumber(ImportColumn.FederationId) is { } number and >= 1 and <= RegistryInput.MaxFederationId ? number : (int?)null;
        return (nationalId, federationId);
    }

    /// <summary>The last and first name as written, so the Admin can find the row; null when not text.</summary>
    public static (string? LastName, string? FirstName) Names(ImportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var cells = new Cells(row, new Dictionary<string, string>(StringComparer.Ordinal));
        return (Shorten(cells.Text(ImportColumn.LastName)), Shorten(cells.Text(ImportColumn.FirstName)));

        static string? Shorten(string? text) =>
            text?.Trim() is { Length: > 0 } trimmed ? trimmed[..Math.Min(trimmed.Length, Arquebusier.NameMaxLength)] : null;
    }

    /// <summary>Codes by every language's label and by the code itself, all normalised.</summary>
    private static FrozenDictionary<string, string> Codes<TEnum>(Func<ImportTemplateTexts, IReadOnlyDictionary<TEnum, string>> labels)
        where TEnum : struct, Enum =>
        ImportTemplateTexts.All
            .SelectMany(labels)
            .Select(label => (Key: ImportText.Normalise(label.Value), Code: EnumCodes.ToCode(label.Key)))
            .Concat(Enum.GetValues<TEnum>().Select(value => (Key: ImportText.Normalise(EnumCodes.ToCode(value)), Code: EnumCodes.ToCode(value))))
            .DistinctBy(entry => entry.Key)
            .ToFrozenDictionary(entry => entry.Key, entry => entry.Code, StringComparer.Ordinal);

    [GeneratedRegex(@"^[0-9]{1,7}[A-Za-z]\z")]
    private static partial Regex ShortDni();

    /// <summary>The cells of one row, recording what only a spreadsheet can get wrong.</summary>
    private sealed class Cells(ImportRow row, Dictionary<string, string> errors)
    {
        public bool Has(ImportColumn column) => !ImportRow.IsBlank(Value(column));

        /// <summary>The text of a text cell; any other kind of value, or text over <see cref="MaxTextLength"/>, is invalid.</summary>
        public string? Text(ImportColumn column) => Value(column) switch
        {
            { IsBlank: true } => null,
            { IsText: true } value when value.GetText().Length <= MaxTextLength => value.GetText(),
            { IsError: true } => Mark(column, CellError),
            _ => Mark(column, InputFields.Invalid),
        };

        /// <summary>A whole number from a number cell or from digits; anything else is invalid.</summary>
        public int? WholeNumber(ImportColumn column)
        {
            var value = Value(column);
            if (value.IsError)
            {
                Mark(column, CellError);
                return null;
            }

            if (ImportRow.IsBlank(value))
            {
                return null;
            }

            if (value.IsNumber && value.GetNumber() is var number && number == Math.Floor(number) && number is >= int.MinValue and <= int.MaxValue)
            {
                return (int)number;
            }

            if (value.IsText && int.TryParse(value.GetText().Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            Mark(column, InputFields.Invalid);
            return null;
        }

        /// <summary>A phone typed as a number loses nothing but its formatting; a fraction is invalid.</summary>
        public string? Digits(ImportColumn column)
        {
            var value = Value(column);
            if (value.IsNumber)
            {
                var number = value.GetNumber();
                if (number != Math.Floor(number) || number < 0)
                {
                    Mark(column, InputFields.Invalid);
                    return null;
                }

                return number.ToString("0", CultureInfo.InvariantCulture);
            }

            // A no-break space pasted from a document separates digits like a space.
            return Text(column)?.Replace('\u00A0', ' ').Replace('\u202F', ' ');
        }

        /// <summary>
        /// A DNI that lost its leading zeros (a person or a number cell dropped them) is padded: the
        /// check letter is computed on the number, so it does not change. NIEs are never padded.
        /// </summary>
        public string? NationalId()
        {
            var text = Text(ImportColumn.NationalId);
            if (text is null)
            {
                return null;
            }

            var compact = text.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("\t", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);
            return ShortDni().IsMatch(compact) ? compact.PadLeft(9, '0') : text;
        }

        /// <summary>
        /// A date cell, or text as <c>dd/mm/yyyy</c> or ISO, as the ISO text the registration reads.
        /// Other text is passed on, so the registration reports it as invalid. A plain number is
        /// invalid: a year or an amount typed into a date column must not become a date in 1905.
        /// </summary>
        public string? Date(ImportColumn column)
        {
            var value = Value(column);
            if (value.IsDateTime)
            {
                var day = DateOnly.FromDateTime(value.GetDateTime());
                return day < EarliestDate ? Mark(column, InputFields.Invalid) : day.ToString(IsoDate, CultureInfo.InvariantCulture);
            }

            var text = Text(column)?.Trim();
            return text is not null && DateOnly.TryParseExact(text, DateTextFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToString(IsoDate, CultureInfo.InvariantCulture)
                : text;
        }

        /// <summary>The code of a label in any language or of the code itself; unknown text is passed on as invalid.</summary>
        public string? Code(ImportColumn column, FrozenDictionary<string, string> codes)
        {
            var text = Text(column)?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            return codes.TryGetValue(ImportText.Normalise(text), out var code) ? code : text;
        }

        private XLCellValue Value(ImportColumn column) =>
            row.Cells.TryGetValue(column, out var value) ? value : Blank.Value;

        private string? Mark(ImportColumn column, string reason)
        {
            errors[ImportColumns.Field(column)] = reason;
            return null;
        }
    }
}
