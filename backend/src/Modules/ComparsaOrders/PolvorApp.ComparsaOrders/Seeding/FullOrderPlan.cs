using Bogus;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.ComparsaOrders.Seeding;

/// <summary>An order of the full dataset; <see cref="Past"/> picks the closed past edition, otherwise the current one.</summary>
internal sealed record FullOrder(int Number, bool Past, int ComparsaNumber, OrderStatus Status, Guid Chief, string? ReturnReason);

/// <summary>An entry of the full dataset. Numbers are the registry's arquebusier, owned weapon and the catalogue's model numbers.</summary>
internal sealed record FullEntry(
    int Number,
    int Order,
    int Arquebusier,
    ArquebusierStatus Status,
    int PowderKg,
    int CapsBoxes,
    CapsType? Caps,
    WeaponSource Source,
    FlaskOption Flask,
    int? OwnedWeapon,
    int? RentalModel,
    int? LentWeapon);

/// <summary>
/// The orders of the full dataset (realistic-seed-data, design D7; spec: Synthetic order data): for
/// each comparsa the full dataset adds, a <c>VALIDATED</c> order of the past edition holding all its
/// arquebusiers but about one in ten (who are then first-year), and a current order in a fixed mix of
/// statuses — four <c>DRAFT</c>, five <c>SUBMITTED</c>, two <c>RETURNED</c>, three <c>VALIDATED</c> —
/// with two comparsas not prepared. Each order leaves one or two arquebusiers out. Entries follow the
/// person: reserves carry nothing (BR-05); owners of a trabuco or arcabuz use it; the others rent the
/// offered model of their side (BR-07), borrow a team-mate's weapon when theirs is not offered
/// (BR-09), or carry none. A pure function of the population, the offered models and the seed.
/// </summary>
internal static class FullOrderPlan
{
    public const int FirstOrderNumber = 1001;
    public const int FirstEntryNumber = 10001;

    private const int MembershipSeedOffset = 3;
    private const int ValuesSeedOffset = 4;

    /// <summary>The current order of each added comparsa, in comparsa order; null is "not prepared".</summary>
    private static readonly OrderStatus?[] CurrentStatuses =
    [
        OrderStatus.Submitted, OrderStatus.Draft, OrderStatus.Validated, OrderStatus.Returned,
        OrderStatus.Submitted, null, OrderStatus.Draft, OrderStatus.Submitted,
        OrderStatus.Validated, OrderStatus.Submitted, OrderStatus.Returned, OrderStatus.Draft,
        null, OrderStatus.Validated, OrderStatus.Submitted, OrderStatus.Draft,
    ];

    private static readonly string[] ReturnReasons =
    [
        "Revisad los kilos de los arcabuceros nuevos.",
        "Faltan las licencias de dos arcabuceros; completadlas antes de volver a enviarlo.",
    ];

    private static readonly int[] PowderKg = [2, 1, 0];
    private static readonly float[] PowderWeights = [0.6f, 0.3f, 0.1f];

