using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using PolvorApp.Api.Platform.Seeding;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Seeding;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Seeding;
using PolvorApp.SharedKernel.Storage;
using PolvorApp.SharedKernel.Time;
using PolvorApp.SharedKernel.Validation;
using SkiaSharp;
using Arquebusier = PolvorApp.ArquebusierRegistry.Arquebusiers.Arquebusier;
using ArquebusierPhoto = PolvorApp.ArquebusierRegistry.Photos.ArquebusierPhoto;
using Comparsa = PolvorApp.FederationCatalog.Comparsas.Comparsa;
using ComparsaOrder = PolvorApp.ComparsaOrders.Orders.ComparsaOrder;
using EditionEntry = PolvorApp.ComparsaOrders.Entries.EditionEntry;
using OwnedWeapon = PolvorApp.ArquebusierRegistry.OwnedWeapons.OwnedWeapon;
using WeaponLoan = PolvorApp.ComparsaOrders.Loans.WeaponLoan;
using WeaponModel = PolvorApp.FederationCatalog.WeaponModels.WeaponModel;

namespace PolvorApp.Api.Tests.Seeding;

/// <summary>
/// The full dataset (realistic-seed-data, designs D3 and D7; specs: Synthetic registry data — "Full
/// population", "Seeded national ID ranges"; Synthetic order data — "Full dataset orders",
/// "Consistent entries", "First-year arquebusiers"). One full seed for the whole class: it stores
/// about a thousand images, so the assertions share it.
/// </summary>
public sealed class FullDatasetSeedTests(FullDatasetSeedTests.Seeded seeded) : IClassFixture<FullDatasetSeedTests.Seeded>
{
    private static readonly HashSet<Guid> Added = [.. SyntheticComparsas.Added.Select(c => c.Id)];

    [Fact]
    public void The_full_seed_runs_and_reruns_successfully()
    {
        Assert.Equal((0, 0), seeded.ExitCodes);
    }

    [Fact]
    public void Each_added_comparsa_has_15_to_40_arquebusiers()
    {
        var counts = seeded.Arquebusiers.Where(a => Added.Contains(a.ComparsaId)).GroupBy(a => a.ComparsaId).ToList();

        Assert.Equal(16, counts.Count);
        Assert.All(counts, group => Assert.InRange(group.Count(), 15, 40));
        Assert.Equal(PolvorApp.ArquebusierRegistry.Seeding.RegistrySeeder.ArquebusierCount + SyntheticPeople.Population(seeded.Today).Count, seeded.Arquebusiers.Count);
    }

