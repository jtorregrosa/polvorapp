namespace PolvorApp.ArquebusierRegistry.Contracts;

/// <summary>
/// Read contract of the registry for derived insights (change add-compliance-insights, design D3):
/// what the compliance rules and the statistics need about each arquebusier, and nothing that
/// identifies them.
/// </summary>
/// <remarks>
/// It applies no comparsa scope. The facts are personal data, so callers MUST take the comparsas from
/// <c>IComparsaScope.GetAccessAsync</c>:
/// <list type="bullet">
/// <item>call <see cref="ListAllAsync"/> only when the access covers every comparsa;</item>
/// <item>otherwise call <see cref="ListAsync"/> with the comparsas of the access;</item>
/// <item>check a user-chosen comparsa against the access first, and answer 404 when it is outside (BR-12).</item>
/// </list>
/// Never return the facts themselves to a client: only aggregates.
/// </remarks>
public interface IArquebusierFacts
{
    /// <summary>The facts of every arquebusier of the Federation, in no defined order.</summary>
    Task<IReadOnlyList<ArquebusierFacts>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>The facts of the arquebusiers of <paramref name="comparsaIds"/>, in no defined order; none for an empty set.</summary>
    Task<IReadOnlyList<ArquebusierFacts>> ListAsync(IReadOnlyCollection<Guid> comparsaIds, CancellationToken cancellationToken);
}

/// <summary>
/// One arquebusier as seen by the insights: no name, national ID, federation ID or contact data. Its
/// text form prints no value, because the birth date and gender are personal data.
/// </summary>
/// <param name="ArquebusierId">
/// The arquebusier, only to join server-side sets such as the first-year rule (add-comparsa-orders,
/// design D5); never returned to a client.
/// </param>
/// <param name="ComparsaId">The current comparsa.</param>
/// <param name="Status">Active or Reserve.</param>
/// <param name="Gender">Gender, for equality reports only.</param>
/// <param name="BirthDate">Birth date, from which the age is derived.</param>
/// <param name="License">The current license, or null when the arquebusier has none.</param>
/// <param name="TrainingCompletedOn">The course date, or null when the course is not done.</param>
/// <param name="HasIdPhoto">Whether the arquebusier has an ID photo.</param>
/// <param name="OwnedWeaponModelIds">The catalogue model of each owned weapon, one entry per weapon.</param>
public sealed record ArquebusierFacts(
    Guid ArquebusierId,
    Guid ComparsaId,
    ArquebusierStatus Status,
    Gender Gender,
    DateOnly BirthDate,
    ArquebusierLicenseFacts? License,
    DateOnly? TrainingCompletedOn,
    bool HasIdPhoto,
    IReadOnlyList<Guid> OwnedWeaponModelIds)
{
    /// <summary>The type name only: the members are personal data.</summary>
    public override string ToString() => nameof(ArquebusierFacts);
}

/// <summary>
/// The current license: <see cref="Pending"/>, which has no dates, or <see cref="Issued"/>, which has
/// its expiry date and may have its photos. Closed to these two cases.
/// </summary>
public abstract record ArquebusierLicenseFacts
{
    private ArquebusierLicenseFacts()
    {
    }

    /// <summary>Applied for but not issued yet.</summary>
    public sealed record Pending : ArquebusierLicenseFacts;

    /// <summary>Issued, valid through <paramref name="ExpiresOn"/> in Europe/Madrid.</summary>
    public sealed record Issued(DateOnly ExpiresOn, bool HasFrontPhoto, bool HasBackPhoto) : ArquebusierLicenseFacts
    {
        /// <summary>The type name only: the expiry date is personal data.</summary>
        public override string ToString() => nameof(Issued);
    }
}
