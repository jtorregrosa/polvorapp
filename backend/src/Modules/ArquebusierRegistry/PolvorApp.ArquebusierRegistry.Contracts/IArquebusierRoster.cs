namespace PolvorApp.ArquebusierRegistry.Contracts;

/// <summary>
/// Read contract of the registry for comparsa orders (add-comparsa-orders, design D5): what an order
/// needs about each arquebusier, the lookup of a lender by an exact national ID, and the owned weapons
/// that orders and loans name. Server-side only.
/// </summary>
/// <remarks>
/// It applies no comparsa scope. Callers MUST check the comparsa against
/// <c>IComparsaScope.GetAccessAsync</c> before listing it (BR-12). The national IDs and ownership
/// guides are returned only so orders can keep their history copy and later exports can use them: an
/// order response never shows a registered owner's guide, and the lender lookup never shows any guide.
/// No method returns contact data or the gender.
/// </remarks>
public interface IArquebusierRoster
{
    /// <summary>The arquebusiers of the comparsa, by last name and first name in Spanish order.</summary>
    Task<IReadOnlyList<RosterArquebusier>> ListByComparsaAsync(Guid comparsaId, CancellationToken cancellationToken);

    /// <summary>
    /// The arquebusiers with these ids, of any comparsa, in no defined order; unknown ids are skipped.
    /// Unscoped: pass only ids read from stored records (an order's entries, a loan's lender), never ids
    /// taken from a request, or a FiringChief could read another comparsa's arquebusiers.
    /// </summary>
    Task<IReadOnlyList<RosterArquebusier>> FindManyAsync(IReadOnlyCollection<Guid> arquebusierIds, CancellationToken cancellationToken);

    /// <summary>
    /// The arquebusier whose national ID is exactly <paramref name="normalisedNationalId"/> (validated
    /// and normalised with <c>NationalId.Parse</c> first), with their owned weapons without ownership
    /// guides, or null when no arquebusier has it. A cross-comparsa existence check: only the lender
    /// lookup, which is rate-limited and audited (design D9), may call it.
    /// </summary>
    Task<LenderSummary?> FindLenderAsync(string normalisedNationalId, CancellationToken cancellationToken);

    /// <summary>
    /// The owned weapons with these ids, in no defined order; unknown ids are skipped. Unscoped: callers
    /// check the owner against the entry's arquebusier or a prior lender lookup before using a weapon.
    /// </summary>
    Task<IReadOnlyList<RosterWeapon>> FindOwnedWeaponsAsync(IReadOnlyCollection<Guid> ownedWeaponIds, CancellationToken cancellationToken);

    /// <summary>
    /// Whether an arquebusier has exactly <paramref name="normalisedNationalId"/>. A cross-comparsa
    /// existence check: only the loan write, to refuse a registered owner typed as external, may call it.
    /// </summary>
    Task<bool> IsNationalIdRegisteredAsync(string normalisedNationalId, CancellationToken cancellationToken);
}

/// <summary>
/// One arquebusier as an order sees them. Its text form prints no value, because the members are
/// personal data.
/// </summary>
/// <param name="Id">Arquebusier identifier.</param>
/// <param name="ComparsaId">The current comparsa.</param>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name.</param>
/// <param name="NationalId">Normalised DNI or NIE, for the entry's history copy.</param>
/// <param name="FederationId">ID in the Federation's external app.</param>
/// <param name="Status">Registry status, the default of a new entry.</param>
/// <param name="BirthDate">Birth date, for the compliance warnings only.</param>
/// <param name="License">The current license, or null when there is none.</param>
/// <param name="TrainingCompletedOn">The course date, or null.</param>
/// <param name="HasIdPhoto">Whether there is an ID photo.</param>
/// <param name="Weapons">The owned weapons, oldest first.</param>
public sealed record RosterArquebusier(
    Guid Id,
    Guid ComparsaId,
    string FirstName,
    string LastName,
    string NationalId,
    int FederationId,
    ArquebusierStatus Status,
    DateOnly BirthDate,
    ArquebusierLicenseFacts? License,
    DateOnly? TrainingCompletedOn,
    bool HasIdPhoto,
    IReadOnlyList<RosterWeapon> Weapons)
{
    /// <summary>The type name only: the members are personal data.</summary>
    public override string ToString() => nameof(RosterArquebusier);
}

/// <summary>An owned weapon. Its text form prints no value.</summary>
/// <param name="Id">Owned weapon identifier.</param>
/// <param name="OwnerId">The owning arquebusier.</param>
/// <param name="WeaponModelId">Its catalogue model.</param>
/// <param name="WeaponNumber">Number engraved on the stock.</param>
/// <param name="OwnershipGuideNumber">Ownership guide, upper-cased; server-side only.</param>
public sealed record RosterWeapon(Guid Id, Guid OwnerId, Guid WeaponModelId, string WeaponNumber, string OwnershipGuideNumber)
{
    /// <summary>The type name only: the ownership guide identifies the owner.</summary>
    public override string ToString() => nameof(RosterWeapon);
}

/// <summary>A registered lender found by national ID. Its text form prints no value.</summary>
/// <param name="ArquebusierId">The lender.</param>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name.</param>
/// <param name="NationalId">Normalised DNI or NIE, for the loan's copy.</param>
/// <param name="ComparsaId">The lender's current comparsa.</param>
/// <param name="Weapons">The lender's owned weapons, oldest first, without their ownership guides.</param>
public sealed record LenderSummary(
    Guid ArquebusierId,
    string FirstName,
    string LastName,
    string NationalId,
    Guid ComparsaId,
    IReadOnlyList<LenderWeapon> Weapons)
{
    /// <summary>The type name only: the members are personal data.</summary>
    public override string ToString() => nameof(LenderSummary);
}

/// <summary>
/// A lender's owned weapon as the lookup may show it: no ownership guide (spec: Lender lookup). The loan
/// write reads the guide server-side through <see cref="IArquebusierRoster.FindOwnedWeaponsAsync"/>.
/// </summary>
/// <param name="Id">Owned weapon identifier.</param>
/// <param name="WeaponModelId">Its catalogue model.</param>
/// <param name="WeaponNumber">Number engraved on the stock.</param>
public sealed record LenderWeapon(Guid Id, Guid WeaponModelId, string WeaponNumber);
