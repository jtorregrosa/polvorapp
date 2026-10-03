using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Entries;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>Spec "Edition entries (BR-05, BR-07)" and the field rules of "Weapon loans (UC-13, BR-09)" (design D8).</summary>
public sealed class EntryInputTests
{
    private static readonly Guid Weapon = Guid.CreateVersion7();
    private static readonly Guid OfferedModel = Guid.CreateVersion7();
    private static readonly EntryContext Context = new(new HashSet<Guid> { Weapon }, new HashSet<Guid> { OfferedModel });

    private static readonly EntryFields Carrier = new(
        Status: "ACTIVE", PowderKg: 2, CapsBoxes: 0, CapsType: null, WeaponSource: "NONE", OwnedWeaponId: null, RentalWeaponModelId: null, Loan: null, Flask: "RENTAL_2KG");

    private static readonly ExternalLenderFields External = new(
        FirstName: " Prestamista ", LastName: "Externo Sintético", NationalId: "00000101-d", WeaponModelId: OfferedModel, WeaponNumber: "37-17", OwnershipGuideNumber: "ab-123");

    private static Dictionary<string, string> Errors(EntryFields fields) => EntryInput.Read(fields, Context).Errors;

    [Fact]
    public void A_carrier_without_a_weapon_is_valid()
    {
        var (input, errors) = EntryInput.Read(Carrier, Context);

        Assert.Empty(errors);
        Assert.Equal(new EntryValues(ArquebusierStatus.Active, 2, 0, null, WeaponSource.None, null, null, FlaskOption.Rental2Kg), input!.Values);
        Assert.Null(input.Loan);
    }

    [Fact]
    public void A_shooter_without_powder_is_valid()
    {
        var (input, errors) = EntryInput.Read(Carrier with { PowderKg = 0, WeaponSource = "OWNED", OwnedWeaponId = Weapon }, Context);

        Assert.Empty(errors);
        Assert.Equal((0, WeaponSource.Owned, (Guid?)Weapon), (input!.Values.PowderKg, input.Values.WeaponSource, input.Values.OwnedWeaponId));
    }

