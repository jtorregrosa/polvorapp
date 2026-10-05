using System.Text.RegularExpressions;
using PolvorApp.SharedKernel.Seeding;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.Api.Tests.SharedKernel;

/// <summary>
/// The full dataset's people (realistic-seed-data, design D3; spec: Synthetic registry data): believable,
/// consistent and deterministic.
/// </summary>
public sealed partial class SyntheticPeopleTests
{
    private static readonly DateOnly Today = new(2026, 10, 4);

    private static readonly IReadOnlyList<SyntheticPerson> People = SyntheticPeople.Population(Today);

    [Fact]
    public void The_same_day_gives_the_same_people()
    {
        Assert.Equal(People, SyntheticPeople.Population(Today));
    }

    [Fact]
    public void Each_added_comparsa_has_15_to_40_people_numbered_from_1001()
    {
        var added = SyntheticComparsas.Added.Select(c => c.Number).ToHashSet();

        Assert.All(People, p => Assert.Contains(p.ComparsaNumber, added));
        Assert.All(People.GroupBy(p => p.ComparsaNumber), group => Assert.InRange(group.Count(), 15, 40));
        Assert.Equal(16, People.Select(p => p.ComparsaNumber).Distinct().Count());
        Assert.Equal(Enumerable.Range(1001, People.Count), People.Select(p => p.Number));
    }

