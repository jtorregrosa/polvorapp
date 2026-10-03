using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Persistence;
using PolvorApp.Distribution.Proxies;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;
using static PolvorApp.Api.Tests.Infrastructure.RegistryData;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Design D2: the database backs up the blocking distribution rules — one day per type, one slot per
/// comparsa, one proxy per holder and type, never the holder — and its keys protect editions and
/// comparsas while a proxy goes with its entries.
/// </summary>
public sealed class DistributionDatabaseTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task A_day_with_its_slots_reads_back()
    {
        var (edition, comparsa) = await EditionAndComparsaAsync();
        var day = NewDay(edition.Id, DistributionType.Powder);
        day.Slots.Add(new DistributionSlot { DistributionId = day.Id, ComparsaId = comparsa, StartsAt = new TimeOnly(9, 30) });
        await SaveAsync(day);

        await using var scope = Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Days.AsNoTracking()
            .Include(d => d.Slots).SingleAsync(d => d.Id == day.Id, Token);

        Assert.Equal((DistributionType.Powder, day.Date, "Paraje Sintético"), (stored.Type, stored.Date, stored.Location));
        Assert.Equal(new TimeOnly(9, 30), Assert.Single(stored.Slots).StartsAt);
        Assert.NotEqual(0u, stored.Version);
    }

    [Fact]
    public async Task A_second_day_of_the_same_type_is_refused()
    {
        var (edition, _) = await EditionAndComparsaAsync();
        await SaveAsync(NewDay(edition.Id, DistributionType.Powder));
        await SaveAsync(NewDay(edition.Id, DistributionType.Weapons));

        var error = await FailAsync(NewDay(edition.Id, DistributionType.Powder));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, DistributionDbContext.DayTypeIndex), error);
    }

    [Fact]
    public async Task A_blank_location_is_refused()
    {
        var (edition, _) = await EditionAndComparsaAsync();
        var day = NewDay(edition.Id, DistributionType.Powder);
        day.Location = "  ";

        Assert.Equal((PostgresErrorCodes.CheckViolation, DistributionDbContext.LocationCheck), await FailAsync(day));
    }

    [Fact]
    public async Task Deleting_a_day_removes_its_slots()
    {
        var (edition, comparsa) = await EditionAndComparsaAsync();
        var day = NewDay(edition.Id, DistributionType.Powder);
        day.Slots.Add(new DistributionSlot { DistributionId = day.Id, ComparsaId = comparsa, StartsAt = new TimeOnly(10, 0) });
        await SaveAsync(day);

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DistributionDbContext>();
        await db.Days.Where(d => d.Id == day.Id).ExecuteDeleteAsync(Token);

        Assert.Equal(0, await db.Slots.CountAsync(Token));
    }

    [Fact]
    public async Task A_proxy_reads_back_and_one_per_holder_and_type_is_kept()
    {
        var (holder, proxy, other) = await EntriesAsync();
        var first = NewProxy(holder, proxy, DistributionType.Powder);
        await SaveAsync(first, NewProxy(holder, proxy, DistributionType.Weapons), NewProxy(other, proxy, DistributionType.Powder));

        var error = await FailAsync(NewProxy(holder, other, DistributionType.Powder));

        Assert.Equal((PostgresErrorCodes.UniqueViolation, DistributionDbContext.ProxyHolderIndex), error);
    }

    [Fact]
    public async Task A_holder_cannot_be_their_own_proxy()
    {
        var (holder, _, _) = await EntriesAsync();

        Assert.Equal((PostgresErrorCodes.CheckViolation, DistributionDbContext.NotHolderCheck), await FailAsync(NewProxy(holder, holder, DistributionType.Powder)));
    }

    [Fact]
    public async Task Removing_an_entry_removes_the_proxies_it_is_in()
    {
        var (holder, proxy, other) = await EntriesAsync();
        await SaveAsync(NewProxy(holder, proxy, DistributionType.Powder), NewProxy(other, holder, DistributionType.Weapons), NewProxy(other, proxy, DistributionType.Powder));

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>().Entries.Where(e => e.Id == holder.EntryId).ExecuteDeleteAsync(Token);

        var left = await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Proxies.AsNoTracking().ToListAsync(Token);
        Assert.Equal((other.EntryId, proxy.EntryId), (Assert.Single(left).HolderEntryId, left[0].ProxyEntryId));
    }

    [Fact]
    public async Task An_edition_or_comparsa_in_use_cannot_be_deleted_from_the_database()
    {
        var (edition, comparsa) = await EditionAndComparsaAsync();
        var day = NewDay(edition.Id, DistributionType.Powder);
        day.Slots.Add(new DistributionSlot { DistributionId = day.Id, ComparsaId = comparsa, StartsAt = new TimeOnly(9, 0) });
        await SaveAsync(day);

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DistributionDbContext>();
        var editionError = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"DELETE FROM editions.festival_editions WHERE id = {edition.Id}", Token));
        var comparsaError = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"DELETE FROM catalog.comparsas WHERE id = {comparsa}", Token));

        Assert.Equal((PostgresErrorCodes.ForeignKeyViolation, DistributionDbContext.DayEditionForeignKey), (editionError.SqlState, editionError.ConstraintName));
        Assert.Equal((PostgresErrorCodes.ForeignKeyViolation, DistributionDbContext.SlotComparsaForeignKey), (comparsaError.SqlState, comparsaError.ConstraintName));
    }

    [Theory]
    [InlineData(DistributionDbContext.DayEditionForeignKey, "NO ACTION")]
    [InlineData(DistributionDbContext.SlotComparsaForeignKey, "NO ACTION")]
    [InlineData(DistributionDbContext.ProxyEditionForeignKey, "NO ACTION")]
    [InlineData(DistributionDbContext.ProxyComparsaForeignKey, "NO ACTION")]
    [InlineData(DistributionDbContext.ProxyHolderEntryForeignKey, "CASCADE")]
    [InlineData(DistributionDbContext.ProxyProxyEntryForeignKey, "CASCADE")]
    public async Task Every_cross_schema_key_exists_with_its_delete_rule(string constraint, string deleteRule)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DistributionDbContext>();

        var rules = await db.Database
            .SqlQuery<string>($"SELECT delete_rule AS \"Value\" FROM information_schema.referential_constraints WHERE constraint_schema = 'distribution' AND constraint_name = {constraint}")
            .ToListAsync(Token);

        Assert.Equal([deleteRule], rules);
    }

    private static DistributionDay NewDay(Guid editionId, DistributionType type) => new()
    {
        Id = Guid.CreateVersion7(),
        EditionId = editionId,
        Type = type,
        Date = new DateOnly(2031, 4, 18),
        Location = "Paraje Sintético",
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static PickupProxy NewProxy(EntryRef holder, EntryRef proxy, DistributionType type) => new()
    {
        Id = Guid.CreateVersion7(),
        EditionId = holder.EditionId,
        ComparsaId = holder.ComparsaId,
        Type = type,
        HolderEntryId = holder.EntryId,
        ProxyEntryId = proxy.EntryId,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<(FestivalEdition Edition, Guid Comparsa)> EditionAndComparsaAsync()
    {
        var comparsa = NewComparsa("Comparsa Sintética " + Guid.NewGuid().ToString("N")[..8]);
        await Services.SaveCatalogAsync(comparsa);
        var edition = NewEdition(2031);
        await Services.SaveEditionsAsync(edition);
        return (edition, comparsa.Id);
    }

    private async Task<(EntryRef Holder, EntryRef Proxy, EntryRef Other)> EntriesAsync()
    {
        var (edition, comparsa) = await EditionAndComparsaAsync();
        ComparsaOrder order = NewOrder(edition, comparsa);
        var entries = new[] { NewEntry(order, null), NewEntry(order, null), NewEntry(order, null) };
        await Services.SaveOrdersAsync([order, .. entries]);
        var refs = entries.Select(e => new EntryRef(e.Id, edition.Id, comparsa)).ToArray();
        return (refs[0], refs[1], refs[2]);
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

    private sealed record EntryRef(Guid EntryId, Guid EditionId, Guid ComparsaId);
}
