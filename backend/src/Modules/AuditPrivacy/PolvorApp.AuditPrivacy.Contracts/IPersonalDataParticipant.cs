using System.Data.Common;

namespace PolvorApp.AuditPrivacy.Contracts;

/// <summary>Whose data a GDPR request is about (UC-26; design D5).</summary>
public abstract record PersonalDataSubject
{
    private PersonalDataSubject()
    {
    }

    /// <summary>An arquebusier or an external weapon owner, by their normalised DNI/NIE.</summary>
    public sealed record Person(string NationalId) : PersonalDataSubject
    {
        /// <summary>Never the value: a subject must not reach logs or exception messages.</summary>
        public override string ToString() => "Person";
    }

    /// <summary>A user of PolvorApp (Admin or FiringChief).</summary>
    public sealed record UserAccount(Guid UserId) : PersonalDataSubject;
}

/// <summary>
/// One module's part of a GDPR request (design D5): it describes, exports and erases what the module
/// holds about a subject. The audit-privacy module calls every participant in <see cref="Order"/>, and a
/// participant that holds nothing about a kind of subject returns an empty part.
/// </summary>
public interface IPersonalDataParticipant
{
    /// <summary>
    /// Where the participant runs, lowest first: the lock order of the modules (see
    /// <see cref="PersonalDataParticipantOrder"/>), so an erasure never deadlocks with a module's own writes.
    /// </summary>
    int Order { get; }

    /// <summary>What the module holds, for the lookup and the erasure warnings.</summary>
    Task<PersonalDataSummary> DescribeAsync(PersonalDataSubject subject, CancellationToken cancellationToken);

    /// <summary>The module's sheets and files of the subject's export, with only the subject's own data.</summary>
    Task<PersonalDataExportPart> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken);

    /// <summary>
    /// First erasure step, on the request's transaction: locks what the module will change and notes
    /// in <paramref name="erasure"/> what later participants need (e.g. the arquebusier's entries before
    /// the registry deletes them). Runs for every participant before any <see cref="EraseAsync"/>.
    /// </summary>
    Task PrepareErasureAsync(PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Second erasure step: deletes or anonymises the module's data on <paramref name="transaction"/>
    /// and adds its counts to <paramref name="erasure"/>. It runs its SQL on the transaction's
    /// connection and never commits it; a failure aborts the whole erasure.
    /// </summary>
    Task EraseAsync(PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>The order of the participants: registry, orders, distribution, identity, catalog, notifications, audit.</summary>
public static class PersonalDataParticipantOrder
{
    public const int Registry = 10;
    public const int Orders = 20;
    public const int Distribution = 30;
    public const int Identity = 40;
    public const int Catalog = 50;
    public const int Notifications = 60;
    public const int Audit = 70;
}

/// <summary>
/// The state of one erasure shared by its participants: what earlier ones found, what each changed
/// (counts only, for the audit entry) and the stored objects to delete after the commit.
/// </summary>
public sealed class PersonalDataErasure
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly List<string> _objectKeys = [];
    private readonly HashSet<Guid> _entryIds = [];
    private readonly HashSet<Guid> _ownedWeaponIds = [];
    private readonly HashSet<Guid> _loanIds = [];

    /// <summary>The registered arquebusier being erased, set by the registry when there is one.</summary>
    public Guid? ArquebusierId { get; set; }

    /// <summary>The registered arquebusier's owned weapons, set by the registry, so loans of them are found after the deletion.</summary>
    public IReadOnlySet<Guid> OwnedWeaponIds => _ownedWeaponIds;

    /// <summary>The edition entries of the person, found by the orders module before the registry deletion.</summary>
    public IReadOnlySet<Guid> EntryIds => _entryIds;

    /// <summary>The loans in which the person is the lender, found by the orders module before the registry deletion.</summary>
    public IReadOnlySet<Guid> LoanIds => _loanIds;

    /// <summary>Whether identity found and locked the user being erased; the other user participants act only then.</summary>
    public bool UserFound { get; set; }

    /// <summary>The erased user's former normalised email, set by identity for the audit redaction; never logged.</summary>
    public string? FormerEmail { get; set; }

    /// <summary>What the participants changed, by count code (e.g. <c>entriesAnonymised</c>).</summary>
    public IReadOnlyDictionary<string, int> Counts => _counts;

    /// <summary>Stored objects (photos) to delete once the erasure is committed.</summary>
    public IReadOnlyList<string> ObjectKeysToDelete => _objectKeys;

    public void AddEntries(IEnumerable<Guid> entryIds) => _entryIds.UnionWith(entryIds);

    public void AddOwnedWeapons(IEnumerable<Guid> ownedWeaponIds) => _ownedWeaponIds.UnionWith(ownedWeaponIds);

    public void AddLoans(IEnumerable<Guid> loanIds) => _loanIds.UnionWith(loanIds);

    public void Count(string code, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (count > 0)
        {
            _counts[code] = _counts.GetValueOrDefault(code) + count;
        }
    }

    public void DeleteObjectsAfterCommit(IEnumerable<string> objectKeys) => _objectKeys.AddRange(objectKeys);

    /// <summary>Whether any participant changed anything.</summary>
    public bool ChangedAnything => _counts.Count > 0;
}

/// <summary>A sheet of an export: a code and column codes translated by the audit module, and the rows.</summary>
public sealed record PersonalDataSheet(string Code, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows)
{
    /// <summary>Never the rows: they are personal data.</summary>
    public override string ToString() => $"PersonalDataSheet({Code}, {Rows.Count} rows)";
}

/// <summary>A file of an export, e.g. a photo.</summary>
public sealed record PersonalDataFile(string Name, string ContentType, ReadOnlyMemory<byte> Content)
{
    public override string ToString() => $"PersonalDataFile({Name}, {Content.Length} bytes)";
}

/// <summary>
/// One module's part of an export. <see cref="Notes"/> are codes the workbook explains in "About this
/// data", e.g. a photo whose stored file is missing or a sheet that was capped: an export never omits
/// data silently.
/// </summary>
public sealed record PersonalDataExportPart(IReadOnlyList<PersonalDataSheet> Sheets, IReadOnlyList<PersonalDataFile> Files)
{
    public static readonly PersonalDataExportPart Empty = new([], []);

    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Never the contents: they are personal data.</summary>
    public override string ToString() => $"PersonalDataExportPart({Sheets.Count} sheets, {Files.Count} files, {Notes.Count} notes)";
}
