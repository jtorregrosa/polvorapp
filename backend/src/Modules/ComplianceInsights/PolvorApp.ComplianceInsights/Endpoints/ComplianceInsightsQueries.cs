using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComplianceInsights.Contracts;
using PolvorApp.ComplianceInsights.Statistics;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Text;

namespace PolvorApp.ComplianceInsights.Endpoints;

/// <summary>
/// The insights of the caller's scope (design D5): every figure is computed in memory from the
/// scoped, evaluated facts, about 800 rows at most. Each breakdown puts every arquebusier in exactly
/// one category, through exhaustive switches, so the breakdowns always add up to the total.
/// </summary>
internal sealed class ComplianceInsightsQueries(IComparsaScope scope, ScopedFacts scopedFacts, ICatalogDirectory catalog)
{
    public async Task<ComplianceSummaryResponse> SummaryAsync(CancellationToken cancellationToken)
    {
        var access = await scope.GetAccessAsync(cancellationToken);
        var evaluated = await scopedFacts.ReadAsync(access, cancellationToken);
        var (active, reserve) = StatusesOf(evaluated);

        return new ComplianceSummaryResponse(
            active,
            reserve,
            evaluated.Count(e => e.Warnings.Count > 0),
            [.. Enum.GetValues<ComplianceWarning>().Select(code => new WarningCountResponse(code, evaluated.Count(e => e.Warnings.Contains(code))))]);
    }

    /// <summary>The statistics, or null when <paramref name="comparsaId"/> is unknown or outside the scope (BR-12).</summary>
    public async Task<ComplianceStatisticsResponse?> StatisticsAsync(Guid? comparsaId, ArquebusierStatus? status, CancellationToken cancellationToken)
    {
        var access = await scope.GetAccessAsync(cancellationToken);
        if (comparsaId is { } wanted && (!access.CanAccess(wanted) || await catalog.FindComparsaAsync(wanted, cancellationToken) is null))
        {
            return null;
        }

        // With a comparsa filter, only that comparsa is read: it is in scope, checked above.
        var reading = comparsaId is { } only ? ComparsaAccess.Only([only]) : access;
        var evaluated = (await scopedFacts.ReadAsync(reading, cancellationToken))
            .Where(e => status is null || e.Facts.Status == status)
            .ToList();
        var (active, reserve) = StatusesOf(evaluated);
        var licenses = evaluated.CountBy(LicenseStateOf).ToDictionary();

        return new ComplianceStatisticsResponse(
            evaluated.Count,
            active,
            reserve,
            GendersOf(evaluated),
            [.. Enum.GetValues<AgeBracket>().Select(bracket => new AgeBracketCounts(bracket, GendersOf(evaluated.Where(e => AgeBrackets.Of(e.Age) == bracket))))],
            new CourseCounts(
                GendersOf(evaluated.Where(e => e.Facts.TrainingCompletedOn is not null)),
                GendersOf(evaluated.Where(e => e.Facts.TrainingCompletedOn is null))),
            new LicenseStateCounts(
                licenses.GetValueOrDefault(LicenseState.Valid),
                licenses.GetValueOrDefault(LicenseState.Expiring),
                licenses.GetValueOrDefault(LicenseState.Expired),
                licenses.GetValueOrDefault(LicenseState.Pending),
                licenses.GetValueOrDefault(LicenseState.None)),
            await OwnedWeaponsOf(evaluated, cancellationToken),
            comparsaId is null && SeesSeveralComparsas(access) ? await ComparsaRowsAsync(evaluated, cancellationToken) : []);
    }

    /// <summary>Per-comparsa rows only make sense when the caller sees more than one comparsa.</summary>
    private static bool SeesSeveralComparsas(ComparsaAccess access) => access.IsAll || access.ComparsaIds.Count > 1;

