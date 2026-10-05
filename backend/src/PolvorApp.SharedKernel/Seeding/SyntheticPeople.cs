using System.Globalization;
using System.Text;
using Bogus;

namespace PolvorApp.SharedKernel.Seeding;

/// <summary>The license kinds the seed uses; the registry maps them to its own codes.</summary>
public enum SyntheticLicenseType
{
    Ae,
    AProf,
}

/// <summary>A seeded license: none (no type), pending (a type, no dates) or issued (both dates).</summary>
public sealed record SyntheticLicense(SyntheticLicenseType? Type, bool Pending, DateOnly? IssuedOn, DateOnly? ExpiresOn)
{
    public static readonly SyntheticLicense None = new(null, false, null, null);

    public static readonly SyntheticLicense PendingAe = new(SyntheticLicenseType.Ae, true, null, null);
}

public enum SyntheticWeaponKind
{
    Trabuco,
    Arcabuz,
    Pistol,
}

/// <summary>A seeded owned weapon; a pistol has no handedness or size.</summary>
public sealed record SyntheticOwnedWeapon(SyntheticWeaponKind Kind, bool LeftHanded, bool Small, string WeaponNumber, string Guide);

public enum SyntheticLicensePhotos
{
    None,
    FrontOnly,
    Both,
}

/// <summary>A person of the full dataset's population (realistic-seed-data, design D3).</summary>
/// <param name="Number">From 1001, in comparsa order; the last part of the arquebusier's fixed id.</param>
public sealed record SyntheticPerson(
    int Number,
    int ComparsaNumber,
    string FirstName,
    string LastName,
    SyntheticGender Gender,
    DateOnly BirthDate,
    string NationalId,
    string? Email,
    string? Phone,
    bool Reserve,
    DateOnly? CourseCompletedOn,
    SyntheticLicense License,
    SyntheticOwnedWeapon? Weapon,
    bool IdPhoto,
    SyntheticLicensePhotos LicensePhotos)
{
    /// <summary>The arquebusier's fixed id, as the registry seeder builds it.</summary>
    public Guid Id => SyntheticPeople.ArquebusierId(Number);
}

/// <summary>The FiringChief of one added comparsa (design D3, D8).</summary>
public sealed record SyntheticFiringChief(Guid Id, int ComparsaNumber, string Name, string Email, string Locale);

/// <summary>
/// The full dataset's people (realistic-seed-data, design D3; spec: Synthetic registry data): a pure
/// function of <see cref="SyntheticData.RandomSeed"/> and the seed day, shared by the identity,
/// catalogue, registry and order seeders, which cannot reference one another. Names come from the
/// curated Alicante lists (<see cref="SyntheticNames"/>) with Bogus's <c>es</c> surnames for variety;
/// ages, licenses, course, contact data, weapons and photos are drawn with fixed weights and kept
/// consistent: no minor holds an issued license, no license starts before its holder turned 18,
/// weapons follow the comparsa's side.
/// </summary>
public static class SyntheticPeople
{
    public const int FirstNumber = 1001;
    public const string EmailDomain = "polvorapp.example";

    private const int PeopleSeedOffset = 1;
    private const int SurnameSeedOffset = 11;
    private const int ChiefSeedOffset = 2;

    private static readonly (int Min, int Max)[] AgeBands = [(16, 17), (18, 29), (30, 44), (45, 59), (60, 75)];
    private static readonly float[] AgeBandWeights = [0.05f, 0.25f, 0.30f, 0.28f, 0.12f];
    private static readonly string[] ExcludedSurnames = ["Torregrosa", "Lloret"];

    private enum LicenseRoll
    {
        Valid,
        ValidProf,
        Expiring,
        Expired,
        Pending,
        None,
    }

    private static readonly (LicenseRoll Roll, float Weight)[] LicenseMix =
    [
        (LicenseRoll.Valid, 0.78f), (LicenseRoll.ValidProf, 0.02f), (LicenseRoll.Expiring, 0.06f),
        (LicenseRoll.Expired, 0.06f), (LicenseRoll.Pending, 0.03f), (LicenseRoll.None, 0.05f),
    ];

    private static readonly LicenseRoll[] LicenseRolls = [.. LicenseMix.Select(m => m.Roll)];
    private static readonly float[] LicenseWeights = [.. LicenseMix.Select(m => m.Weight)];

    private static readonly Lock CacheLock = new();
    private static (DateOnly Day, IReadOnlyList<SyntheticPerson> People)? _cache;

    /// <summary>The arquebusier id for <paramref name="number"/>: scenarios 1–99, population from 1001.</summary>
    public static Guid ArquebusierId(int number) => new($"0193a300-0000-7000-8000-{number.ToString("D12", CultureInfo.InvariantCulture)}");

    /// <summary>One FiringChief per added comparsa, in comparsa order, alternating es-ES and ca-ES-valencia.</summary>
    public static IReadOnlyList<SyntheticFiringChief> FiringChiefs { get; } = BuildFiringChiefs();

