using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ComplianceInsights.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>
/// How checking or importing a file ended: a blocking outcome (comparsa, capacity, race), a file
/// that cannot be read, the report of a check or of rejected rows, or the number imported.
/// </summary>
internal sealed record ImportCheck
{
    private ImportCheck(RegistryOutcome outcome, ImportFileProblem? fileProblem, ImportValidationResult? result, int importedCount)
    {
        Outcome = outcome;
        FileProblem = fileProblem;
        Result = result;
        ImportedCount = importedCount;
    }

    public RegistryOutcome Outcome { get; }

    public ImportFileProblem? FileProblem { get; }

    /// <summary>The validation of the rows; for an import, only when rows have errors.</summary>
    public ImportValidationResult? Result { get; }

    public int ImportedCount { get; }

    public static ImportCheck Refused(RegistryOutcome outcome) =>
        outcome == RegistryOutcome.Done
            ? throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A refusal needs a problem outcome.")
            : new(outcome, null, null, 0);

    public static ImportCheck Unreadable(ImportFileProblem problem) => new(RegistryOutcome.Done, problem, null, 0);

    public static ImportCheck Checked(ImportValidationResult result) => new(RegistryOutcome.Done, null, result, 0);

    public static ImportCheck RowErrors(ImportValidationResult result) => new(RegistryOutcome.ImportRowErrors, null, result, 0);

    public static ImportCheck Imported(int count) => new(RegistryOutcome.Done, null, null, count);
}

