using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.Api.Platform.Seeding;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.ComparsaOrders.Seeding;
using PolvorApp.FederationCatalog.Seeding;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>Synthetic orders (SEC-11, design D13): fictional, consistent with the other seeders, safe to run again.</summary>
public sealed class OrderSeederTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio)
{
    private const string SeedPassword = "semilla-sintetica-local";
    private const string SeedKey = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    /// <summary>The registry seeder's arquebusiers (see its table).</summary>
    private static readonly Guid Uno = Arquebusier(1);
    private static readonly Guid Tres = Arquebusier(3);
    private static readonly Guid Nueve = Arquebusier(9);

    [Fact]
    public async Task Seeding_twice_creates_every_order_entry_and_loan_once()
    {
        await using var host = await StartAsync();

        Assert.Equal(0, await SeedAsync(host));
        Assert.Equal(0, await SeedAsync(host));

        var (orders, entries, loans) = await ReadAsync(host, async db => (
            await db.Orders.CountAsync(TestContext.Current.CancellationToken),
            await db.Entries.CountAsync(TestContext.Current.CancellationToken),
            await db.Loans.CountAsync(TestContext.Current.CancellationToken)));
        Assert.Equal((OrderSeeder.OrderCount, OrderSeeder.EntryCount, OrderSeeder.LoanCount), (orders, entries, loans));
    }

