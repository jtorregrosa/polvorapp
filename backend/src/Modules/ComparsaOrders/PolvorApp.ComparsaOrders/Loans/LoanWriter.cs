using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ComparsaOrders.Loans;

/// <summary>
/// Keeps the loan of an entry in line with its weapon source (spec: Weapon loans (UC-13, BR-09); design
/// D9), inside the entry write's transaction: no loan unless the source is <c>LOAN</c>, a replaced loan
/// is deleted with its external owner, and a registered owner's loan keeps a copy of the lender and the
/// weapon (design D3). The checks that need other records are made here.
/// </summary>
internal sealed class LoanWriter(ComparsaOrdersDbContext db, IArquebusierRoster roster, ICatalogDirectory catalog)
{
    public const string NotFound = "notFound";
    public const string Inactive = "inactive";
    public const string LenderRegistered = "lenderRegistered";

    /// <summary>Whether the entry has a loan now.</summary>
    public Task<bool> HasLoanAsync(Guid entryId, CancellationToken cancellationToken) =>
        db.Loans.AnyAsync(l => l.EntryId == entryId, cancellationToken);

    /// <summary>
    /// Applies <paramref name="loan"/> to <paramref name="entry"/>, whose new values are already set.
    /// Returns the field errors, or whether the loan changed.
    /// </summary>
    public async Task<(IReadOnlyDictionary<string, string>? Errors, bool Changed)> ApplyAsync(
        EditionEntry entry, LoanInput? loan, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var existing = await db.Loans.SingleOrDefaultAsync(l => l.EntryId == entry.Id, cancellationToken);
        if (entry.WeaponSource != WeaponSource.Loan)
        {
            if (existing is not null)
            {
                db.Loans.Remove(existing);
            }

            return (null, existing is not null);
        }

        if (loan is null)
        {
            throw new InvalidOperationException($"Entry {entry.Id} has the weapon source LOAN but no loan input.");
        }

        return loan switch
        {
            LoanInput.Keep => await KeepAsync(existing, now, cancellationToken),
            LoanInput.Registered registered => await RegisteredAsync(entry, existing, registered, now, cancellationToken),
            LoanInput.External external => await ExternalAsync(entry, existing, external, now, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown loan shape {loan.GetType().Name}."),
        };
    }

    /// <summary>The current loan, unchanged; a registered owner's copy is refreshed while the weapon exists.</summary>
    private async Task<(IReadOnlyDictionary<string, string>?, bool)> KeepAsync(WeaponLoan? existing, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (existing is null)
        {
            return (Error("loan", InputFields.Required), false);
        }

        await RefreshAsync(existing, now, cancellationToken);
        return (null, false);
    }

    /// <summary>Refreshes a registered owner's copy from the registry while the weapon is in it (design D3); an external owner's stays as typed.</summary>
    public async Task RefreshAsync(WeaponLoan loan, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loan);
        if (loan.LenderOwnedWeaponId is not { } weaponId)
        {
            return;
        }

        var (weapon, lender) = await LenderOfAsync(weaponId, cancellationToken);
        if (weapon is not null && lender is not null)
        {
            Copy(loan, weapon, lender, now);
        }
    }

    /// <summary>
    /// <see cref="RefreshAsync"/> for many loans with two registry reads in all, for moves that hold the
    /// order lock (design D8). A weapon that left the registry keeps its loan's copy, as in
    /// <see cref="RefreshAsync"/>: its key to the registry is being nulled.
    /// </summary>
    public async Task RefreshManyAsync(IReadOnlyCollection<WeaponLoan> loans, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loans);
        Guid[] weaponIds = [.. loans.Select(l => l.LenderOwnedWeaponId).OfType<Guid>().Distinct()];
        if (weaponIds.Length == 0)
        {
            return;
        }