/// <summary>
/// Checks and imports a file for one comparsa (specs: Import file reading, Import validation report,
/// All-or-nothing import, Imports are audited; design D1, D5–D7). A check stores nothing. An import
/// validates the file again against the registry as it is at that moment and registers every row,
/// with its audit entries, in one transaction, or nothing. The file is never kept; only counts are
/// logged. Callers are Admins (the endpoints require it), so every active comparsa is a valid target.
/// </summary>
internal sealed partial class ArquebusierImporter(
    ArquebusierRegistryDbContext db,
    ICatalogDirectory catalog,
    IComplianceRules rules,
    IAuditTrail trail,
    IComparsaScope scope,
    RegistryWriteGuard guard,
    TimeProvider time,
    ImportSlots slots,
    ILogger<ArquebusierImporter> logger)
{
    public const string ImportedAction = "ArquebusiersImported";
    public const string ComparsaEntityType = "Comparsa";

    public async Task<ImportCheck> CheckAsync(Guid comparsaId, byte[] content, CancellationToken cancellationToken)
    {
        var (stop, sheet) = await PrepareAsync(comparsaId, content, cancellationToken);
        if (stop is not null)
        {
            return stop;
        }

        var result = await ValidateAsync(sheet!, cancellationToken);
        LogChecked(logger, comparsaId, result.Report.RowCount, result.Report.ErrorRowCount, result.Report.WarningRowCount);
        return ImportCheck.Checked(result);
    }

    public async Task<ImportCheck> ImportAsync(Guid comparsaId, byte[] content, CancellationToken cancellationToken)
    {
        var (stop, sheet) = await PrepareAsync(comparsaId, content, cancellationToken);
        if (stop is not null)
        {
            return stop;
        }

        var (outcome, done) = await guard.RunAsync<ImportCheck>(nameof(ImportAsync), null, null, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            await db.LockImportsAsync(cancellationToken);
            var result = await ValidateAsync(sheet!, cancellationToken);
            if (result.HasErrors)
            {
                return (RegistryOutcome.ImportRowErrors, ImportCheck.RowErrors(result));
            }

            Register(comparsaId, result.Inputs);
            var saved = await SaveAsync(comparsaId, cancellationToken);
            if (saved != RegistryOutcome.Done)
            {
                return (saved, null);
            }

            // Once the rows are saved, a client that disconnects must not leave the outcome unknown.
            await transaction.CommitAsync(CancellationToken.None);
            return (RegistryOutcome.Done, ImportCheck.Imported(result.Inputs.Count));
        });

        if (outcome == RegistryOutcome.Done)
        {
            LogImported(logger, comparsaId, done!.ImportedCount);
            return done;
        }

        LogImportRefused(logger, comparsaId, outcome, done?.Result?.Report.ErrorRowCount ?? 0);
        return done ?? ImportCheck.Refused(outcome);
    }

    /// <summary>Validates the rows against the registry as it is now (BR-02 across every comparsa).</summary>
    internal async Task<ImportValidationResult> ValidateAsync(ImportSheet sheet, CancellationToken cancellationToken)
    {
        var (nationalIds, federationIds) = ImportValidation.Identities(sheet);
        if (nationalIds.Count == 0 && federationIds.Count == 0)
        {
            return ImportValidation.Validate(sheet, FederationCalendar.Today(time), new HashSet<string>(), new HashSet<int>(), rules);
        }

        var taken = await db.Arquebusiers.AsNoTracking()
            .Where(a => nationalIds.Contains(a.NationalId) || federationIds.Contains(a.FederationId))
            .Select(a => new { a.NationalId, a.FederationId })
            .ToListAsync(cancellationToken);

        // Only the values are compared, so the report can never say whose they are.
        var takenNationalIds = taken.Select(a => a.NationalId).ToHashSet(StringComparer.Ordinal);
        var takenFederationIds = taken.Select(a => a.FederationId).ToHashSet();
        return ImportValidation.Validate(sheet, FederationCalendar.Today(time), takenNationalIds, takenFederationIds, rules);
    }

    /// <summary>The comparsa check and the reading of the file, shared by a check and an import.</summary>
    private async Task<(ImportCheck? Stop, ImportSheet? Sheet)> PrepareAsync(Guid comparsaId, byte[] content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        // The endpoints are for Admins only; the scope check keeps any other caller to its comparsas (BR-12).
        if (!(await scope.GetAccessAsync(cancellationToken)).CanAccess(comparsaId))
        {
            return (ImportCheck.Refused(RegistryOutcome.ComparsaNotFound), null);
        }

        if (await ComparsaOutcomeAsync(comparsaId, cancellationToken) is { } refused)
        {
            return (ImportCheck.Refused(refused), null);
        }

        var (read, sheet, problem) = await ReadAsync(content, cancellationToken);
        if (!read)
        {
            LogNoSlot(logger, comparsaId);
            return (ImportCheck.Refused(RegistryOutcome.Busy), null);
        }

        return sheet is null ? (ImportCheck.Unreadable(problem!), null) : (null, sheet);
    }

    /// <summary>
    /// Reads the workbook in one of the slots: only the load needs it, because only the load builds
    /// a large model in memory. False when no slot freed up in time.
    /// </summary>
    private async Task<(bool Read, ImportSheet? Sheet, ImportFileProblem? Problem)> ReadAsync(byte[] content, CancellationToken cancellationToken)
    {
        using var slot = await slots.EnterAsync(cancellationToken);
        if (slot is null)
        {
            return (false, null, null);
        }

        var (sheet, problem) = ImportWorkbookReader.Read(content, logger);
        return (true, sheet, problem);
    }

    /// <summary>
    /// Adds every arquebusier with its registration entry, marked as an import, and one import entry
    /// with the count (design D7). No entry holds a personal value, the file name or a row number.
    /// </summary>
    private void Register(Guid comparsaId, IReadOnlyList<ArquebusierInput> inputs)
    {
        var now = time.GetUtcNow();
        foreach (var input in inputs)
        {
            var arquebusier = ArquebusierAdministration.New(comparsaId, input, now);
            db.Arquebusiers.Add(arquebusier);
            trail.Record(db, new AuditRecord(
                ArquebusierAdministration.RegisteredAction, ArquebusierAdministration.EntityType, arquebusier.Id.ToString(),
                new { source = "import" }, ComparsaId: comparsaId));
        }

        trail.Record(db, new AuditRecord(ImportedAction, ComparsaEntityType, comparsaId.ToString(), new { count = inputs.Count }, ComparsaId: comparsaId));
    }

    /// <summary>
    /// Saves every row at once. A unique value taken meanwhile by another request (the indexes decide
    /// races) rejects the whole import; so does the comparsa being deleted after the check.
    /// </summary>
    private async Task<RegistryOutcome> SaveAsync(Guid comparsaId, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return RegistryOutcome.Done;
        }
        catch (DbUpdateException exception) when (RegistryProblems.ViolatedConstraint(exception) is { } constraint)
        {
            db.ChangeTracker.Clear();
            var outcome = constraint switch
            {
                ArquebusierRegistryDbContext.NationalIdIndex or ArquebusierRegistryDbContext.FederationIdIndex => RegistryOutcome.ImportConflict,
                ArquebusierRegistryDbContext.ComparsaForeignKey => RegistryOutcome.ComparsaNotFound,

                // Not the original exception: its database detail can quote the row, which is personal data.
                _ => throw new InvalidOperationException($"Import for comparsa {comparsaId} violated the unexpected constraint {constraint}."),
            };
            guard.LostRace(comparsaId, constraint);
            return outcome;
        }
    }

    /// <summary>The comparsa must exist and be active, as for a registration.</summary>
    private async Task<RegistryOutcome?> ComparsaOutcomeAsync(Guid comparsaId, CancellationToken cancellationToken) =>
        await catalog.FindComparsaAsync(comparsaId, cancellationToken) switch
        {
            null => RegistryOutcome.ComparsaNotFound,
            { Active: false } => RegistryOutcome.ComparsaInactive,
            _ => null,
        };

    [LoggerMessage(Level = LogLevel.Information, Message = "Import file for comparsa {ComparsaId} checked: {Rows} rows, {ErrorRows} with errors, {WarningRows} with warnings")]
    private static partial void LogChecked(ILogger logger, Guid comparsaId, int rows, int errorRows, int warningRows);

    [LoggerMessage(Level = LogLevel.Information, Message = "Import for comparsa {ComparsaId} registered {Count} arquebusiers")]
    private static partial void LogImported(ILogger logger, Guid comparsaId, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Import for comparsa {ComparsaId} refused with {Outcome} ({ErrorRows} rows with errors); nothing was stored")]
    private static partial void LogImportRefused(ILogger logger, Guid comparsaId, RegistryOutcome outcome, int errorRows);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import file for comparsa {ComparsaId} refused: every workbook-reading slot is busy")]
    private static partial void LogNoSlot(ILogger logger, Guid comparsaId);
}
