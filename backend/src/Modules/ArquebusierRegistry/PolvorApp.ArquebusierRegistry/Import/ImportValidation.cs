using System.Collections.Frozen;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Insights;
using PolvorApp.ComplianceInsights.Contracts;

namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>A checked file: its report and, for every row without errors, the validated input in sheet order.</summary>
internal sealed record ImportValidationResult(ArquebusierImportReport Report, IReadOnlyList<ArquebusierInput> Inputs)
{
    public bool HasErrors => Report.ErrorRowCount > 0;

    /// <summary>The counts only: the inputs are personal data.</summary>
    public override string ToString() => $"{nameof(ImportValidationResult)} {Report.RowCount} rows, {Report.ErrorRowCount} with errors";
}

/// <summary>
/// Validates every row of a sheet (spec: Import validation report; design D5, D6): the registration's
/// blocking rules, duplicates inside the file and against the registry (BR-02, without saying whose),
/// and the compliance warnings of valid rows (BR-04), which never block.
/// </summary>
internal static class ImportValidation
{
    public const string DuplicateInFile = "duplicateInFile";
    public const string Taken = "taken";

    private const string NationalIdField = "nationalId";
    private const string FederationIdField = "federationId";

    private static readonly FrozenDictionary<string, int> ColumnOrderByField =
        ImportColumns.All.ToFrozenDictionary(ImportColumns.Field, column => (int)column, StringComparer.Ordinal);

    /// <summary>The valid identities of the file, to look up in the registry in one query.</summary>
    public static (IReadOnlyList<string> NationalIds, IReadOnlyList<int> FederationIds) Identities(ImportSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var identities = sheet.Rows.Select(ImportCellReader.Identity).ToList();
        return (
            [.. identities.Select(identity => identity.NationalId).OfType<string>().Distinct(StringComparer.Ordinal)],
            [.. identities.Select(identity => identity.FederationId).OfType<int>().Distinct()]);
    }

    /// <param name="sheet">The rows read.</param>
    /// <param name="today">Today in Europe/Madrid: the reference date of the rules.</param>
    /// <param name="takenNationalIds">Which of the file's nationalIds an arquebusier already has.</param>
    /// <param name="takenFederationIds">Which of the file's federationIds an arquebusier already has.</param>
    /// <param name="rules">The compliance rules, owned by the compliance insights module.</param>
    public static ImportValidationResult Validate(
        ImportSheet sheet,
        DateOnly today,
        IReadOnlySet<string> takenNationalIds,
        IReadOnlySet<int> takenFederationIds,
        IComplianceRules rules)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(rules);
        var checkedRows = sheet.Rows.Select(row => (Row: row, Identity: ImportCellReader.Identity(row), Read: ImportCellReader.Read(row, today))).ToList();
        var repeatedNationalIds = Repeated(checkedRows.Select(row => row.Identity.NationalId).OfType<string>());
        var repeatedFederationIds = Repeated(checkedRows.Select(row => row.Identity.FederationId).OfType<int>());

        List<ArquebusierImportRow> reported = [];
        List<ArquebusierInput> inputs = [];
        var errorRows = 0;
        var warningRows = 0;
        foreach (var (row, (nationalId, federationId), (input, found)) in checkedRows)
        {
            var errors = new Dictionary<string, string>(found, StringComparer.Ordinal);
            if (nationalId is not null)
            {
                Identify(errors, NationalIdField, repeatedNationalIds.Contains(nationalId), takenNationalIds.Contains(nationalId));
            }

            if (federationId is { } number)
            {
                Identify(errors, FederationIdField, repeatedFederationIds.Contains(number), takenFederationIds.Contains(number));
            }

            var (lastName, firstName) = ImportCellReader.Names(row);
            if (errors.Count > 0)
            {
                errorRows++;
                reported.Add(new ArquebusierImportRow(row.RowNumber, lastName, firstName, Ordered(errors), []));
                continue;
            }

            if (input is null)
            {
                throw new InvalidOperationException($"Import row {row.RowNumber} has neither an input nor an error.");
            }

            inputs.Add(input);
            var warnings = Warnings(input, today, rules);
            if (warnings.Count > 0)
            {
                warningRows++;
                reported.Add(new ArquebusierImportRow(row.RowNumber, lastName, firstName, [], warnings));
            }
        }

        var report = new ArquebusierImportReport(sheet.Rows.Count, inputs.Count, errorRows, warningRows, sheet.IgnoredColumns, reported);
        return new ImportValidationResult(report, inputs);
    }

    /// <summary>
    /// A repeated or registered identity is an error on the row (only valid identities get here).
    /// "Already registered" wins over "repeated in the file": removing the repetition would not help.
    /// </summary>
    private static void Identify(Dictionary<string, string> errors, string field, bool repeated, bool taken)
    {
        if (taken)
        {
            errors[field] = Taken;
        }
        else if (repeated)
        {
            errors[field] = DuplicateInFile;
        }
    }

    private static HashSet<T> Repeated<T>(IEnumerable<T> values) =>
        [.. values.GroupBy(value => value).Where(group => group.Count() > 1).Select(group => group.Key)];

    /// <summary>
    /// The warnings the arquebusier would have once imported, except the photo ones: no import brings
    /// photos, so the page says so once instead of on every row (design D6).
    /// </summary>
    private static List<ComplianceWarning> Warnings(ArquebusierInput input, DateOnly today, IComplianceRules rules)
    {
        var facts = RegistryCompliance.FactsOf(
            Guid.Empty,
            input.BirthDate,
            input.TrainingCompletedOn,
            new LicenseColumns(input.License?.Type, input.License?.Pending ?? false, input.License?.ExpiresOn),
            new PhotoFlags(Id: true, LicenseFront: true, LicenseBack: true));
        return [.. rules.Evaluate(facts, today)];
    }

    /// <summary>Errors in template column order, so the report reads like the sheet.</summary>
    private static List<ArquebusierImportError> Ordered(Dictionary<string, string> errors) =>
        [.. errors
            .OrderBy(error => ColumnOrder(error.Key))
            .ThenBy(error => error.Key, StringComparer.Ordinal)
            .Select(error => new ArquebusierImportError(error.Key, error.Value))];

    private static int ColumnOrder(string field) => ColumnOrderByField.GetValueOrDefault(field, int.MaxValue);
}
