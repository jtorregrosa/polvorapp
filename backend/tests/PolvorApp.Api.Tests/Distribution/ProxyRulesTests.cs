using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Proxies;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Spec "Pickup proxies (UC-19, BR-06)" and "Proxies that no longer hold" (design D5): the rules a proxy
/// is checked against when registered, and the problems it shows when the data changed afterwards.
/// </summary>
public sealed class ProxyRulesTests
{
    private static readonly Guid Edition = Guid.CreateVersion7();
    private static readonly Guid Norte = Guid.CreateVersion7();
    private static readonly Guid Order = Guid.CreateVersion7();
    private static readonly DateOnly FestivalStart = new(2031, 4, 22);

    [Theory]
    [InlineData(DistributionType.Powder, 2, WeaponSource.None, true)]
    [InlineData(DistributionType.Powder, 0, WeaponSource.Rental, false)]
    [InlineData(DistributionType.Weapons, 0, WeaponSource.Rental, true)]
    [InlineData(DistributionType.Weapons, 2, WeaponSource.Owned, false)]
    [InlineData(DistributionType.Weapons, 2, WeaponSource.Loan, false)]
    public void A_holder_collects_powder_or_a_rented_weapon(DistributionType type, int powderKg, WeaponSource source, bool collects) =>
        Assert.Equal(collects, ProxyRules.HasSomethingToCollect(Entry(powderKg: powderKg, source: source), type));

    [Fact]
    public void A_reserve_holder_has_nothing_to_collect() =>
        Assert.False(ProxyRules.HasSomethingToCollect(Entry(active: false, powderKg: 0), DistributionType.Powder));

    [Fact]
    public void The_reference_date_is_the_day_of_that_type_or_the_festivals_first_day()
    {
        var days = new Dictionary<DistributionType, DateOnly> { [DistributionType.Powder] = new(2031, 4, 18) };

        Assert.Equal(new DateOnly(2031, 4, 18), ProxyRules.ReferenceDate(DistributionType.Powder, days, FestivalStart));
        Assert.Equal(FestivalStart, ProxyRules.ReferenceDate(DistributionType.Weapons, days, FestivalStart));
    }

    [Theory]
    [InlineData("2031-04-18", true)]
    [InlineData("2031-06-30", true)]
    [InlineData("2031-04-17", false)]
    public void An_issued_license_of_any_type_holds_through_the_reference_date(string expiresOn, bool holds)
    {
        var expiry = DateOnly.Parse(expiresOn, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(holds, ProxyRules.LicenseHolds(new ArquebusierLicenseFacts.Issued(LicenseType.Ae, expiry, true, true), new DateOnly(2031, 4, 18)));
        Assert.Equal(holds, ProxyRules.LicenseHolds(new ArquebusierLicenseFacts.Issued(LicenseType.AProf, expiry, false, false), new DateOnly(2031, 4, 18)));
    }

    [Fact]
    public void A_pending_or_missing_license_never_holds()
    {
        Assert.False(ProxyRules.LicenseHolds(new ArquebusierLicenseFacts.Pending(LicenseType.Ae), new DateOnly(2031, 4, 18)));
        Assert.False(ProxyRules.LicenseHolds(null, new DateOnly(2031, 4, 18)));
    }

    [Fact]
    public void Registration_checks_the_order_the_holder_the_collection_and_the_license_in_that_order()
    {
        var holder = Entry(powderKg: 2);
        var proxy = Entry(active: false, powderKg: 0);
        var valid = new ArquebusierLicenseFacts.Issued(LicenseType.Ae, new DateOnly(2031, 12, 31), true, true);
        var reference = new DateOnly(2031, 4, 18);

        Assert.Null(ProxyRules.CheckRegistration(holder, proxy, DistributionType.Powder, valid, reference));
        Assert.Equal(("proxyEntryId", "notInOrder"), ProxyRules.CheckRegistration(holder, Entry(order: Guid.CreateVersion7()), DistributionType.Powder, valid, reference));
        Assert.Equal(("proxyEntryId", "sameAsHolder"), ProxyRules.CheckRegistration(holder, holder, DistributionType.Powder, valid, reference));
        Assert.Equal(("holderEntryId", "nothingToCollect"), ProxyRules.CheckRegistration(holder, proxy, DistributionType.Weapons, valid, reference));
        Assert.Equal(("proxyEntryId", "licenseInvalid"), ProxyRules.CheckRegistration(holder, proxy, DistributionType.Powder, null, reference));
    }

    [Fact]
    public void A_stored_proxy_shows_a_problem_when_its_data_changed()
    {
        var expired = new ArquebusierLicenseFacts.Issued(LicenseType.Ae, new DateOnly(2031, 4, 10), true, true);
        var valid = new ArquebusierLicenseFacts.Issued(LicenseType.Ae, new DateOnly(2031, 12, 31), true, true);
        var reference = new DateOnly(2031, 4, 18);

        Assert.Null(ProxyRules.ProblemOf(Entry(powderKg: 2), DistributionType.Powder, valid, reference));
        Assert.Equal(ProxyProblem.NotApplicable, ProxyRules.ProblemOf(Entry(powderKg: 0), DistributionType.Powder, valid, reference));
        Assert.Equal(ProxyProblem.LicenseInvalid, ProxyRules.ProblemOf(Entry(powderKg: 2), DistributionType.Powder, expired, reference));
        Assert.Equal(ProxyProblem.LicenseInvalid, ProxyRules.ProblemOf(Entry(powderKg: 2), DistributionType.Powder, null, reference));
    }

    private static EditionEntryFacts Entry(bool active = true, int powderKg = 0, WeaponSource source = WeaponSource.None, Guid? order = null) => new(
        Guid.CreateVersion7(), order ?? Order, Edition, Norte, OrderStatus.Validated, Guid.CreateVersion7(), active, powderKg, source,
        source == WeaponSource.Rental ? Guid.CreateVersion7() : null, FlaskOption.None, new ExportedPerson("Ana", "Abad Sintética", "00000000T", 1));
}