    [Fact]
    public void National_ids_are_valid_unique_and_from_the_seeded_ranges()
    {
        Assert.Equal(seeded.Arquebusiers.Count, seeded.Arquebusiers.Select(a => a.NationalId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(seeded.Arquebusiers, a =>
        {
            Assert.Equal(a.NationalId, NationalId.Parse(a.NationalId).Value);
            var digits = int.Parse(a.NationalId[^8..^1], System.Globalization.CultureInfo.InvariantCulture);
            if (a.NationalId.StartsWith('Z'))
            {
                Assert.InRange(digits, 9_000_000, 9_999_999);
            }
            else
            {
                Assert.InRange(int.Parse(a.NationalId[..8], System.Globalization.CultureInfo.InvariantCulture), 99_000_000, 99_999_999);
            }
        });
    }

    [Fact]
    public void Faces_follow_gender_and_age_band_and_repeat_only_when_a_band_runs_out()
    {
        var images = SyntheticImages.FromBuildOutput();
        var withPhoto = seeded.Photos.Where(p => p.Kind == ArquebusierPhotoKind.Id).Select(p => p.ArquebusierId).ToHashSet();
        var people = seeded.Arquebusiers.Where(a => withPhoto.Contains(a.Id) && a.Gender != Gender.Unspecified).OrderBy(a => a.Id).ToList();
        var requests = people.Select((a, i) => new SyntheticFaceRequest(i, a.Gender == Gender.Male ? SyntheticGender.Male : SyntheticGender.Female, a.BirthDate)).ToList();

        var assignment = images.AssignFaces(requests, seeded.Today);

        Assert.Equal(0, assignment.OutsideBand);
        foreach (var group in requests.GroupBy(r => (r.Gender, Band: SyntheticImages.AgeBandOf(r.BirthDate, seeded.Today))))
        {
            var available = images.Faces.Count(f => f.Gender == group.Key.Gender && f.AgeBand == group.Key.Band);
            var used = group.Select(r => assignment.Faces[r.Key].File).Distinct().Count();
            Assert.All(group, r => Assert.Equal((group.Key.Gender, group.Key.Band), (assignment.Faces[r.Key].Gender, assignment.Faces[r.Key].AgeBand)));
            Assert.Equal(Math.Min(group.Count(), available), used);
        }
    }

    [Fact]
    public void A_stored_license_photo_shows_its_own_holders_data()
    {
        Assert.NotEmpty(seeded.LicenseFronts);
        foreach (var (owner, other, stored) in seeded.LicenseFronts)
        {
            Assert.True(
                Distance(stored, seeded.Render(owner)) < Distance(stored, seeded.Render(other)),
                "a stored license front is closer to another holder's card than to its own");
        }
    }

    [Fact]
    public void Running_the_full_dataset_after_the_scenarios_gives_the_same_data()
    {
        // The second database was seeded with the scenarios first, then with the full dataset.
        Assert.Equal(seeded.Fingerprint, seeded.SecondFingerprint);
    }

    [Fact]
    public void Outside_local_environments_the_full_seed_accepts_its_own_data_and_refuses_real_data()
    {
        Assert.Equal(0, seeded.StagingRerun);
        Assert.Equal(1, seeded.StagingWithRealData);
    }

    /// <summary>Mean absolute difference of the red channel on a grid of pixels, both images scaled to the stored size.</summary>
    private static double Distance(byte[] stored, byte[] rendered)
    {
        using var a = SKBitmap.Decode(stored);
        using var b = SKBitmap.Decode(rendered).Resize(new SKImageInfo(a.Width, a.Height), new SKSamplingOptions(SKFilterMode.Linear));
        var (sum, count) = (0.0, 0);
        for (var y = 0; y < a.Height; y += 3)
        {
            for (var x = 0; x < a.Width; x += 3)
            {
                sum += Math.Abs(a.GetPixel(x, y).Red - b.GetPixel(x, y).Red);
                count++;
            }
        }

        return sum / count;
    }

    [Fact]
    public void No_minor_holds_an_issued_license_and_owned_weapons_match_the_side()
    {
        var population = seeded.Arquebusiers.Where(a => Added.Contains(a.ComparsaId)).ToList();
        Assert.All(population.Where(a => a.BirthDate.AddYears(18) > seeded.Today), a => Assert.Null(a.LicenseIssuedOn));
        Assert.All(population.Where(a => a.LicenseIssuedOn is not null), a => Assert.True(a.LicenseIssuedOn >= a.BirthDate.AddYears(18)));

        var sides = seeded.Comparsas.ToDictionary(c => c.Id, c => c.Side);
        var owners = seeded.Arquebusiers.ToDictionary(a => a.Id, a => a.ComparsaId);
        var models = seeded.Models.ToDictionary(m => m.Id);
        var ids = population.Select(a => a.Id).ToHashSet();
        var weapons = seeded.Weapons.Where(w => ids.Contains(w.ArquebusierId)).ToList();
        Assert.NotEmpty(weapons);
        Assert.All(weapons, w =>
        {
            var model = models[w.WeaponModelId];
            Assert.True(model.Kind == WeaponKind.Pistol || model.Side == sides[owners[w.ArquebusierId]], $"{model.Label} owned in a {sides[owners[w.ArquebusierId]]} comparsa");
        });
    }

    [Fact]
    public void Most_have_an_id_photo_and_most_licensed_have_both_license_photos()
    {
        var population = seeded.Arquebusiers.Where(a => Added.Contains(a.ComparsaId)).ToList();
        var ids = population.Select(a => a.Id).ToHashSet();
        var photos = seeded.Photos.Where(p => ids.Contains(p.ArquebusierId)).ToList();
        Assert.InRange(photos.Count(p => p.Kind == ArquebusierPhotoKind.Id), population.Count * 0.7, population.Count * 0.95);

        var licensed = population.Where(a => a.LicenseIssuedOn is not null).Select(a => a.Id).ToList();
        var both = licensed.Count(id => photos.Count(p => p.ArquebusierId == id && p.Kind != ArquebusierPhotoKind.Id) == 2);
        Assert.True(both > licensed.Count * 0.75, $"{both} of {licensed.Count}");
        Assert.All(photos.Where(p => p.Kind == ArquebusierPhotoKind.Id), p => Assert.Equal((600, 800), (p.Width, p.Height)));
        Assert.All(photos.Where(p => p.Kind != ArquebusierPhotoKind.Id), p => Assert.Equal((1000, 630), (p.Width, p.Height)));
    }

    [Fact]
    public void The_added_comparsas_have_a_validated_past_order_and_the_planned_current_mix()
    {
        var past = seeded.Orders.Where(o => o.EditionId == EditionSeeder.PastEdition && Added.Contains(o.ComparsaId)).ToList();
        var current = seeded.Orders.Where(o => o.EditionId == EditionSeeder.CurrentEdition && Added.Contains(o.ComparsaId)).ToList();

        Assert.Equal(16, past.Count);
        Assert.All(past, o => Assert.Equal(OrderStatus.Validated, o.Status));
        Assert.Equal(14, current.Count);
        Assert.Equal(
            new Dictionary<OrderStatus, int> { [OrderStatus.Draft] = 4, [OrderStatus.Submitted] = 5, [OrderStatus.Returned] = 2, [OrderStatus.Validated] = 3 },
            current.GroupBy(o => o.Status).ToDictionary(g => g.Key, g => g.Count()));
        Assert.All(current.Where(o => o.Status == OrderStatus.Returned), o => Assert.False(string.IsNullOrWhiteSpace(o.ReturnReason)));
        Assert.All(current.Where(o => o.Status != OrderStatus.Returned), o => Assert.Null(o.ReturnReason));
        Assert.All(current.Where(o => o.Status != OrderStatus.Draft), o => Assert.True(o.SubmittedAt is not null && o.Attested));
    }

    [Fact]
    public void Each_current_order_leaves_one_or_two_arquebusiers_out()
    {
        foreach (var order in seeded.Orders.Where(o => o.EditionId == EditionSeeder.CurrentEdition && Added.Contains(o.ComparsaId)))
        {
            var members = seeded.Arquebusiers.Count(a => a.ComparsaId == order.ComparsaId);
            var entries = seeded.Entries.Count(e => e.OrderId == order.Id);
            Assert.InRange(members - entries, 1, 2);
        }
    }

    [Fact]
    public void Every_generated_entry_and_loan_passes_the_apis_rules()
    {
        var orders = seeded.Orders.Where(o => Added.Contains(o.ComparsaId)).ToDictionary(o => o.Id);
        var weapons = seeded.Weapons.ToLookup(w => w.ArquebusierId, w => w.Id);
        var arquebusiers = seeded.Arquebusiers.ToDictionary(a => a.Id);
        var loans = seeded.Loans.ToDictionary(l => l.EntryId);
        var entries = seeded.Entries.Where(e => orders.ContainsKey(e.OrderId)).ToList();
        Assert.NotEmpty(entries);

        // Left-handed trabucos are not offered for rental: their shooters borrow a team-mate's (BR-09).
        Assert.Contains(entries, e => e.WeaponSource == WeaponSource.Loan);
        foreach (var entry in entries)
        {
            var offered = seeded.Offered[entry.EditionId];
            var loan = loans.GetValueOrDefault(entry.Id);
            var fields = new EntryFields(
                EnumCodes.ToCode(entry.Status), entry.PowderKg, entry.CapsBoxes, entry.CapsType is { } caps ? EnumCodes.ToCode(caps) : null,
                EnumCodes.ToCode(entry.WeaponSource), entry.OwnedWeaponId, entry.RentalWeaponModelId,
                loan is { LenderOwnedWeaponId: { } lent } ? new LoanFields(lent, null) : null, EnumCodes.ToCode(entry.Flask));
            var context = new EntryContext(weapons[entry.ArquebusierId!.Value].ToHashSet(), offered);

            var (_, errors) = EntryInput.Read(fields, context);

            Assert.True(errors.Count == 0, $"Entry {entry.Number(entries)}: {string.Join(", ", errors.Select(e => $"{e.Key}={e.Value}"))}");
            Assert.Empty(EntryIssues.Of(entry, loan, offered));
            var person = arquebusiers[entry.ArquebusierId.Value];
            Assert.Equal(person.Status, entry.Status);
            if (entry.WeaponSource == WeaponSource.Loan)
            {
                Assert.NotNull(loan);
                Assert.NotEqual(entry.ArquebusierId, seeded.Weapons.Single(w => w.Id == loan.LenderOwnedWeaponId).ArquebusierId);
            }
        }
    }

    [Fact]
    public void Owners_of_a_trabuco_or_arcabuz_use_it_and_rentals_match_the_side()
    {
        var models = seeded.Models.ToDictionary(m => m.Id);
        var sides = seeded.Comparsas.ToDictionary(c => c.Id, c => c.Side);
        var arquebusiers = seeded.Arquebusiers.ToDictionary(a => a.Id);
        var shooters = seeded.Weapons.Where(w => models[w.WeaponModelId].Kind != WeaponKind.Pistol).Select(w => w.ArquebusierId).ToHashSet();
        var entries = seeded.Entries.Where(e => e.ArquebusierId is { } id && Added.Contains(arquebusiers[id].ComparsaId)).ToList();

        Assert.All(entries.Where(e => e.Status == ArquebusierStatus.Active && shooters.Contains(e.ArquebusierId!.Value)), e => Assert.Equal(WeaponSource.Owned, e.WeaponSource));
        Assert.All(entries.Where(e => e.WeaponSource == WeaponSource.Rental), e =>
            Assert.Equal(sides[arquebusiers[e.ArquebusierId!.Value].ComparsaId], models[e.RentalWeaponModelId!.Value].Side));
        Assert.Contains(entries, e => e.WeaponSource == WeaponSource.Rental);
        Assert.Contains(entries, e => e.Status == ArquebusierStatus.Reserve);
    }

    [Fact]
    public void Some_arquebusiers_of_the_added_comparsas_are_first_year()
    {
        var pastActive = seeded.Entries.Where(e => e.EditionId == EditionSeeder.PastEdition && e.Status == ArquebusierStatus.Active).Select(e => e.ArquebusierId).ToHashSet();
        var currentActive = seeded.Entries.Where(e => e.EditionId == EditionSeeder.CurrentEdition && e.Status == ArquebusierStatus.Active && e.ArquebusierId is { } id
            && Added.Contains(seeded.Arquebusiers.Single(a => a.Id == id).ComparsaId)).ToList();

        Assert.Contains(currentActive, e => !pastActive.Contains(e.ArquebusierId));
        Assert.Contains(currentActive, e => pastActive.Contains(e.ArquebusierId));
    }

    [Fact]
    public void The_seed_time_is_recorded()
    {
        // Recorded in design.md (task 4.4); a guard against an accidental slowdown.
        Assert.True(seeded.Elapsed < TimeSpan.FromMinutes(10), $"The full seed took {seeded.Elapsed}.");
    }

    /// <summary>Seeds the full dataset twice on one database (rerun), and once on a second one (determinism).</summary>
    public sealed class Seeded(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio) : IAsyncLifetime
    {
        public (int First, int Rerun) ExitCodes { get; private set; }

        public DateOnly Today { get; private set; }

        public TimeSpan Elapsed { get; private set; }

        internal List<Arquebusier> Arquebusiers { get; private set; } = [];

        internal List<OwnedWeapon> Weapons { get; private set; } = [];

        internal List<ArquebusierPhoto> Photos { get; private set; } = [];

        internal List<Comparsa> Comparsas { get; private set; } = [];

        internal List<WeaponModel> Models { get; private set; } = [];

        internal List<ComparsaOrder> Orders { get; private set; } = [];

        internal List<EditionEntry> Entries { get; private set; } = [];

        internal List<WeaponLoan> Loans { get; private set; } = [];

        internal Dictionary<Guid, IReadOnlySet<Guid>> Offered { get; } = [];

        public string Fingerprint { get; private set; } = string.Empty;

        public string SecondFingerprint { get; private set; } = string.Empty;

        public int StagingRerun { get; private set; }

        public int StagingWithRealData { get; private set; }

        /// <summary>A stored license front, its holder and another licensed holder.</summary>
        internal List<(Arquebusier Owner, Arquebusier Other, byte[] Stored)> LicenseFronts { get; } = [];

        private readonly PolvorApp.Api.Platform.Images.SkiaSpecimenCardPainter _painter = new();

        internal byte[] Render(Arquebusier holder) => _painter.Front(new SpecimenCardData(
            holder.NationalId, holder.FirstName, holder.LastName, holder.BirthDate,
            holder.LicenseType == LicenseType.AProf ? SyntheticLicenseType.AProf : SyntheticLicenseType.Ae,
            holder.LicenseIssuedOn!.Value, holder.LicenseExpiresOn!.Value));

        public async ValueTask InitializeAsync()
        {
            await using (var host = await StartAsync())
            {
                var clock = Stopwatch.StartNew();
                var first = await SeedAsync(host);
                Elapsed = clock.Elapsed;
                ExitCodes = (first, await SeedAsync(host));
                Today = FederationCalendar.Today(host.Time);
                await using var scope = host.Services.CreateAsyncScope();
                var registry = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
                Arquebusiers = await registry.Arquebusiers.AsNoTracking().ToListAsync();
                Weapons = await registry.OwnedWeapons.AsNoTracking().ToListAsync();
                Photos = await registry.Photos.AsNoTracking().ToListAsync();
                var catalog = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
                Comparsas = await catalog.Comparsas.AsNoTracking().ToListAsync();
                Models = await catalog.WeaponModels.AsNoTracking().ToListAsync();
                var orders = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
                Orders = await orders.Orders.AsNoTracking().ToListAsync();
                Entries = await orders.Entries.AsNoTracking().ToListAsync();
                Loans = await orders.Loans.AsNoTracking().ToListAsync();
                var editions = scope.ServiceProvider.GetRequiredService<IEditionDirectory>();
                foreach (var id in new[] { EditionSeeder.PastEdition, EditionSeeder.CurrentEdition })
                {
                    Offered[id] = (await editions.FindAsync(id, CancellationToken.None))!.OfferedWeaponModelIds.ToHashSet();
                }

                Fingerprint = await FingerprintAsync(scope);

                var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
                var licensed = Arquebusiers.Where(a => a.LicenseIssuedOn is not null).OrderBy(a => a.Id).ToList();
                foreach (var photo in Photos.Where(p => p.Kind == ArquebusierPhotoKind.LicenseFront).OrderBy(p => p.Id).Take(3))
                {
                    var owner = licensed.Single(a => a.Id == photo.ArquebusierId);
                    var stranger = licensed.First(a => a.Id != owner.Id && a.LastName != owner.LastName);
                    await using var stored = await storage.GetAsync(photo.ObjectKey, CancellationToken.None);
                    using var buffer = new MemoryStream();
                    await stored!.Content.CopyToAsync(buffer);
                    LicenseFronts.Add((owner, stranger, buffer.ToArray()));
                }

                // Staging accepts the full dataset's own rows and photos, and refuses a real row before writing.
                // The seeder reads the host's environment, so it is built for Staging here.
                var staging = ActivatorUtilities.CreateInstance<PolvorApp.ArquebusierRegistry.Seeding.RegistrySeeder>(
                    scope.ServiceProvider, new HostingEnvironment { EnvironmentName = "Staging" });
                StagingRerun = await OutcomeAsync(() => staging.SeedAsync(CancellationToken.None));
                registry.Arquebusiers.Add(RegistryData.NewArquebusier(SyntheticComparsas.ByNumber(5).Id, lastName: "Real Persona"));
                await registry.SaveChangesAsync();
                registry.ChangeTracker.Clear();
                StagingWithRealData = await OutcomeAsync(() => staging.SeedAsync(CancellationToken.None));
            }

            // Scenarios first, then the full dataset, on a second empty database: the same data.
            await using var second = await StartAsync();
            var configuration = second.Services.GetRequiredService<IConfiguration>();
            configuration[SeedDatasets.Key] = nameof(SeedDataset.Scenarios);
            Assert.Equal(0, await SeedAsync(second));
            configuration[SeedDatasets.Key] = nameof(SeedDataset.Full);
            Assert.Equal(0, await SeedAsync(second));
            await using var other = second.Services.CreateAsyncScope();
            SecondFingerprint = await FingerprintAsync(other);
        }

        public ValueTask DisposeAsync()
        {
            _painter.Dispose();
            return ValueTask.CompletedTask;
        }

        private static async Task<string> FingerprintAsync(AsyncServiceScope scope)
        {
            var registry = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
            var orders = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
            var lines = (await registry.Arquebusiers.AsNoTracking().ToListAsync()).OrderBy(a => a.Id)
                .Select(a => $"{a.Id}|{a.FirstName}|{a.LastName}|{a.NationalId}|{a.BirthDate}|{a.Email}|{a.Phone}|{a.Status}|{a.LicenseType}|{a.LicenseIssuedOn}|{a.LicenseExpiresOn}|{a.TrainingCompletedOn}")
                .Concat((await registry.OwnedWeapons.AsNoTracking().ToListAsync()).OrderBy(w => w.Id).Select(w => $"{w.Id}|{w.ArquebusierId}|{w.WeaponModelId}|{w.WeaponNumber}|{w.OwnershipGuideNumber}"))
                .Concat((await registry.Photos.AsNoTracking().ToListAsync()).OrderBy(p => p.Id).Select(p => $"{p.Id}|{p.Kind}|{p.Width}x{p.Height}|{p.SizeBytes}"))
                .Concat((await orders.Orders.AsNoTracking().ToListAsync()).OrderBy(o => o.Id).Select(o => $"{o.Id}|{o.ComparsaId}|{o.Status}|{o.ReturnReason}"))
                .Concat((await orders.Entries.AsNoTracking().ToListAsync()).OrderBy(e => e.Id).Select(e => $"{e.Id}|{e.ArquebusierId}|{e.Status}|{e.PowderKg}|{e.CapsBoxes}|{e.CapsType}|{e.WeaponSource}|{e.OwnedWeaponId}|{e.RentalWeaponModelId}|{e.Flask}"))
                .Concat((await orders.Loans.AsNoTracking().ToListAsync()).OrderBy(l => l.Id).Select(l => $"{l.Id}|{l.EntryId}|{l.LenderOwnedWeaponId}"));
            return string.Join('\n', lines);
        }

        private async Task<IdentityTestHost> StartAsync()
        {
            var settings = new Dictionary<string, string?>(minio.SettingsFor(await minio.CreateBucketAsync()))
            {
                [IdentitySeeder.PasswordKey] = "semilla-sintetica-local",
                // Not the published placeholder: the staging runs refuse it.
                [IdentitySeeder.AuthenticatorKeyKey] = "KRSXG5CTMVRXEZLUKRSXG5CTMVRXEZLU",
                [SeedDatasets.Key] = nameof(SeedDataset.Full),
            };
            return await IdentityTestHost.StartAsync(postgres, mailpit, settings);
        }

        private static Task<int> SeedAsync(IdentityTestHost host) =>
            SeedCommand.RunAsync(host.Services, new HostingEnvironment { EnvironmentName = Environments.Development }, CancellationToken.None);

        /// <summary>0 when the seed succeeds, 1 when it refuses with the "not synthetic" error.</summary>
        private static async Task<int> OutcomeAsync(Func<Task> seed)
        {
            try
            {
                await seed();
                return 0;
            }
            catch (InvalidOperationException error) when (error.Message.Contains("not synthetic", StringComparison.Ordinal))
            {
                return 1;
            }
        }
    }
}

internal static class EntryNumbers
{
    /// <summary>The entry's position, for a readable failure message without personal data.</summary>
    public static int Number(this EditionEntry entry, List<EditionEntry> entries) => entries.IndexOf(entry);
}