    [Fact]
    public async Task The_current_edition_has_Norte_submitted_Sur_draft_and_Este_not_prepared()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));

        var current = await ReadAsync(host, db => db.Orders.AsNoTracking()
            .Where(o => o.EditionId == EditionSeederIds.Current)
            .ToDictionaryAsync(o => o.ComparsaId, o => o.Status, TestContext.Current.CancellationToken));
        var past = await ReadAsync(host, db => db.Orders.AsNoTracking()
            .Where(o => o.EditionId == EditionSeederIds.Past)
            .Select(o => o.Status)
            .ToListAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            new Dictionary<Guid, OrderStatus> { [CatalogSeeder.Norte] = OrderStatus.Submitted, [CatalogSeeder.Sur] = OrderStatus.Draft },
            current);
        Assert.Equal([OrderStatus.Validated, OrderStatus.Validated], past);
    }

    [Fact]
    public async Task The_past_orders_make_the_first_year_known()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));
        await using var scope = host.Services.CreateAsyncScope();
        var current = await scope.ServiceProvider.GetRequiredService<IEditionDirectory>().GetCurrentAsync(TestContext.Current.CancellationToken);

        var result = await scope.ServiceProvider.GetRequiredService<IParticipationHistory>()
            .FirstYearAsync(current!.Year, [Uno, Tres], TestContext.Current.CancellationToken);

        Assert.True(result.Known);
        Assert.Equal((false, true), (result.Of(Uno), result.Of(Tres)));
    }

    [Fact]
    public async Task Every_value_kind_is_present()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));

        var entries = await ReadAsync(host, db => db.Entries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));

        Assert.Equal(Enum.GetValues<WeaponSource>().ToHashSet(), entries.Select(e => e.WeaponSource).ToHashSet());
        Assert.Equal(Enum.GetValues<FlaskOption>().ToHashSet(), entries.Select(e => e.Flask).ToHashSet());
        Assert.Equal(Enum.GetValues<CapsType>().ToHashSet(), entries.Select(e => e.CapsType).OfType<CapsType>().ToHashSet());
        Assert.Contains(entries, e => e.Status == ArquebusierStatus.Reserve);
        Assert.Contains(entries, e => e is { Status: ArquebusierStatus.Active, PowderKg: 0 });
        Assert.Contains(entries, e => e is { Status: ArquebusierStatus.Active, WeaponSource: WeaponSource.None });
        Assert.DoesNotContain(entries, e => e.ArquebusierId == Nueve);
    }

    [Fact]
    public async Task A_past_entry_without_a_registry_link_keeps_its_synthetic_copy()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));

        var orphan = Assert.Single(await ReadAsync(host, db => db.Entries.AsNoTracking()
            .Where(e => e.ArquebusierId == null)
            .ToListAsync(TestContext.Current.CancellationToken)));

        Assert.Equal(EditionSeederIds.Past, orphan.EditionId);
        Assert.Equal(("Manuel", "Cerdà Boix", "99000091H"), (orphan.FirstName, orphan.LastName, orphan.NationalId));
        Assert.Equal(orphan.NationalId, NationalId.Parse(orphan.NationalId).Value);
    }

    [Fact]
    public async Task The_loans_cross_comparsas_and_include_an_external_owner()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));

        var loans = await ReadAsync(host, db => db.Loans.AsNoTracking().ToDictionaryAsync(l => l.LenderKind, TestContext.Current.CancellationToken));

        var registered = loans[LenderKind.Arquebusier];
        Assert.Equal(CatalogSeeder.Sur, registered.LenderComparsaId);
        Assert.NotNull(registered.LenderOwnedWeaponId);
        var external = loans[LenderKind.External];
        Assert.Equal(OrderSeeder.ExternalOwnerNationalId, NationalId.Parse(external.LenderNationalId).Value);
        await using var scope = host.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IArquebusierRoster>()
            .IsNationalIdRegisteredAsync(external.LenderNationalId!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_submitted_order_has_no_blocking_issue()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));
        await using var scope = host.Services.CreateAsyncScope();
        var current = await scope.ServiceProvider.GetRequiredService<IEditionDirectory>().GetCurrentAsync(TestContext.Current.CancellationToken);
        var offered = current!.OfferedWeaponModelIds.ToHashSet();

        var (entries, loans) = await ReadAsync(host, async db => (
            await db.Entries.AsNoTracking().Where(e => e.OrderId == OrderSeeder.CurrentNorteOrder).ToListAsync(TestContext.Current.CancellationToken),
            await db.Loans.AsNoTracking().ToDictionaryAsync(l => l.EntryId, TestContext.Current.CancellationToken)));

        Assert.Equal(6, entries.Count);
        Assert.All(entries, e => Assert.Empty(EntryIssues.Of(e, loans.GetValueOrDefault(e.Id), offered)));
    }

    [Fact]
    public async Task Outside_local_environments_a_database_with_a_real_order_is_refused()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
        var edition = await scope.ServiceProvider.GetRequiredService<IEditionDirectory>().FindAsync(EditionSeederIds.Current, TestContext.Current.CancellationToken);
        db.Orders.Add(new PolvorApp.ComparsaOrders.Orders.ComparsaOrder
        {
            Id = Guid.CreateVersion7(),
            EditionId = edition!.Id,
            EditionYear = edition.Year,
            ComparsaId = new Guid("0193a100-0000-7000-8000-000000000003"),
            PreparedAt = DateTimeOffset.UtcNow,
            PreparedByUserId = Guid.CreateVersion7(),
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var staging = ActivatorUtilities.CreateInstance<OrderSeeder>(
            scope.ServiceProvider,
            new HostingEnvironment { EnvironmentName = "Staging" },
            NullLogger<OrderSeeder>.Instance);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => staging.SeedAsync(TestContext.Current.CancellationToken));

        Assert.Contains("not synthetic", error.Message, StringComparison.Ordinal);
    }

    private static Guid Arquebusier(int number) => new($"0193a300-0000-7000-8000-{number:D12}");

    private static async Task<T> ReadAsync<T>(IdentityTestHost host, Func<ComparsaOrdersDbContext, Task<T>> read)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>());
    }

    private Task<IdentityTestHost> StartAsync()
    {
        var settings = new Dictionary<string, string?> { [IdentitySeeder.PasswordKey] = SeedPassword, [IdentitySeeder.AuthenticatorKeyKey] = SeedKey };
        foreach (var (key, value) in minio.SettingsFor("polvorapp-test-pedidos"))
        {
            settings[key] = value;
        }

        return IdentityTestHost.StartAsync(postgres, mailpit, settings);
    }

    private static Task<int> SeedAsync(IdentityTestHost host) =>
        SeedCommand.RunAsync(host.Services, new HostingEnvironment { EnvironmentName = Environments.Development }, TestContext.Current.CancellationToken);

    /// <summary>The edition seeder's fixed ids.</summary>
    private static class EditionSeederIds
    {
        public static readonly Guid Past = PolvorApp.FestivalEditions.Seeding.EditionSeeder.PastEdition;
        public static readonly Guid Current = PolvorApp.FestivalEditions.Seeding.EditionSeeder.CurrentEdition;
    }
}