    [Fact]
    public void Names_are_realistic_and_never_synthetic_or_the_maintainers()
    {
        Assert.All(People, p =>
        {
            Assert.DoesNotContain("Sintétic", p.FirstName + p.LastName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(p.FirstName.Trim(), p.FirstName);
            Assert.Equal(p.LastName.Trim(), p.LastName);
            Assert.DoesNotContain("Torregrosa", p.LastName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Lloret", p.LastName, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(p.FirstName.Length + p.LastName.Length, 4, 100);
        });

        // Spanish nationals (DNI) have two surnames; foreign residents (NIE) may have one.
        Assert.All(People.Where(p => !p.NationalId.StartsWith('Z')), p => Assert.True(p.LastName.Split(' ').Length >= 2, p.LastName));
        Assert.Contains(People, p => p.NationalId.StartsWith('Z'));
    }

    [Fact]
    public void The_gender_matches_the_first_name()
    {
        Assert.All(People, p => Assert.Contains(p.FirstName, SyntheticNames.FirstNamesOf(p.Gender)));
        var men = People.Count(p => p.Gender == SyntheticGender.Male) / (double)People.Count;
        Assert.InRange(men, 0.6, 0.85);
    }

    [Fact]
    public void Ages_range_from_16_to_75_with_some_minors()
    {
        var ages = People.Select(p => AgeOn(p.BirthDate)).ToList();

        Assert.All(ages, age => Assert.InRange(age, 16, 75));
        Assert.Contains(ages, age => age < 18);
        Assert.Contains(ages, age => age >= 60);
    }

    [Fact]
    public void Licenses_are_consistent_with_the_age()
    {
        Assert.All(People, p =>
        {
            var license = p.License;
            if (AgeOn(p.BirthDate) < 18)
            {
                Assert.Null(license.IssuedOn);
            }

            if (license.Type is null)
            {
                Assert.False(license.Pending);
                Assert.Null(license.IssuedOn);
                return;
            }

            if (license.Pending)
            {
                Assert.Null(license.IssuedOn);
                Assert.Null(license.ExpiresOn);
                return;
            }

            Assert.NotNull(license.IssuedOn);
            Assert.True(license.IssuedOn >= p.BirthDate.AddYears(18), $"{p.Number} licensed before 18");
            Assert.True(license.IssuedOn <= Today);
            var years = license.Type == SyntheticLicenseType.Ae ? 5 : 1;
            Assert.Equal(license.IssuedOn.Value.AddYears(years), license.ExpiresOn);
        });
    }

    [Fact]
    public void Most_are_active_with_a_valid_license_and_the_course_and_a_minority_shows_each_warning()
    {
        var valid = People.Count(p => p.License.ExpiresOn > Today.AddYears(1));
        Assert.True(valid > People.Count * 0.6, $"{valid} valid of {People.Count}");
        Assert.True(People.Count(p => !p.Reserve) > People.Count * 0.8);
        Assert.True(People.Count(p => p.CourseCompletedOn is not null) > People.Count * 0.8);

        Assert.Contains(People, p => p.License.ExpiresOn is { } e && e > Today && e <= Today.AddYears(1));
        Assert.Contains(People, p => p.License.ExpiresOn < Today);
        Assert.Contains(People, p => p.License.Pending);
        Assert.Contains(People, p => p.License.Type is null);
        Assert.Contains(People, p => p.License.Type == SyntheticLicenseType.AProf);
        Assert.Contains(People, p => p.CourseCompletedOn is null);
        Assert.Contains(People, p => p.Reserve);
        Assert.All(People.Where(p => p.CourseCompletedOn is { } c), p => Assert.True(p.CourseCompletedOn <= Today && p.CourseCompletedOn >= p.BirthDate.AddYears(14)));
    }

    [Fact]
    public void National_ids_are_valid_unique_and_from_the_seeded_ranges()
    {
        Assert.Equal(People.Count, People.Select(p => p.NationalId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(People, p =>
        {
            Assert.Equal(p.NationalId, NationalId.Parse(p.NationalId).Value);
            Assert.True(p.NationalId.StartsWith("99", StringComparison.Ordinal) || p.NationalId.StartsWith("Z9", StringComparison.Ordinal), p.NationalId);
        });
    }

    [Fact]
    public void Emails_and_phones_follow_the_registry_rules()
    {
        var emails = People.Where(p => p.Email is not null).Select(p => p.Email!).ToList();
        Assert.InRange(emails.Count, People.Count * 0.6, People.Count * 0.95);
        Assert.Equal(emails.Count, emails.Distinct(StringComparer.Ordinal).Count());
        Assert.All(emails, email =>
        {
            Assert.True(InputFields.IsPlainEmail(email), email);
            Assert.Equal(email.ToLowerInvariant(), email);
            Assert.EndsWith("@polvorapp.example", email, StringComparison.Ordinal);
            Assert.True(email.Length <= 254);
        });

        var phones = People.Where(p => p.Phone is not null).Select(p => p.Phone!).ToList();
        Assert.InRange(phones.Count, People.Count * 0.7, People.Count);
        Assert.All(phones, phone => Assert.Matches(SpanishMobile(), phone));
    }

    [Fact]
    public void An_email_is_derived_from_the_persons_name()
    {
        var person = People.First(p => p.Email is not null);
        var first = Fold(person.FirstName.Split(' ')[0]);

        Assert.StartsWith(first, person.Email!, StringComparison.Ordinal);
    }

    [Fact]
    public void Owned_weapons_match_the_comparsas_side_and_are_unique()
    {
        var owners = People.Where(p => p.Weapon is not null).ToList();
        Assert.InRange(owners.Count, People.Count * 0.15, People.Count * 0.45);
        Assert.Contains(owners, p => p.Weapon!.Kind == SyntheticWeaponKind.Pistol);
        Assert.Contains(owners, p => p.Weapon!.LeftHanded);
        Assert.Contains(owners, p => p.Weapon!.Small);
        Assert.All(owners, p =>
        {
            var christian = SyntheticComparsas.ByNumber(p.ComparsaNumber).Christian;
            var expected = christian ? SyntheticWeaponKind.Trabuco : SyntheticWeaponKind.Arcabuz;
            Assert.True(p.Weapon!.Kind == expected || p.Weapon.Kind == SyntheticWeaponKind.Pistol);
            Assert.Matches("^GP-[0-9]{6}$", p.Weapon.Guide);
        });
        Assert.Equal(owners.Count, owners.Select(p => p.Weapon!.Guide).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(owners.Count, owners.Select(p => p.Weapon!.WeaponNumber).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Most_have_an_id_photo_and_most_licensed_have_both_license_photos()
    {
        Assert.InRange(People.Count(p => p.IdPhoto), People.Count * 0.7, People.Count * 0.95);

        var issued = People.Where(p => p.License.IssuedOn is not null).ToList();
        Assert.True(issued.Count(p => p.LicensePhotos == SyntheticLicensePhotos.Both) > issued.Count * 0.75);
        Assert.Contains(issued, p => p.LicensePhotos == SyntheticLicensePhotos.FrontOnly);
        Assert.All(People.Where(p => p.License.IssuedOn is null), p => Assert.Equal(SyntheticLicensePhotos.None, p.LicensePhotos));
    }

    [Fact]
    public void Each_added_comparsa_has_one_active_firing_chief_with_a_realistic_name()
    {
        var chiefs = SyntheticPeople.FiringChiefs;

        Assert.Equal(SyntheticComparsas.Added.Select(c => c.Number), chiefs.Select(c => c.ComparsaNumber));
        Assert.Equal(chiefs.Count, chiefs.Select(c => c.Email).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(chiefs.Count, chiefs.Select(c => c.Id).Distinct().Count());
        Assert.All(chiefs, c =>
        {
            Assert.True(InputFields.IsPlainEmail(c.Email));
            Assert.EndsWith("@polvorapp.example", c.Email, StringComparison.Ordinal);
            Assert.True(c.Locale is "es-ES" or "ca-ES-valencia", c.Locale);
            Assert.True(c.Name.Split(' ').Length >= 3, c.Name);
            Assert.StartsWith("0193a000-0000-7000-8000-0000000001", c.Id.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Comparsas_have_invented_names_ten_per_side_with_their_scenario_roles()
    {
        Assert.Equal(["Cruzados", "Abencerrajes", "Hospitalarios", "Zegríes"], SyntheticComparsas.Scenario.Select(c => c.Name));
        Assert.Equal(16, SyntheticComparsas.Added.Count);
        Assert.Equal(10, SyntheticComparsas.All.Count(c => c.Christian));
        Assert.Equal(SyntheticComparsas.All.Count, SyntheticComparsas.All.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(["Zegríes"], SyntheticComparsas.All.Where(c => !c.Active).Select(c => c.Name));
        Assert.Equal(["Abencerrajes"], SyntheticComparsas.All.Where(c => !c.HasLogo).Select(c => c.Name));
        Assert.Equal(new Guid("0193a100-0000-7000-8000-000000000001"), SyntheticComparsas.ByNumber(1).Id);
        Assert.Equal(new Guid("0193a100-0000-7000-8000-000000000020"), SyntheticComparsas.ByNumber(20).Id);

        // None is a comparsa of San Vicente del Raspeig (maintainer decision 2026-10-04).
        string[] real = ["Cristianos", "Contrabandistas", "Almogavers", "Maseros", "Nómadas", "Templarios", "Estudiantes", "Visigodos", "Astures", "Navarros",
            "Negros Zulúes", "Moros Viejos", "Moros Nuevos", "Pacos", "Tuaregs", "Marrocs", "Abasires", "Almorávides", "Benimerines", "Caballo Loco"];
        Assert.Empty(SyntheticComparsas.All.Select(c => c.Name).Intersect(real, StringComparer.OrdinalIgnoreCase));
    }

    private static int AgeOn(DateOnly birth)
    {
        var age = Today.Year - birth.Year;
        return birth.AddYears(age) > Today ? age - 1 : age;
    }

    private static string Fold(string value) => new(value.Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
        .Where(char.IsLetter)
        .Select(char.ToLowerInvariant)
        .ToArray());

    [GeneratedRegex(@"^\+34 [67][0-9]{2} [0-9]{3} [0-9]{3}$")]
    private static partial Regex SpanishMobile();
}