    private async Task<OwnedWeaponCounts> OwnedWeaponsOf(List<EvaluatedFacts> evaluated, CancellationToken cancellationToken)
    {
        var modelIds = evaluated.SelectMany(e => e.Facts.OwnedWeaponModelIds).ToList();
        var kinds = (await catalog.FindWeaponModelsAsync([.. modelIds.Distinct()], cancellationToken)).ToDictionary(m => m.Id, m => m.Kind);
        var byKind = modelIds
            .CountBy(id => kinds.TryGetValue(id, out var kind) ? kind : throw new InvalidOperationException($"Weapon model {id} is missing from the catalog."))
            .ToDictionary();

        return new OwnedWeaponCounts(
            GendersOf(evaluated.Where(e => e.Facts.OwnedWeaponModelIds.Count > 0)),
            GendersOf(evaluated.Where(e => e.Facts.OwnedWeaponModelIds.Count == 0)),
            [.. Enum.GetValues<WeaponKind>().Select(kind => new WeaponKindCount(kind, byKind.GetValueOrDefault(kind)))]);
    }

    private async Task<IReadOnlyList<ComparsaStatisticsResponse>> ComparsaRowsAsync(List<EvaluatedFacts> evaluated, CancellationToken cancellationToken)
    {
        var byComparsa = evaluated.GroupBy(e => e.Facts.ComparsaId).ToList();
        var names = (await catalog.FindComparsasAsync([.. byComparsa.Select(g => g.Key)], cancellationToken)).ToDictionary(c => c.Id, c => c.Name);

        return [.. byComparsa
            .Select(group =>
            {
                var (active, reserve) = StatusesOf(group);
                return new ComparsaStatisticsResponse(
                    group.Key,
                    names.TryGetValue(group.Key, out var name) ? name : throw new InvalidOperationException($"Comparsa {group.Key} is missing from the catalog."),
                    group.Count(),
                    active,
                    reserve,
                    GendersOf(group),
                    group.Count(e => e.Warnings.Count > 0));
            })
            .OrderBy(row => row.Name, SpanishOrder.Names)
            .ThenBy(row => row.ComparsaId)];
    }

    private static (int Active, int Reserve) StatusesOf(IEnumerable<EvaluatedFacts> evaluated)
    {
        var counts = evaluated.CountBy(e => e.Facts.Status switch
        {
            ArquebusierStatus.Active => ArquebusierStatus.Active,
            ArquebusierStatus.Reserve => ArquebusierStatus.Reserve,
            var other => throw new InvalidOperationException($"Status {other} is not counted."),
        }).ToDictionary();
        return (counts.GetValueOrDefault(ArquebusierStatus.Active), counts.GetValueOrDefault(ArquebusierStatus.Reserve));
    }

    private static GenderCounts GendersOf(IEnumerable<EvaluatedFacts> evaluated)
    {
        var counts = evaluated.CountBy(e => e.Facts.Gender switch
        {
            Gender.Male => Gender.Male,
            Gender.Female => Gender.Female,
            Gender.Unspecified => Gender.Unspecified,
            var other => throw new InvalidOperationException($"Gender {other} is not counted."),
        }).ToDictionary();
        return new GenderCounts(counts.GetValueOrDefault(Gender.Male), counts.GetValueOrDefault(Gender.Female), counts.GetValueOrDefault(Gender.Unspecified));
    }

    /// <summary>Exactly one license state per arquebusier, from the license and its (exclusive) license warning.</summary>
    private static LicenseState LicenseStateOf(EvaluatedFacts evaluated) => evaluated.Facts.License switch
    {
        null => LicenseState.None,
        ArquebusierLicenseFacts.Pending => LicenseState.Pending,
        ArquebusierLicenseFacts.Issued when evaluated.Warnings.Contains(ComplianceWarning.LicenseExpired) => LicenseState.Expired,
        ArquebusierLicenseFacts.Issued when evaluated.Warnings.Contains(ComplianceWarning.LicenseExpiring) => LicenseState.Expiring,
        ArquebusierLicenseFacts.Issued => LicenseState.Valid,
        _ => throw new InvalidOperationException($"Unknown license shape {evaluated.Facts.License.GetType().Name}."),
    };

    private enum LicenseState
    {
        Valid,
        Expiring,
        Expired,
        Pending,
        None,
    }
}
