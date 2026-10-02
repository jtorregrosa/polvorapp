using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Insights;
using PolvorApp.ArquebusierRegistry.Licenses;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ComplianceInsights.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Text;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.ArquebusierRegistry.Endpoints;

/// <summary>
/// Scoped reads of the registry (spec: Arquebusier visibility, BR-12). Every query goes through the
/// caller's comparsa scope, so an arquebusier outside it is never returned.
/// </summary>
internal sealed class ArquebusierQueries(
    ArquebusierRegistryDbContext db, IComparsaScope scope, ICatalogDirectory catalog, IComplianceRules rules, TimeProvider time)
{
    /// <summary>
    /// The scoped list, sorted by last then first name in Spanish order. At most about 800 rows for an
    /// Admin, so it is filtered in SQL and sorted in memory, whatever the database collation (design D6).
    /// </summary>
    public async Task<List<ArquebusierRowResponse>> ListAsync(Guid? comparsaId, ArquebusierStatus? status, CancellationToken cancellationToken)
    {
        var access = await scope.GetAccessAsync(cancellationToken);
        var query = access.Filter(db.Arquebusiers.AsNoTracking(), a => a.ComparsaId);
        if (comparsaId is { } wantedComparsa)
        {
            query = query.Where(a => a.ComparsaId == wantedComparsa);
        }

        if (status is { } wantedStatus)
        {
            query = query.Where(a => a.Status == wantedStatus);
        }

        // Only the listed columns, plus what the compliance rules need: the birth date and the course
        // date are read to derive the warnings but never returned, and contact data is never read.
        var rows = await query
            .Select(a => new
            {
                a.Id,
                a.FirstName,
                a.LastName,
                a.NationalId,
                a.FederationId,
                a.ComparsaId,
                a.Status,
                a.LicenseType,
                a.LicensePending,
                a.LicenseIssuedOn,
                a.LicenseExpiresOn,
                a.BirthDate,
                a.TrainingCompletedOn,
                HasIdPhoto = db.Photos.Any(p => p.ArquebusierId == a.Id && p.Kind == ArquebusierPhotoKind.Id),
                HasFrontPhoto = db.Photos.Any(p => p.ArquebusierId == a.Id && p.Kind == ArquebusierPhotoKind.LicenseFront),
                HasBackPhoto = db.Photos.Any(p => p.ArquebusierId == a.Id && p.Kind == ArquebusierPhotoKind.LicenseBack),
            })
            .ToListAsync(cancellationToken);
        var comparsas = (await catalog.FindComparsasAsync([.. rows.Select(a => a.ComparsaId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.Name);
        var today = FederationCalendar.Today(time);

        return [.. rows
            .OrderBy(a => a.LastName, SpanishOrder.Names)
            .ThenBy(a => a.FirstName, SpanishOrder.Names)
            .ThenBy(a => a.Id)
            .Select(a => new ArquebusierRowResponse(
                a.Id,
                a.FirstName,
                a.LastName,
                a.NationalId,
                a.FederationId,
                a.ComparsaId,
                comparsas.TryGetValue(a.ComparsaId, out var name)
                    ? name
                    : throw new InvalidOperationException($"Comparsa {a.ComparsaId} of arquebusier {a.Id} is missing from the catalog."),
                a.Status,
                a.LicenseType is { } type ? new License(type, a.LicensePending, a.LicenseIssuedOn, a.LicenseExpiresOn).StatusOn(today) : null,
                a.LicenseExpiresOn,
                a.HasIdPhoto,
                rules.Evaluate(
                    RegistryCompliance.FactsOf(
                        a.Id,
                        a.BirthDate,
                        a.TrainingCompletedOn,
                        new LicenseColumns(a.LicenseType, a.LicensePending, a.LicenseExpiresOn),
                        new PhotoFlags(a.HasIdPhoto, a.HasFrontPhoto, a.HasBackPhoto)),
                    today)))];
    }

    /// <summary>The arquebusier, read-only, or null when it does not exist or is outside the caller's scope.</summary>
    public async Task<Arquebusier?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = await scope.GetAccessAsync(cancellationToken);
        return await access.Filter(db.Arquebusiers.AsNoTracking(), a => a.ComparsaId).SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
    }
}