        var weapons = (await roster.FindOwnedWeaponsAsync(weaponIds, cancellationToken)).ToDictionary(w => w.Id);
        Guid[] ownerIds = [.. weapons.Values.Select(w => w.OwnerId).Distinct()];
        var lenders = ownerIds.Length == 0 ? [] : (await roster.FindManyAsync(ownerIds, cancellationToken)).ToDictionary(a => a.Id);
        foreach (var loan in loans)
        {
            if (loan.LenderOwnedWeaponId is { } weaponId && weapons.TryGetValue(weaponId, out var weapon)
                && lenders.TryGetValue(weapon.OwnerId, out var lender))
            {
                Copy(loan, weapon, lender, now);
            }
        }
    }

    private async Task<(IReadOnlyDictionary<string, string>?, bool)> RegisteredAsync(
        EditionEntry entry, WeaponLoan? existing, LoanInput.Registered registered, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Unscoped by design: the weapon id normally comes from the audited lender lookup (an id cannot
        // be guessed: UUIDv7). Only its owner's name, comparsa and the weapon reach the loan, and the
        // responses never show a registered owner's DNI or guide.
        var (weapon, lender) = await LenderOfAsync(registered.OwnedWeaponId, cancellationToken);
        if (weapon is null || lender is null)
        {
            return (Error("loan.ownedWeaponId", NotFound), false);
        }

        if (existing is { LenderKind: LenderKind.Arquebusier } && existing.LenderOwnedWeaponId == weapon.Id)
        {
            Copy(existing, weapon, lender, now);
            return (null, false);
        }

        Replace(existing);
        var loan = NewLoan(entry, LenderKind.Arquebusier, now);
        Copy(loan, weapon, lender, now);
        db.Loans.Add(loan);
        return (null, true);
    }

    private async Task<(IReadOnlyDictionary<string, string>?, bool)> ExternalAsync(
        EditionEntry entry, WeaponLoan? existing, LoanInput.External external, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The same external owner, sent again with an unrelated edit: kept as it is, without re-checking
        // the registry or the catalogue, which may have changed since (an owner who joined later).
        if (existing is { LenderKind: LenderKind.External } && Same(existing, external))
        {
            return (null, false);
        }

        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (await roster.IsNationalIdRegisteredAsync(external.NationalId, cancellationToken))
        {
            errors["loan.nationalId"] = LenderRegistered;
        }

        switch (await catalog.FindWeaponModelAsync(external.WeaponModelId, cancellationToken))
        {
            case null:
                errors["loan.weaponModelId"] = NotFound;
                break;
            case { Active: false }:
                errors["loan.weaponModelId"] = Inactive;
                break;
        }

        if (errors.Count > 0)
        {
            return (errors, false);
        }

        Replace(existing);
        var loan = NewLoan(entry, LenderKind.External, now);
        (loan.LenderFirstName, loan.LenderLastName, loan.LenderNationalId) = (external.FirstName, external.LastName, external.NationalId);
        (loan.WeaponModelId, loan.WeaponNumber, loan.OwnershipGuideNumber) = (external.WeaponModelId, external.WeaponNumber, external.OwnershipGuideNumber);
        db.Loans.Add(loan);
        return (null, true);
    }

    /// <summary>The owned weapon and its owner, or nulls when the weapon is not in the registry.</summary>
    private async Task<(RosterWeapon? Weapon, RosterArquebusier? Lender)> LenderOfAsync(Guid weaponId, CancellationToken cancellationToken)
    {
        if ((await roster.FindOwnedWeaponsAsync([weaponId], cancellationToken)).SingleOrDefault() is not { } weapon)
        {
            return (null, null);
        }

        return (weapon, (await roster.FindManyAsync([weapon.OwnerId], cancellationToken)).SingleOrDefault());
    }

    private void Replace(WeaponLoan? existing)
    {
        if (existing is not null)
        {
            // The old row goes first, so the unique entry key admits the new one in the same save.
            db.Loans.Remove(existing);
        }
    }

    private static WeaponLoan NewLoan(EditionEntry entry, LenderKind kind, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), EntryId = entry.Id, LenderKind = kind, CopiedAt = now };

    /// <summary>Copies the lender and the weapon; <c>CopiedAt</c> moves only when a copied value changes.</summary>
    private static void Copy(WeaponLoan loan, RosterWeapon weapon, RosterArquebusier lender, DateTimeOffset now)
    {
        var before = Copied(loan);
        (loan.LenderOwnedWeaponId, loan.LenderFirstName, loan.LenderLastName, loan.LenderNationalId, loan.LenderComparsaId) =
            (weapon.Id, lender.FirstName, lender.LastName, lender.NationalId, lender.ComparsaId);
        (loan.WeaponModelId, loan.WeaponNumber, loan.OwnershipGuideNumber) =
            (weapon.WeaponModelId, weapon.WeaponNumber, weapon.OwnershipGuideNumber);
        if (Copied(loan) != before)
        {
            loan.CopiedAt = now;
        }
    }

    private static (Guid?, string?, string?, string?, Guid?, Guid?, string?, string?) Copied(WeaponLoan loan) =>
        (loan.LenderOwnedWeaponId, loan.LenderFirstName, loan.LenderLastName, loan.LenderNationalId, loan.LenderComparsaId,
         loan.WeaponModelId, loan.WeaponNumber, loan.OwnershipGuideNumber);

    private static bool Same(WeaponLoan loan, LoanInput.External external) =>
        (loan.LenderFirstName, loan.LenderLastName, loan.LenderNationalId, loan.WeaponModelId, loan.WeaponNumber, loan.OwnershipGuideNumber)
        == (external.FirstName, external.LastName, external.NationalId, external.WeaponModelId, external.WeaponNumber, external.OwnershipGuideNumber);

    private static Dictionary<string, string> Error(string field, string reason) => new(StringComparer.Ordinal) { [field] = reason };
}