    [Fact]
    public void Missing_values_are_required() =>
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["status"] = "required",
                ["powderKg"] = "required",
                ["capsBoxes"] = "required",
                ["weaponSource"] = "required",
                ["flask"] = "required",
            },
            Errors(new EntryFields(null, null, null, null, null, null, null, null, null)));

    [Fact]
    public void Unknown_codes_are_invalid() =>
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["status"] = "invalid",
                ["capsType"] = "invalid",
                ["weaponSource"] = "invalid",
                ["flask"] = "invalid",
            },
            Errors(Carrier with { Status = "INACTIVE", CapsBoxes = 1, CapsType = "BIG", WeaponSource = "BORROWED", Flask = "3KG" }));

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Powder_outside_zero_to_two_is_out_of_range(int kg) =>
        Assert.Equal("outOfRange", Errors(Carrier with { PowderKg = kg })["powderKg"]);

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void Caps_outside_zero_to_ninety_nine_are_out_of_range(int boxes) =>
        Assert.Equal("outOfRange", Errors(Carrier with { CapsBoxes = boxes, CapsType = "NORMAL" })["capsBoxes"]);

    [Fact]
    public void Caps_need_their_type() => Assert.Equal("required", Errors(Carrier with { CapsBoxes = 2 })["capsType"]);

    [Fact]
    public void A_caps_type_without_caps_is_invalid() => Assert.Equal("invalid", Errors(Carrier with { CapsType = "SMALL" })["capsType"]);

    [Fact]
    public void A_reserve_entry_has_nothing()
    {
        var errors = Errors(Carrier with { Status = "RESERVE", PowderKg = 1, CapsBoxes = 1, CapsType = "NORMAL", WeaponSource = "OWNED", OwnedWeaponId = Weapon });

        Assert.Equal("reserve", errors["powderKg"]);
        Assert.Equal("reserve", errors["capsBoxes"]);
        Assert.Equal("reserve", errors["weaponSource"]);
        Assert.Equal("reserve", errors["flask"]);
    }

    [Fact]
    public void A_valid_reserve_entry_is_read()
    {
        var (input, errors) = EntryInput.Read(new EntryFields("RESERVE", 0, 0, null, "NONE", null, null, null, "NONE"), Context);

        Assert.Empty(errors);
        Assert.Equal(EntryValues.Empty(ArquebusierStatus.Reserve), input!.Values);
    }

    [Fact]
    public void An_owned_weapon_must_be_the_arquebusiers()
    {
        Assert.Equal("required", Errors(Carrier with { WeaponSource = "OWNED" })["ownedWeaponId"]);
        Assert.Equal("notOwned", Errors(Carrier with { WeaponSource = "OWNED", OwnedWeaponId = Guid.CreateVersion7() })["ownedWeaponId"]);
    }

    [Fact]
    public void A_rental_model_must_be_offered()
    {
        Assert.Equal("required", Errors(Carrier with { WeaponSource = "RENTAL" })["rentalWeaponModelId"]);
        Assert.Equal("notOffered", Errors(Carrier with { WeaponSource = "RENTAL", RentalWeaponModelId = Guid.CreateVersion7() })["rentalWeaponModelId"]);
    }

    [Fact]
    public void Values_of_another_source_are_ignored()
    {
        var (input, errors) = EntryInput.Read(Carrier with { WeaponSource = "RENTAL", RentalWeaponModelId = OfferedModel, OwnedWeaponId = Weapon }, Context);

        Assert.Empty(errors);
        Assert.Null(input!.Values.OwnedWeaponId);
    }

    [Fact]
    public void A_loan_needs_one_lender()
    {
        Assert.Equal("required", Errors(Carrier with { WeaponSource = "LOAN" })["loan"]);
        Assert.Equal("required", Errors(Carrier with { WeaponSource = "LOAN", Loan = new LoanFields(null, null) })["loan"]);
        Assert.Equal("invalid", Errors(Carrier with { WeaponSource = "LOAN", Loan = new LoanFields(Weapon, External) })["loan"]);
    }

    [Fact]
    public void A_registered_lender_names_a_weapon()
    {
        var other = Guid.CreateVersion7();

        var (input, errors) = EntryInput.Read(Carrier with { WeaponSource = "LOAN", Loan = new LoanFields(other, null) }, Context);

        Assert.Empty(errors);
        Assert.Equal(new LoanInput.Registered(other), input!.Loan);
    }

    [Fact]
    public void Lending_the_arquebusiers_own_weapon_is_refused() =>
        Assert.Equal("ownWeapon", Errors(Carrier with { WeaponSource = "LOAN", Loan = new LoanFields(Weapon, null) })["loan.ownedWeaponId"]);

    [Fact]
    public void An_external_owner_is_normalised()
    {
        var (input, errors) = EntryInput.Read(Carrier with { WeaponSource = "LOAN", Loan = new LoanFields(null, External) }, Context);

        Assert.Empty(errors);
        Assert.Equal(new LoanInput.External("Prestamista", "Externo Sintético", "00000101D", OfferedModel, "37-17", "AB-123"), input!.Loan);
    }

    [Fact]
    public void An_external_owner_breaking_the_rules_is_reported_by_field()
    {
        var external = new ExternalLenderFields("  ", new string('a', 101), "12345678A", null, "", new string('9', 31));

        var errors = Errors(Carrier with { WeaponSource = "LOAN", Loan = new LoanFields(null, external) });

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["loan.firstName"] = "required",
                ["loan.lastName"] = "tooLong",
                ["loan.nationalId"] = "checkLetter",
                ["loan.weaponModelId"] = "required",
                ["loan.weaponNumber"] = "required",
                ["loan.ownershipGuideNumber"] = "tooLong",
            },
            errors);
    }

    [Fact]
    public void A_loan_is_ignored_for_another_source()
    {
        var (input, errors) = EntryInput.Read(Carrier with { Loan = new LoanFields(Guid.CreateVersion7(), null) }, Context);

        Assert.Empty(errors);
        Assert.Null(input!.Loan);
    }

    [Fact]
    public void An_entry_of_an_arquebusier_no_longer_in_the_registry_cannot_name_an_owned_weapon()
    {
        var gone = new EntryContext(OwnedWeaponIds: null, new HashSet<Guid> { OfferedModel });

        var (_, errors) = EntryInput.Read(Carrier with { WeaponSource = "OWNED", OwnedWeaponId = Weapon }, gone);

        Assert.Equal("notOwned", errors["ownedWeaponId"]);
    }
}
