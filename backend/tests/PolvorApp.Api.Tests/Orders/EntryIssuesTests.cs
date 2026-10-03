using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>
/// Specs "Submitting an order (UC-14)" and "Entries after registry changes (BR-13, BR-14)": the data
/// problems that block a submission or validation (design D8). An <c>ACTIVE</c> entry without powder or
/// a weapon is valid and has none.
/// </summary>
public sealed class EntryIssuesTests
{
    private static readonly Guid Offered = Guid.CreateVersion7();
    private static readonly HashSet<Guid> OfferedModels = [Offered];

    [Fact]
    public void An_owned_weapon_removed_from_the_registry_blocks_while_the_arquebusier_is_there()
    {
        var entry = Entry(Guid.CreateVersion7());
        entry.WeaponSource = WeaponSource.Owned;

        Assert.Equal([EntryIssues.OwnedWeaponMissing], EntryIssues.Of(entry, loan: null, OfferedModels));
    }

    [Fact]
    public void An_owned_weapon_missing_from_an_entry_no_longer_in_the_registry_does_not_block()
    {
        var entry = Entry(arquebusierId: null);
        entry.WeaponSource = WeaponSource.Owned;

        Assert.Empty(EntryIssues.Of(entry, loan: null, OfferedModels));
    }

    [Fact]
    public void A_loaned_weapon_removed_from_the_registry_blocks()
    {
        var entry = Entry(Guid.CreateVersion7());
        entry.WeaponSource = WeaponSource.Loan;
        var loan = new WeaponLoan { Id = Guid.CreateVersion7(), EntryId = entry.Id, LenderKind = LenderKind.Arquebusier, CopiedAt = DateTimeOffset.UtcNow };

        Assert.Equal([EntryIssues.LoanWeaponMissing], EntryIssues.Of(entry, loan, OfferedModels));
    }

    [Fact]
    public void An_external_loan_never_misses_its_weapon()
    {
        var entry = Entry(Guid.CreateVersion7());
        entry.WeaponSource = WeaponSource.Loan;
        var loan = new WeaponLoan { Id = Guid.CreateVersion7(), EntryId = entry.Id, LenderKind = LenderKind.External, CopiedAt = DateTimeOffset.UtcNow };

        Assert.Empty(EntryIssues.Of(entry, loan, OfferedModels));
    }

    [Fact]
    public void A_rental_model_no_longer_offered_blocks()
    {
        var entry = Entry(Guid.CreateVersion7());
        (entry.WeaponSource, entry.RentalWeaponModelId) = (WeaponSource.Rental, Guid.CreateVersion7());

        Assert.Equal([EntryIssues.RentalModelNotOffered], EntryIssues.Of(entry, loan: null, OfferedModels));
    }

    [Fact]
    public void An_offered_rental_has_no_issue()
    {
        var entry = Entry(Guid.CreateVersion7());
        (entry.WeaponSource, entry.RentalWeaponModelId) = (WeaponSource.Rental, Offered);

        Assert.Empty(EntryIssues.Of(entry, loan: null, OfferedModels));
    }

    [Fact]
    public void Shooters_without_powder_and_carriers_without_a_weapon_have_no_issue()
    {
        var shooter = Entry(Guid.CreateVersion7());
        (shooter.WeaponSource, shooter.OwnedWeaponId) = (WeaponSource.Owned, Guid.CreateVersion7());
        var carrier = Entry(Guid.CreateVersion7());
        carrier.PowderKg = 2;

        Assert.Empty(EntryIssues.Of(shooter, loan: null, OfferedModels));
        Assert.Empty(EntryIssues.Of(carrier, loan: null, OfferedModels));
    }

    private static EditionEntry Entry(Guid? arquebusierId) =>
        NewEntry(NewOrder(NewEdition(2031), Guid.CreateVersion7()), arquebusierId, ArquebusierStatus.Active);
}