    public static (IReadOnlyList<FullOrder> Orders, IReadOnlyList<FullEntry> Entries) Build(
        IReadOnlyList<SyntheticPerson> population, IReadOnlySet<int> offeredPast, IReadOnlySet<int> offeredCurrent)
    {
        // Two streams: who is in which order (the ids) never depends on the offered models (the values).
        var membership = new Randomizer(SyntheticData.RandomSeed + MembershipSeedOffset);
        var values = new Randomizer(SyntheticData.RandomSeed + ValuesSeedOffset);
        var orders = new List<FullOrder>();
        var entries = new List<FullEntry>();
        var returned = 0;
        if (SyntheticComparsas.Added.Count != CurrentStatuses.Length)
        {
            throw new InvalidOperationException($"The full order plan has {CurrentStatuses.Length} current statuses for {SyntheticComparsas.Added.Count} added comparsas.");
        }

        foreach (var (comparsa, index) in SyntheticComparsas.Added.Select((c, i) => (c, i)))
        {
            var members = population.Where(p => p.ComparsaNumber == comparsa.Number).OrderBy(p => p.Number).ToList();
            var chief = SyntheticPeople.FiringChiefs.Single(c => c.ComparsaNumber == comparsa.Number).Id;

            var past = new FullOrder(FirstOrderNumber + orders.Count, true, comparsa.Number, OrderStatus.Validated, chief, null);
            orders.Add(past);
            foreach (var member in members)
            {
                // About one in ten joined after the past edition: first-year in the current one.
                if (!membership.Bool(0.10f))
                {
                    entries.Add(EntryFor(FirstEntryNumber + entries.Count, past.Number, member, members, offeredPast, values));
                }
            }

            if (CurrentStatuses[index] is not { } status)
            {
                continue;
            }

            var reason = status == OrderStatus.Returned ? ReturnReasons[returned++ % ReturnReasons.Length] : null;
            var current = new FullOrder(FirstOrderNumber + orders.Count, false, comparsa.Number, status, chief, reason);
            orders.Add(current);
            var leftOut = membership.Shuffle(members).Take(membership.Number(1, 2)).Select(m => m.Number).ToHashSet();
            foreach (var member in members.Where(m => !leftOut.Contains(m.Number)))
            {
                entries.Add(EntryFor(FirstEntryNumber + entries.Count, current.Number, member, members, offeredCurrent, values));
            }
        }

        return (orders, entries);
    }

    private static FullEntry EntryFor(int number, int order, SyntheticPerson person, List<SyntheticPerson> team, IReadOnlySet<int> offered, Randomizer random)
    {
        if (person.Reserve)
        {
            return new FullEntry(number, order, person.Number, ArquebusierStatus.Reserve, 0, 0, null, WeaponSource.None, FlaskOption.None, null, null, null);
        }

        var powder = random.WeightedRandom(PowderKg, PowderWeights);
        var christian = SyntheticComparsas.ByNumber(person.ComparsaNumber).Christian;
        var (source, owned, rental, lent, small) = Weapon(person, team, christian, offered, random);
        var boxes = source == WeaponSource.None ? 0 : random.Number(0, 4);
        CapsType? caps = boxes == 0 ? null : small ? CapsType.Small : CapsType.Normal;
        var flask = source == WeaponSource.Owned ? FlaskOption.Owned
            : powder switch { 0 => FlaskOption.None, 1 => FlaskOption.Rental1Kg, _ => FlaskOption.Rental2Kg };
        return new FullEntry(number, order, person.Number, ArquebusierStatus.Active, powder, boxes, caps, source, flask, owned, rental, lent);
    }

    private static (WeaponSource Source, int? Owned, int? Rental, int? Lent, bool Small) Weapon(
        SyntheticPerson person, List<SyntheticPerson> team, bool christian, IReadOnlySet<int> offered, Randomizer random)
    {
        if (person.Weapon is { Kind: not SyntheticWeaponKind.Pistol } own)
        {
            return (WeaponSource.Owned, person.Number, null, null, own.Small);
        }

        // Powder carriers and captains.
        if (random.Bool(0.05f))
        {
            return (WeaponSource.None, null, null, null, false);
        }

        var leftHanded = random.Bool(0.10f);
        var small = random.Bool(0.15f);
        var model = (christian ? 1 : 5) + (leftHanded ? 2 : 0) + (small ? 1 : 0);
        if (offered.Contains(model))
        {
            return (WeaponSource.Rental, null, model, null, small);
        }

        // Not offered for rental: a team-mate lends theirs, if anyone has one.
        var kind = christian ? SyntheticWeaponKind.Trabuco : SyntheticWeaponKind.Arcabuz;
        var lenders = team.Where(t => t.Number != person.Number && t.Weapon?.Kind == kind).ToList();
        if (lenders.Count == 0)
        {
            return (WeaponSource.None, null, null, null, false);
        }

        var lender = random.ArrayElement([.. lenders]);
        return (WeaponSource.Loan, null, null, lender.Number, lender.Weapon!.Small);
    }
}
