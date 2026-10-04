using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.AuditPrivacy.Maintenance;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.AuditPrivacy.Privacy;

/// <summary>
/// The audit trail's part of a GDPR request about a user (UC-26; design D8): their activity (entries in
/// which they acted or that are about their account), without the recorded data of entries about other
/// people. The erasure keeps every entry and its ids (accountability) and only redacts the one personal
/// value the trail still holds about a user: the attempted email of failed sign-ins.
/// </summary>
internal sealed class AuditPersonalData(AuditDbContext db) : IPersonalDataParticipant
{
    public const string ActivitySheet = "activity";

    /// <summary>Rows of the activity sheet; a note in "About this data" says when it was capped.</summary>
    public const int MaxActivityRows = 100_000;

    /// <summary>The note of a capped activity sheet.</summary>
    public const string ActivityCappedNote = "activityCapped";

    public int Order => PersonalDataParticipantOrder.Audit;

    public Task<PersonalDataSummary> DescribeAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        Task.FromResult(PersonalDataSummary.Empty);

    public async Task<PersonalDataExportPart> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (subject is not PersonalDataSubject.UserAccount(var userId))
        {
            return PersonalDataExportPart.Empty;
        }

        var id = userId.ToString();
        // The newest first: when capped, the oldest activity is the part left out, and a note says so.
        var rows = await db.Set<AuditEntry>().AsNoTracking()
            .Where(e => e.ActorUserId == userId || (e.EntityType == "User" && e.EntityId == id))
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Take(MaxActivityRows + 1)
            .Select(e => new { e.OccurredAt, e.Action, e.EntityType, e.EntityId })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return PersonalDataExportPart.Empty;
        }

        var capped = rows.Count > MaxActivityRows;
        return new PersonalDataExportPart(
            [
                new PersonalDataSheet(
                    ActivitySheet,
                    ["occurredAt", "action", "entityType", "entityId"],
                    [.. rows.Take(MaxActivityRows).Select(r => (IReadOnlyList<object?>)[r.OccurredAt, r.Action, r.EntityType, r.EntityId])]),
            ],
            [])
        {
            Notes = capped ? [ActivityCappedNote] : [],
        };
    }

    public Task PrepareErasureAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task EraseAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(erasure);
        if (subject is not PersonalDataSubject.UserAccount || !erasure.UserFound || erasure.FormerEmail is not { } email)
        {
            return;
        }

        var redacted = await AuditMaintenance.RedactAsync(transaction, "SignInFailed", "attemptedEmail", email, cancellationToken);
        erasure.Count("auditEntriesRedacted", redacted);
    }
}
