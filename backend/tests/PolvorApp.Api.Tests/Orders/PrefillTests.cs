using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Entries;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>Spec "Pre-fill from the previous edition (UC-12, BR-11)" (design D8).</summary>
public sealed class PrefillTests
{
    private static readonly Guid Weapon = Guid.CreateVersion7();
    private static readonly Guid OtherWeapon = Guid.CreateVersion7();
    private static readonly Guid OfferedModel = Guid.CreateVersion7();
    private static readonly Guid RetiredModel = Guid.CreateVersion7();
    private static readonly HashSet<Guid> Offered = [OfferedModel];

    private static readonly EntryValues ActiveRental = new(
        ArquebusierStatus.Active, PowderKg: 2, CapsBoxes: 3, CapsType.Normal, WeaponSource.Rental, OwnedWeaponId: null, RentalWeaponModelId: OfferedModel, FlaskOption.Rental2Kg);

    [Fact]
    public void An_active_previous_entry_is_copied()
    {
        var values = Prefill.For(ArquebusierStatus.Active, [], ActiveRental, Offered);

        Assert.Equal(ActiveRental, values);
    }

    [Fact]
    public void An_owned_weapon_still_owned_is_kept()
    {
        var previous = ActiveRental with { WeaponSource = WeaponSource.Owned, OwnedWeaponId = Weapon, RentalWeaponModelId = null };

        var values = Prefill.For(ArquebusierStatus.Active, [Weapon, OtherWeapon], previous, Offered);

        Assert.Equal((WeaponSource.Owned, (Guid?)Weapon), (values.WeaponSource, values.OwnedWeaponId));
    }

    [Fact]
    public void An_owned_weapon_no_longer_owned_becomes_none()
    {
        var previous = ActiveRental with { WeaponSource = WeaponSource.Owned, OwnedWeaponId = Weapon, RentalWeaponModelId = null };

        var values = Prefill.For(ArquebusierStatus.Active, [OtherWeapon], previous, Offered);

        Assert.Equal((WeaponSource.None, (Guid?)null, 2, FlaskOption.Rental2Kg), (values.WeaponSource, values.OwnedWeaponId, values.PowderKg, values.Flask));
    }

    [Fact]
    public void A_rental_model_no_longer_offered_becomes_none()
    {
        var values = Prefill.For(ArquebusierStatus.Active, [], ActiveRental with { RentalWeaponModelId = RetiredModel }, Offered);

        Assert.Equal((WeaponSource.None, (Guid?)null), (values.WeaponSource, values.RentalWeaponModelId));
    }

    [Fact]
    public void Loans_are_never_copied()
    {
        var previous = ActiveRental with { WeaponSource = WeaponSource.Loan, RentalWeaponModelId = null };

        var values = Prefill.For(ArquebusierStatus.Active, [Weapon], previous, Offered);

        Assert.Equal(WeaponSource.None, values.WeaponSource);
    }

    [Fact]
    public void A_reserve_previous_entry_starts_from_the_defaults()
    {
        var values = Prefill.For(ArquebusierStatus.Active, [Weapon], EntryValues.Empty(ArquebusierStatus.Reserve), Offered);

        Assert.Equal(EntryValues.Empty(ArquebusierStatus.Active) with { WeaponSource = WeaponSource.Owned, OwnedWeaponId = Weapon }, values);
    }

    [Fact]
    public void A_first_entry_of_an_owner_of_one_weapon_uses_it()
    {
        var values = Prefill.For(ArquebusierStatus.Active, [Weapon], previous: null, Offered);

        Assert.Equal(
            new EntryValues(ArquebusierStatus.Active, 0, 0, null, WeaponSource.Owned, Weapon, null, FlaskOption.None),
            values);
    }

    [Fact]
    public void A_first_entry_of_an_owner_of_several_weapons_has_none()
    {
        var values = Prefill.For(ArquebusierStatus.Active, [Weapon, OtherWeapon], previous: null, Offered);

        Assert.Equal(EntryValues.Empty(ArquebusierStatus.Active), values);
    }

    [Fact]
    public void A_reserve_in_the_registry_has_nothing_whatever_came_before()
    {
        var values = Prefill.For(ArquebusierStatus.Reserve, [Weapon], ActiveRental, Offered);

        Assert.Equal(EntryValues.Empty(ArquebusierStatus.Reserve), values);
    }

    [Fact]
    public void Nothing_is_carried_over_from_an_empty_history()
    {
        var values = Prefill.For(ArquebusierStatus.Active, [], previous: null, Offered);

        Assert.Equal(new EntryValues(ArquebusierStatus.Active, 0, 0, null, WeaponSource.None, null, null, FlaskOption.None), values);
    }
}
