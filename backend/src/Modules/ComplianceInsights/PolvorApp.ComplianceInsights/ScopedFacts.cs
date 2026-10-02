using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComplianceInsights.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.ComplianceInsights;

/// <summary>An arquebusier's facts with their age and warnings on the reference date.</summary>
internal sealed record EvaluatedFacts(ArquebusierFacts Facts, int Age, IReadOnlyList<ComplianceWarning> Warnings);

/// <summary>
/// Reads the registry facts within the caller's scope (BR-12) and evaluates their age and warnings
/// once, on one reference date for the whole request (design D5). The only place where this module
/// asks the registry for facts, so the scope rule of <see cref="IArquebusierFacts"/> is applied here
/// and nowhere else.
/// </summary>
internal sealed class ScopedFacts(IArquebusierFacts facts, IComplianceRules rules, TimeProvider time)
{
    /// <summary>
    /// The evaluated facts of every arquebusier <paramref name="access"/> may see, on today's date in
    /// Europe/Madrid; none for <see cref="ComparsaAccess.None"/>.
    /// </summary>
    public async Task<IReadOnlyList<EvaluatedFacts>> ReadAsync(ComparsaAccess access, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        var today = FederationCalendar.Today(time);
        var read = access.IsAll
            ? await facts.ListAllAsync(cancellationToken)
            : await facts.ListAsync(access.ComparsaIds, cancellationToken);
        return [.. read.Select(f => new EvaluatedFacts(f, rules.AgeOn(f.BirthDate, today), rules.Evaluate(ComplianceFactsOf(f), today)))];
    }

    /// <summary>The registry's facts as the rules read them; the license hierarchy maps one to one.</summary>
    private static ComplianceFacts ComplianceFactsOf(ArquebusierFacts facts) =>
        new(
            facts.BirthDate,
            facts.License switch
            {
                null => null,
                ArquebusierLicenseFacts.Pending => new ComplianceLicense.Pending(),
                ArquebusierLicenseFacts.Issued issued => new ComplianceLicense.Issued(issued.ExpiresOn, issued.HasFrontPhoto, issued.HasBackPhoto),
                _ => throw new InvalidOperationException($"Unknown license shape {facts.License.GetType().Name}."),
            },
            facts.TrainingCompletedOn,
            facts.HasIdPhoto);
}
