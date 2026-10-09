using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Handovers;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;
using static PolvorApp.Api.Tests.Infrastructure.RegistryData;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// add-offline-distribution-capture D1: the database backs up the blocking handover rules — one per
/// holder and day, a flask number once per day ignoring case — and its keys keep the record of the
/// day: a handover goes with its holder's entry, keeps its role when the proxy's entry goes, and
/// stops its day from being deleted.
/// </summary>
public sealed class HandoverDatabaseTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory? _factory;

    private IServiceProvider Services => _factory!.Services;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync());
        Assert.Equal(0, await MigrateCommand.RunAsync(_factory.Services, Token));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_handover_reads_back()
    {
        var day = await DayAsync();
        var handover = NewHandover(day.Id, day.Holder, flask: "P-117", collector: day.Proxy);
        await SaveAsync(handover);

        await using var scope = Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Handovers.AsNoTracking()
            .SingleAsync(h => h.Id == handover.Id, Token);

        Assert.Equal(HandoverCollector.Proxy, stored.CollectedBy);
        Assert.Equal(day.Proxy, stored.CollectorEntryId);
        Assert.Equal(("P-117", "A3", (string?)null), (stored.RentalFlaskNumber, stored.Traceability1, stored.Traceability2));
        Assert.Equal((2, 7), (stored.PowderKg, stored.DistributionNumber));
        Assert.NotEqual(0u, stored.Version);
    }

    [Fact]
    public async Task A_second_handover_for_the_same_holder_and_day_is_refused()
    {
        var day = await DayAsync();
        await SaveAsync(NewHandover(day.Id, day.Holder));

        var error = await FailAsync(NewHandover(day.Id, day.Holder));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, DistributionDbContext.HandoverHolderIndex), error);
    }

    [Fact]
    public async Task A_flask_number_is_given_once_per_day_ignoring_case()
    {
        var day = await DayAsync();
        await SaveAsync(NewHandover(day.Id, day.Holder, flask: "P-117"), NewHandover(day.Id, day.Other));

        var error = await FailAsync(NewHandover(day.Id, day.Proxy, flask: "p-117"));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, DistributionDbContext.HandoverFlaskIndex), error);
    }

    [Fact]
    public async Task The_same_flask_number_may_be_given_on_another_day()
    {
        var first = await DayAsync(2031);
        var second = await DayAsync(2030, EditionStatus.Closed);
        await SaveAsync(NewHandover(first.Id, first.Holder, flask: "P-117"));

        await SaveAsync(NewHandover(second.Id, second.Holder, flask: "P-117"));
    }

    [Fact]
    public async Task Handovers_without_a_flask_number_do_not_collide()
    {
        var day = await DayAsync();

        await SaveAsync(NewHandover(day.Id, day.Holder), NewHandover(day.Id, day.Other));
    }

    [Fact]
    public async Task A_holder_collected_handover_has_no_collector_entry()
    {
        var day = await DayAsync();

        Assert.Equal((PostgresErrorCodes.CheckViolation, DistributionDbContext.HandoverCollectorCheck),
            await FailAsync(NewHandover(day.Id, day.Holder, collector: day.Proxy, collectedBy: HandoverCollector.Holder)));
    }

    [Theory]
    [InlineData("  ", null)]
    [InlineData(null, "")]
    public async Task Blank_texts_are_refused(string? flask, string? traceability)
    {
        var day = await DayAsync();
        var handover = NewHandover(day.Id, day.Holder, flask: flask, traceability1: traceability);

        Assert.Equal((PostgresErrorCodes.CheckViolation, DistributionDbContext.HandoverTextsCheck), await FailAsync(handover));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task The_kilograms_are_one_or_two(short powderKg)
    {
        var day = await DayAsync();

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_handovers_powder_kg"),
            await FailAsync(NewHandover(day.Id, day.Holder, powderKg: powderKg)));
    }

    [Fact]
    public async Task A_holder_cannot_collect_as_their_own_proxy()
    {
        var day = await DayAsync();

        Assert.Equal((PostgresErrorCodes.CheckViolation, DistributionDbContext.HandoverNotHolderCheck),
            await FailAsync(NewHandover(day.Id, day.Holder, collector: day.Holder)));
    }

    [Fact]
    public async Task Removing_the_holder_entry_removes_its_handover()
    {
        var day = await DayAsync();
        await SaveAsync(NewHandover(day.Id, day.Holder), NewHandover(day.Id, day.Other));

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>().Entries.Where(e => e.Id == day.Holder).ExecuteDeleteAsync(Token);

        var left = await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Handovers.AsNoTracking().ToListAsync(Token);
        Assert.Equal(day.Other, Assert.Single(left).HolderEntryId);
    }

    [Fact]
    public async Task Removing_the_proxy_entry_keeps_the_handover_as_collected_by_a_proxy()
    {
        var day = await DayAsync();
        var handover = NewHandover(day.Id, day.Holder, collector: day.Proxy);
        await SaveAsync(handover);

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>().Entries.Where(e => e.Id == day.Proxy).ExecuteDeleteAsync(Token);

        var stored = await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Handovers.AsNoTracking()
            .SingleAsync(h => h.Id == handover.Id, Token);
        Assert.Equal((HandoverCollector.Proxy, (Guid?)null), (stored.CollectedBy, stored.CollectorEntryId));
    }

    [Fact]
    public async Task A_day_with_handovers_cannot_be_deleted_from_the_database()
    {
        var day = await DayAsync();
        await SaveAsync(NewHandover(day.Id, day.Holder));

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DistributionDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"DELETE FROM distribution.distributions WHERE id = {day.Id}", Token));

        Assert.Equal((PostgresErrorCodes.RestrictViolation, DistributionDbContext.HandoverDayForeignKey), (error.SqlState, error.ConstraintName));
    }

    [Theory]
    [InlineData(DistributionDbContext.HandoverDayForeignKey, "RESTRICT")]
    [InlineData(DistributionDbContext.HandoverHolderEntryForeignKey, "CASCADE")]
    [InlineData(DistributionDbContext.HandoverCollectorEntryForeignKey, "SET NULL")]
    public async Task Every_handover_key_exists_with_its_delete_rule(string constraint, string deleteRule)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DistributionDbContext>();

        var rules = await db.Database
            .SqlQuery<string>($"SELECT delete_rule AS \"Value\" FROM information_schema.referential_constraints WHERE constraint_schema = 'distribution' AND constraint_name = {constraint}")
            .ToListAsync(Token);

        Assert.Equal([deleteRule], rules);
    }

    private static Handover NewHandover(
        Guid dayId,
        Guid holder,
        string? flask = null,
        Guid? collector = null,
        HandoverCollector? collectedBy = null,
        string? traceability1 = "A3",
        short powderKg = 2) => new()
        {
            Id = Guid.CreateVersion7(),
            DistributionId = dayId,
            HolderEntryId = holder,
            DistributionNumber = 7,
            CollectedBy = collectedBy ?? (collector is null ? HandoverCollector.Holder : HandoverCollector.Proxy),
            CollectorEntryId = collector,
            PowderKg = powderKg,
            RentalFlaskNumber = flask,
            Traceability1 = traceability1,
            CollectedAt = DateTimeOffset.UtcNow,
            RecordedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>A powder day of a new edition, with three entries of one order: a holder, a proxy and another holder.</summary>
    private async Task<DayRef> DayAsync(int year = 2031, EditionStatus status = EditionStatus.InProgress)
    {
        var comparsa = NewComparsa("Comparsa Sintética " + Guid.NewGuid().ToString("N")[..8]);
        await Services.SaveCatalogAsync(comparsa);
        FestivalEdition edition = NewEdition(year, status);
        await Services.SaveEditionsAsync(edition);
        ComparsaOrder order = NewOrder(edition, comparsa.Id);
        var entries = new[] { NewEntry(order, null), NewEntry(order, null), NewEntry(order, null) };
        await Services.SaveOrdersAsync([order, .. entries]);
        var day = new DistributionDay
        {
            Id = Guid.CreateVersion7(),
            EditionId = edition.Id,
            Type = DistributionType.Powder,
            Date = new DateOnly(year, 4, 18),
            Location = "Paraje Sintético",
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await SaveAsync(day);
        return new DayRef(day.Id, entries[0].Id, entries[1].Id, entries[2].Id);
    }

    private async Task SaveAsync(params object[] entities)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DistributionDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync(Token);
    }

    private async Task<(string SqlState, string? Constraint)> FailAsync(object entity)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(entity));
        var database = Assert.IsType<PostgresException>(error.InnerException);
        return (database.SqlState, database.ConstraintName);
    }

    private sealed record DayRef(Guid Id, Guid Holder, Guid Proxy, Guid Other);
}
