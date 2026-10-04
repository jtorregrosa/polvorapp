using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.FederationCatalog.Privacy;

/// <summary>
/// The catalogue's part of a GDPR request about a user (UC-26; add-audit-privacy, design D5): their
/// FiringChief assignments, exported with the comparsa's name and removed by the erasure.
/// </summary>
internal sealed class CatalogPersonalData(FederationCatalogDbContext db) : IPersonalDataParticipant
{
    public const string AssignmentsSheet = "assignments";

    public int Order => PersonalDataParticipantOrder.Catalog;

    public async Task<PersonalDataSummary> DescribeAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (subject is not PersonalDataSubject.UserAccount(var userId))
        {
            return PersonalDataSummary.Empty;
        }

        var count = await db.Assignments.CountAsync(a => a.UserId == userId, cancellationToken);
        return count == 0 ? PersonalDataSummary.Empty : new PersonalDataSummary { Counts = new Dictionary<string, int> { ["assignments"] = count } };
    }

    public async Task<PersonalDataExportPart> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (subject is not PersonalDataSubject.UserAccount(var userId))
        {
            return PersonalDataExportPart.Empty;
        }

        var rows = await (
            from assignment in db.Assignments.AsNoTracking()
            join comparsa in db.Comparsas.AsNoTracking() on assignment.ComparsaId equals comparsa.Id
            where assignment.UserId == userId
            orderby comparsa.Name
            select new { comparsa.Name, assignment.AssignedAt })
            .ToListAsync(cancellationToken);
        return rows.Count == 0
            ? PersonalDataExportPart.Empty
            : new PersonalDataExportPart(
                [new PersonalDataSheet(AssignmentsSheet, ["comparsa", "assignedAt"], [.. rows.Select(r => (IReadOnlyList<object?>)[r.Name, r.AssignedAt])])],
                []);
    }

    public Task PrepareErasureAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task EraseAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(erasure);
        if (subject is not PersonalDataSubject.UserAccount(var userId) || !erasure.UserFound)
        {
            return;
        }

        await db.EnlistAsync(transaction, cancellationToken);
        var removed = await db.Database.ExecuteSqlAsync(
            $"DELETE FROM catalog.firing_chief_assignments WHERE user_id = {userId}", cancellationToken);
        erasure.Count("assignmentsRemoved", removed);
    }
}
