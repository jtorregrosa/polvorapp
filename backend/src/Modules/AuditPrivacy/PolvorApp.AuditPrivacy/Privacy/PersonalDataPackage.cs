using System.Globalization;
using System.IO.Compression;
using Microsoft.Extensions.Localization;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.Exports.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.SharedKernel.Time;
namespace PolvorApp.AuditPrivacy.Privacy;

/// <summary>Marker for the <c>PrivacyTexts</c> resources (sheet and column names, "About this data").</summary>
internal sealed class PrivacyTexts;

/// <summary>A personal data export, ready to send: never stored (SEC-06).</summary>
internal sealed record PersonalDataPackage(
    byte[] Content, string FileName, IReadOnlyDictionary<string, int> Sheets, int Files, IReadOnlyList<string> Notes)
{
    public const string ContentType = "application/zip";

    /// <summary>The file name and counts only: the content is personal data.</summary>
    public override string ToString() => $"PersonalDataPackage({FileName})";
}

/// <summary>
/// Builds a person's or a user's data export (spec: Exporting a person's / a user's data; design D11): a
/// ZIP with one Excel workbook in the Admin's language — one sheet per category that holds data, then
/// "About this data" with the controller, purposes, recipients, retention, rights, the request reference,
/// the date and every note (a missing photo, a capped sheet) — and the person's photos. The controller is
/// the Federation's official name of the settings, in Valencian for a Valencian export (add-federation-settings).
/// The file name never holds the DNI/NIE or a name. A missing text fails the export rather than print its key.
/// </summary>
internal sealed class PersonalDataPackager(IDocumentRenderer renderer, IStringLocalizer<PrivacyTexts> texts, IFederationSettings settings, TimeProvider time)
{
    public const string WorkbookName = "personal-data.xlsx";

    /// <summary>The column whose codes (<c>holder</c>, <c>proxy</c>) are written as words.</summary>
    private const string RoleColumn = "participation";

    private static readonly string[] AboutItems = ["Controller", "Purposes", "Recipients", "Retention", "Rights"];

    public async Task<PersonalDataPackage> BuildAsync(IReadOnlyList<PersonalDataExportPart> parts, string reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var federation = await settings.GetAsync(cancellationToken);
        var sheets = parts.SelectMany(p => p.Sheets).Where(s => s.Rows.Count > 0).ToList();
        if (sheets.GroupBy(s => s.Code, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            throw new InvalidOperationException($"Two participants export the sheet '{duplicate.Key}'.");
        }

        var notes = parts.SelectMany(p => p.Notes).ToList();
        var now = time.GetUtcNow();
        var today = FederationCalendar.Today(time);
        var workbook = new DocumentWorkbook(
            "personal-data",
            [.. sheets.Select(Sheet), About(notes, reference, today, federation)]);

        var files = parts.SelectMany(p => p.Files).ToList();
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Both are compressed already; the entries carry the export's time, not the server's clock.
            Add(zip, WorkbookName, renderer.RenderWorkbook(workbook).Content, now);
            foreach (var file in files)
            {
                Add(zip, file.Name, file.Content, now);
            }
        }

        var name = $"polvorapp-personal-data-{today.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.zip";
        return new PersonalDataPackage(buffer.ToArray(), name, sheets.ToDictionary(s => s.Code, s => s.Rows.Count), files.Count, notes);
    }

    private DocumentSheet Sheet(PersonalDataSheet sheet)
    {
        var role = Enumerable.Range(0, sheet.Columns.Count).Where(i => sheet.Columns[i] == RoleColumn).DefaultIfEmpty(-1).First();
        return new DocumentSheet(
            Text($"Sheet.{sheet.Code}"),
            [.. sheet.Columns.Select(column => Text($"Column.{column}"))],
            [.. sheet.Rows.Select(row => (IReadOnlyList<object?>)[.. row.Select((value, i) => i == role && value is string code ? Text($"Value.{code}") : value)])]);
    }

    private DocumentSheet About(IEnumerable<string> notes, string reference, DateOnly today, FederationSettingsSnapshot federation)
    {
        var form = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ca" ? FederationNameForm.Valencian : FederationNameForm.Spanish;
        var controller = string.Format(CultureInfo.CurrentCulture, Text("About.ControllerValue"), federation.OfficialName(form));
        List<IReadOnlyList<object?>> rows =
        [
            .. AboutItems.Select(item => (IReadOnlyList<object?>)[Text($"About.{item}"), item == "Controller" ? controller : Text($"About.{item}Value")]),
            [Text("About.Reference"), reference],
            [Text("About.GeneratedAt"), today],
        ];
        foreach (var note in notes)
        {
            var (code, argument) = note.Split(':', 2) is [var c, var a] ? (c, a) : (note, string.Empty);
            rows.Add([Text("About.Note"), string.Format(CultureInfo.CurrentCulture, Text($"Note.{code}"), argument)]);
        }

        return new DocumentSheet(Text("Sheet.about"), [Text("Column.item"), Text("Column.value")], rows);
    }

    /// <summary>A text in the request's language; a missing one is a programming error, never printed as its key.</summary>
    private string Text(string key)
    {
        var text = texts[key];
        return text.ResourceNotFound ? throw new InvalidOperationException($"The privacy text {key} is missing.") : text.Value;
    }

    private static void Add(ZipArchive zip, string name, ReadOnlyMemory<byte> content, DateTimeOffset time)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = time;
        using var stream = entry.Open();
        stream.Write(content.Span);
    }
}
