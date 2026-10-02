using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.FestivalEditions.Persistence;

namespace PolvorApp.Api.Tests.Editions;

/// <summary>
/// Design D1 and D3: the database backs up the blocking edition rules, so a race past the API checks
/// still cannot store a second edition in progress, open orders outside it, or a dangling model.
/// </summary>
public sealed class EditionsDatabaseTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory? _factory;

    public async ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync());
        Assert.Equal(0, await MigrateCommand.RunAsync(_factory.Services, TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_second_edition_in_progress_violates_the_index()
    {
        await SaveAsync(NewEdition(2031, EditionStatus.InProgress), NewEdition(2030, EditionStatus.Closed), NewEdition(2032, EditionStatus.Draft));

        var error = await FailAsync(NewEdition(2033, EditionStatus.InProgress));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, FestivalEditionsDbContext.InProgressIndex), error);
    }

    [Fact]
    public async Task Several_drafts_and_closed_editions_may_coexist()
    {
        await SaveAsync(
            NewEdition(2040, EditionStatus.Draft),
            NewEdition(2041, EditionStatus.Draft),
            NewEdition(2042, EditionStatus.Closed),
            NewEdition(2043, EditionStatus.Closed));

        Assert.Equal(4, await CountAsync());
    }

    [Fact]
    public async Task A_year_is_unique()
    {
        await SaveAsync(NewEdition(2031, EditionStatus.Draft));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, FestivalEditionsDbContext.YearIndex), await FailAsync(NewEdition(2031, EditionStatus.Closed)));
    }

    [Theory]
    [InlineData(EditionStatus.Draft)]
    [InlineData(EditionStatus.Closed)]
    public async Task Open_orders_outside_the_edition_in_progress_violate_the_check(EditionStatus status)
    {
        var edition = NewEdition(2031, status);
        edition.OrdersOpen = true;

        Assert.Equal((PostgresErrorCodes.CheckViolation, FestivalEditionsDbContext.OrdersOpenCheck), await FailAsync(edition));
    }

    [Fact]
    public async Task Dates_out_of_order_and_negative_prices_violate_their_checks()
    {
        var festival = NewEdition(2031, EditionStatus.Draft);
        festival.FestivalEndsOn = festival.FestivalStartsOn.AddDays(-1);
        var window = NewEdition(2032, EditionStatus.Draft);
        (window.OrdersOpenOn, window.OrdersCloseOn) = (new DateOnly(2032, 2, 10), new DateOnly(2032, 2, 1));
        var price = NewEdition(2033, EditionStatus.Draft);
        price.CapsBox = -0.01m;

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_festival_editions_festival_dates"), await FailAsync(festival));
        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_festival_editions_orders_window"), await FailAsync(window));
        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_festival_editions_caps_box"), await FailAsync(price));
    }

    [Fact]
    public async Task An_unknown_status_code_violates_its_check()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();

        // The EF converter cannot produce an unknown code, so this goes around it.
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO editions.festival_editions (id, year, festival_starts_on, festival_ends_on, status, orders_open, created_at)"
            + " VALUES (gen_random_uuid(), 2031, '2031-04-22', '2031-04-25', 'OPEN', false, now())",
            TestContext.Current.CancellationToken));

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_festival_editions_status"), (error.SqlState, error.ConstraintName));
    }

    [Fact]
    public async Task A_year_outside_the_range_violates_its_check()
    {
        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_festival_editions_year"), await FailAsync(NewEdition(1999, EditionStatus.Draft)));
    }

    [Fact]
    public async Task Deleting_a_weapon_model_an_edition_offers_violates_the_foreign_key()
    {
        var model = await SaveModelAsync();
        var edition = NewEdition(2031, EditionStatus.Draft);
        await SaveAsync(edition, new EditionWeaponModel { EditionId = edition.Id, WeaponModelId = model.Id });

        await using var scope = _factory!.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        catalog.WeaponModels.Remove(await catalog.WeaponModels.SingleAsync(m => m.Id == model.Id, TestContext.Current.CancellationToken));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => catalog.SaveChangesAsync(TestContext.Current.CancellationToken));

        var database = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal((PostgresErrorCodes.ForeignKeyViolation, FestivalEditionsDbContext.WeaponModelForeignKey), (database.SqlState, database.ConstraintName));
    }

    [Fact]
    public async Task An_unknown_weapon_model_violates_the_foreign_key()
    {
        var edition = NewEdition(2031, EditionStatus.Draft);
        await SaveAsync(edition);

        var error = await FailAsync(new EditionWeaponModel { EditionId = edition.Id, WeaponModelId = Guid.CreateVersion7() });

        Assert.Equal((PostgresErrorCodes.ForeignKeyViolation, FestivalEditionsDbContext.WeaponModelForeignKey), error);
    }

    [Fact]
    public async Task Deleting_an_edition_removes_its_models_and_milestones()
    {
        var model = await SaveModelAsync();
        var edition = NewEdition(2031, EditionStatus.Draft);
        await SaveAsync(
            edition,
            new EditionWeaponModel { EditionId = edition.Id, WeaponModelId = model.Id },
            new CalendarMilestone { Id = Guid.CreateVersion7(), EditionId = edition.Id, Date = new DateOnly(2030, 11, 30), Title = "Plazo sintético", CreatedAt = DateTimeOffset.UtcNow });

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
            await db.Editions.Where(e => e.Id == edition.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        await using var check = _factory.Services.CreateAsyncScope();
        var after = check.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        Assert.False(await after.EditionWeaponModels.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await after.Milestones.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_blank_milestone_title_violates_its_check()
    {
        var edition = NewEdition(2031, EditionStatus.Draft);
        await SaveAsync(edition);

        var error = await FailAsync(new CalendarMilestone { Id = Guid.CreateVersion7(), EditionId = edition.Id, Date = new DateOnly(2031, 1, 1), Title = "  ", CreatedAt = DateTimeOffset.UtcNow });

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_calendar_milestones_title_not_blank"), error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.01")]
    [InlineData("4.55")]
    [InlineData("55")]
    [InlineData("9999.99")]
    public async Task Prices_round_trip_exactly(string amount)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        var edition = NewEdition(2031, EditionStatus.Draft);
        (edition.PowderPerKg, edition.CapsBox, edition.WeaponRental, edition.FlaskRental) = (value, value, value, value);
        await SaveAsync(edition);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>().Editions.AsNoTracking()
            .SingleAsync(e => e.Id == edition.Id, TestContext.Current.CancellationToken);

        Assert.Equal((value, value, value, value), (stored.PowderPerKg, stored.CapsBox, stored.WeaponRental, stored.FlaskRental));
    }

    [Fact]
    public async Task A_price_beyond_the_precision_is_rejected()
    {
        var edition = NewEdition(2031, EditionStatus.Draft);
        edition.PowderPerKg = 10000m;

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(edition));

        Assert.Equal(PostgresErrorCodes.NumericValueOutOfRange, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    /// <remarks>The dates are clamped to a valid year so an out-of-range year fails only on its own check.</remarks>
    private static FestivalEdition NewEdition(int year, EditionStatus status) => new()
    {
        Id = Guid.CreateVersion7(),
        Year = year,
        FestivalStartsOn = new DateOnly(Math.Clamp(year, 2000, 2100), 4, 22),
        FestivalEndsOn = new DateOnly(Math.Clamp(year, 2000, 2100), 4, 25),
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<WeaponModel> SaveModelAsync()
    {
        var model = new WeaponModel
        {
            Id = Guid.CreateVersion7(),
            Kind = WeaponKind.Arcabuz,
            Side = Side.Moorish,
            Handedness = Handedness.Right,
            Size = WeaponSize.Normal,
            Rentable = true,
            Label = "ARCABUZ SINTÉTICO " + Guid.NewGuid().ToString("N")[..8],
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await using var scope = _factory!.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        catalog.WeaponModels.Add(model);
        await catalog.SaveChangesAsync(TestContext.Current.CancellationToken);
        return model;
    }

    private async Task SaveAsync(params object[] entities)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> CountAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>().Editions.CountAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(string SqlState, string? Constraint)> FailAsync(object entity)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(entity));
        var database = Assert.IsType<PostgresException>(error.InnerException);
        return (database.SqlState, database.ConstraintName);
    }
}