    /// <summary>
    /// The population on <paramref name="today"/>: 15–40 people in each added comparsa. Several
    /// seeders ask for it in one run, so the last day's list is kept.
    /// </summary>
    public static IReadOnlyList<SyntheticPerson> Population(DateOnly today)
    {
        lock (CacheLock)
        {
            if (_cache is { } cached && cached.Day == today)
            {
                return cached.People;
            }

            var people = Build(today);
            _cache = (today, people);
            return people;
        }
    }

    private static List<SyntheticPerson> Build(DateOnly today)
    {
        var generator = new Generator(today);
        var people = new List<SyntheticPerson>();
        foreach (var comparsa in SyntheticComparsas.Added)
        {
            var size = generator.Random.Number(15, 40);
            for (var i = 0; i < size; i++)
            {
                people.Add(generator.Next(FirstNumber + people.Count, comparsa));
            }
        }

        return people;
    }

    private static List<SyntheticFiringChief> BuildFiringChiefs()
    {
        var random = new Randomizer(SyntheticData.RandomSeed + ChiefSeedOffset);
        var emails = new HashSet<string>(StringComparer.Ordinal);
        return
        [
            .. SyntheticComparsas.Added.Select((comparsa, index) =>
            {
                var gender = random.Bool(0.72f) ? SyntheticGender.Male : SyntheticGender.Female;
                var first = random.ArrayElement(gender == SyntheticGender.Male ? SyntheticNames.MaleFirstNames : SyntheticNames.FemaleFirstNames);
                var first1 = random.ArrayElement(SyntheticNames.ValencianSurnames);
                var second = random.ArrayElement(SyntheticNames.SpanishSurnames);
                // Two digits keep the id's last part at 12 characters.
                ArgumentOutOfRangeException.ThrowIfGreaterThan(comparsa.Number, 99);
                var id = new Guid($"0193a000-0000-7000-8000-0000000001{comparsa.Number.ToString("D2", CultureInfo.InvariantCulture)}");
                return new SyntheticFiringChief(id, comparsa.Number, $"{first} {first1} {second}", UniqueEmail(first, first1, emails), index % 2 == 0 ? "es-ES" : "ca-ES-valencia");
            }),
        ];
    }

    /// <summary><c>nombre.apellido@polvorapp.example</c> from the first given name and <paramref name="surname"/>, folded to ASCII.</summary>
    public static string EmailFor(string firstName, string surname) =>
        $"{Fold(firstName.Split(' ')[0], "persona")}.{Fold(surname, "apellido")}@{EmailDomain}";

    /// <summary><c>nombre.apellido@polvorapp.example</c>, folded to ASCII; a number is appended on clashes.</summary>
    private static string UniqueEmail(string firstName, string surname, HashSet<string> taken)
    {
        var local = $"{Fold(firstName.Split(' ')[0], "persona")}.{Fold(surname, "apellido")}";
        var candidate = local;
        for (var n = 2; !taken.Add(candidate); n++)
        {
            candidate = local + n.ToString(CultureInfo.InvariantCulture);
        }

        return $"{candidate}@{EmailDomain}";
    }

