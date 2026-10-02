using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.Api.Platform.Seeding;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.FestivalEditions.Seeding;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.Api.Tests.Editions;

/// <summary>Spec "Synthetic edition data" (SEC-11, design D11): fictional, relative to the seed date, safe to run again.</summary>
public sealed class EditionSeederTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio)
{
    private const string SeedPassword = "semilla-sintetica-local";
    private const string SeedKey = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    [Fact]
    public async Task Seeding_creates_a_past_a_current_and_a_draft_edition_once()
    {
        await using var host = await StartAsync();

        Assert.Equal(0, await SeedAsync(host));
        Assert.Equal(0, await SeedAsync(host));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        var editions = await db.Editions.AsNoTracking().OrderBy(e => e.Year).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [(EditionSeeder.PastEdition, EditionStatus.Closed), (EditionSeeder.CurrentEdition, EditionStatus.InProgress), (EditionSeeder.DraftEdition, EditionStatus.Draft)],
            editions.Select(e => (e.Id, e.Status)));
        Assert.Equal([editions[1].Year - 1, editions[1].Year, editions[1].Year + 1], editions.Select(e => e.Year));
        Assert.Equal(EditionSeeder.MilestoneCount, await db.Milestones.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_current_edition_has_open_orders_every_date_and_price_and_rentable_models_of_several_kinds()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));
        var today = FederationCalendar.Today(host.Time);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        var current = await db.Editions.AsNoTracking().SingleAsync(e => e.Id == EditionSeeder.CurrentEdition, TestContext.Current.CancellationToken);
        Assert.True(current.OrdersOpen);
        Assert.True(current.OrdersOpenOn <= today && today <= current.OrdersCloseOn, "The order window does not include the seed date.");
        Assert.Equal(current.Year, current.FestivalStartsOn.Year);
        Assert.Equal(current.Year, current.FestivalEndsOn.Year);
        Assert.Empty(EditionStatusMoves.Check(Draft(current), EditionStatus.InProgress).Missing);

        var directory = scope.ServiceProvider.GetRequiredService<IEditionDirectory>();
        var snapshot = await directory.GetCurrentAsync(TestContext.Current.CancellationToken);
        var models = await scope.ServiceProvider.GetRequiredService<ICatalogDirectory>().FindWeaponModelsAsync(snapshot!.OfferedWeaponModelIds.ToList(), TestContext.Current.CancellationToken);
        Assert.True(snapshot.OrdersOpen);
        Assert.Equal([WeaponKind.Arcabuz, WeaponKind.Trabuco], models.Select(m => m.Kind).ToHashSet());
        Assert.True(await db.Milestones.CountAsync(m => m.EditionId == EditionSeeder.CurrentEdition && m.Date >= today, TestContext.Current.CancellationToken) >= 2);
    }

    [Fact]
    public async Task Outside_local_environments_a_database_with_a_real_edition_is_refused()
    {
        await using var host = await StartAsync();
        Assert.Equal(0, await SeedAsync(host));
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        db.Editions.Add(new FestivalEdition
        {
            Id = Guid.CreateVersion7(),
            Year = 2090,
            FestivalStartsOn = new DateOnly(2090, 4, 22),
            FestivalEndsOn = new DateOnly(2090, 4, 25),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var staging = new EditionSeeder(
            db,
            scope.ServiceProvider.GetRequiredService<ICatalogDirectory>(),
            host.Time,
            new HostingEnvironment { EnvironmentName = "Staging" },
            NullLogger<EditionSeeder>.Instance);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => staging.SeedAsync(TestContext.Current.CancellationToken));

        Assert.Contains("not synthetic", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A draft copy of <paramref name="edition"/>, to ask whether it is complete enough to start.</summary>
    private static FestivalEdition Draft(FestivalEdition edition) => new()
    {
        Id = edition.Id,
        Year = edition.Year,
        FestivalStartsOn = edition.FestivalStartsOn,
        FestivalEndsOn = edition.FestivalEndsOn,
        OrdersOpenOn = edition.OrdersOpenOn,
        OrdersCloseOn = edition.OrdersCloseOn,
        PowderPerKg = edition.PowderPerKg,
        CapsBox = edition.CapsBox,
        WeaponRental = edition.WeaponRental,
        FlaskRental = edition.FlaskRental,
        CreatedAt = edition.CreatedAt,
    };

    private Task<IdentityTestHost> StartAsync()
    {
        var settings = new Dictionary<string, string?> { [IdentitySeeder.PasswordKey] = SeedPassword, [IdentitySeeder.AuthenticatorKeyKey] = SeedKey };
        foreach (var (key, value) in minio.SettingsFor("polvorapp-test-ediciones"))
        {
            settings[key] = value;
        }

        return IdentityTestHost.StartAsync(postgres, mailpit, settings);
    }

    private static Task<int> SeedAsync(IdentityTestHost host) =>
        SeedCommand.RunAsync(host.Services, new HostingEnvironment { EnvironmentName = Environments.Development }, TestContext.Current.CancellationToken);
}