    /// <summary>ASCII letters only, lower-cased; <paramref name="fallback"/> when nothing is left.</summary>
    private static string Fold(string value, string fallback)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.Length == 0 ? fallback : builder.ToString();
    }

    /// <summary>Draws people one by one; every draw happens in a fixed order, so the output is deterministic.</summary>
    private sealed class Generator(DateOnly today)
    {
        private readonly Faker _spanish = new("es") { Random = new Randomizer(SyntheticData.RandomSeed + SurnameSeedOffset) };
        // The FiringChiefs' addresses are taken too, so no person shares one with a user.
        private readonly HashSet<string> _emails = new(FiringChiefs.Select(c => c.Email.Split('@')[0]), StringComparer.Ordinal);
        private readonly HashSet<string> _weaponNumbers = new(StringComparer.Ordinal);
        private readonly HashSet<string> _guides = new(StringComparer.Ordinal);
        private int _dniIndex;
        private int _nieIndex;

        public Randomizer Random { get; } = new(SyntheticData.RandomSeed + PeopleSeedOffset);

        public SyntheticPerson Next(int number, SyntheticComparsa comparsa)
        {
            var foreign = Random.Bool(0.05f);
            var gender = Random.Bool(0.72f) ? SyntheticGender.Male : SyntheticGender.Female;
            var firstName = Random.ArrayElement(gender == SyntheticGender.Male
                ? (foreign ? SyntheticNames.ForeignMaleFirstNames : SyntheticNames.MaleFirstNames)
                : (foreign ? SyntheticNames.ForeignFemaleFirstNames : SyntheticNames.FemaleFirstNames));
            var lastName = foreign ? Random.ArrayElement(SyntheticNames.ForeignSurnames) : TwoSurnames();
            var nationalId = foreign ? SyntheticNationalIds.PopulationNie(_nieIndex++) : SyntheticNationalIds.PopulationDni(_dniIndex++);

            var band = Random.WeightedRandom(AgeBands, AgeBandWeights);
            var age = Random.Number(band.Min, band.Max);
            var birthDate = today.AddYears(-age).AddDays(-Random.Number(0, 364));
            var reserve = Random.Bool(0.10f);
            DateOnly? course = Random.Bool(0.90f) ? Later(today.AddDays(-Random.Number(30, 3650)), birthDate.AddYears(14)) : null;
            var license = License(age, birthDate);
            // Minors carry the comparsa's weapons, but do not own one.
            var owns = Random.Bool(0.30f);
            var weapon = owns && age >= 18 ? Weapon(comparsa) : null;
            var email = Random.Bool(0.80f) ? UniqueEmail(firstName, foreign ? lastName : lastName.Split(' ')[0], _emails) : null;
            var phone = Random.Bool(0.90f) ? Phone() : null;
            var idPhoto = Random.Bool(0.85f);
            var licensePhotos = license.IssuedOn is null
                ? SyntheticLicensePhotos.None
                : Random.WeightedRandom([SyntheticLicensePhotos.Both, SyntheticLicensePhotos.FrontOnly, SyntheticLicensePhotos.None], [0.90f, 0.03f, 0.07f]);

            return new SyntheticPerson(number, comparsa.Number, firstName, lastName, gender, birthDate, nationalId, email, phone,
                reserve, course, license, weapon, idPhoto, licensePhotos);
        }

        private string TwoSurnames()
        {
            var first = Surname();
            var second = Surname();
            while (second == first)
            {
                second = Surname();
            }

            return $"{first} {second}";
        }

        /// <summary>Half Valencian, the rest Castilian, a few of those from Bogus's <c>es</c> locale.</summary>
        private string Surname()
        {
            if (Random.Bool(0.5f))
            {
                return Random.ArrayElement(SyntheticNames.ValencianSurnames);
            }

            if (Random.Bool(0.15f))
            {
                var bogus = _spanish.Name.LastName();
                if (!ExcludedSurnames.Contains(bogus, StringComparer.OrdinalIgnoreCase) && !bogus.Contains(' ', StringComparison.Ordinal))
                {
                    return bogus;
                }
            }

            return Random.ArrayElement(SyntheticNames.SpanishSurnames);
        }

        /// <summary>Minors hold none or a pending one; an issued license never starts before the 18th birthday.</summary>
        private SyntheticLicense License(int age, DateOnly birthDate)
        {
            if (age < 18)
            {
                return Random.Bool(0.3f) ? SyntheticLicense.PendingAe : SyntheticLicense.None;
            }

            var roll = Random.WeightedRandom(LicenseRolls, LicenseWeights);
            if (roll == LicenseRoll.Pending)
            {
                return SyntheticLicense.PendingAe;
            }

            if (roll == LicenseRoll.None)
            {
                return SyntheticLicense.None;
            }

            var (type, expiresOn) = roll switch
            {
                LicenseRoll.Valid => (SyntheticLicenseType.Ae, today.AddDays(Random.Number(366, 1825))),
                LicenseRoll.ValidProf => (SyntheticLicenseType.AProf, today.AddDays(Random.Number(65, 365))),
                LicenseRoll.Expiring => (SyntheticLicenseType.Ae, today.AddDays(Random.Number(1, 364))),
                _ => (SyntheticLicenseType.Ae, today.AddDays(-Random.Number(1, 1095))),
            };
            var years = type == SyntheticLicenseType.Ae ? 5 : 1;
            var issuedOn = expiresOn.AddYears(-years);
            var eighteen = birthDate.AddYears(18);
            if (issuedOn >= eighteen)
            {
                return new SyntheticLicense(type, false, issuedOn, expiresOn);
            }

            // Too young for that license: a valid one starts on the 18th birthday (still valid), but
            // an expiring or expired one cannot exist yet, so the young adult is still waiting for it.
            return roll is LicenseRoll.Valid or LicenseRoll.ValidProf
                ? new SyntheticLicense(type, false, eighteen, eighteen.AddYears(years))
                : SyntheticLicense.PendingAe;
        }

        private SyntheticOwnedWeapon Weapon(SyntheticComparsa comparsa)
        {
            var pistol = Random.Bool(0.08f);
            var kind = pistol ? SyntheticWeaponKind.Pistol : comparsa.Christian ? SyntheticWeaponKind.Trabuco : SyntheticWeaponKind.Arcabuz;
            var leftHanded = !pistol && Random.Bool(0.10f);
            var small = !pistol && Random.Bool(0.15f);
            return new SyntheticOwnedWeapon(kind, leftHanded, small, Unique(_weaponNumbers, () => Random.Number(10_000, 999_999).ToString(CultureInfo.InvariantCulture)),
                Unique(_guides, () => "GP-" + Random.Number(100_000, 999_999).ToString(CultureInfo.InvariantCulture)));
        }

        private string Phone() =>
            string.Create(CultureInfo.InvariantCulture, $"+34 6{Random.Number(0, 99):D2} {Random.Number(0, 999):D3} {Random.Number(0, 999):D3}");

        private static string Unique(HashSet<string> taken, Func<string> draw)
        {
            var value = draw();
            while (!taken.Add(value))
            {
                value = draw();
            }

            return value;
        }

        private static DateOnly Later(DateOnly a, DateOnly b) => a > b ? a : b;
    }
}
