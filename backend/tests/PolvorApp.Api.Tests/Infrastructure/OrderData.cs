using Microsoft.Extensions.DependencyInjection;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.FestivalEditions.Persistence;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>Synthetic editions, orders and entries written directly, for tests that need state the API reaches only in several steps.</summary>
public static class OrderData
{
    internal static FestivalEdition NewEdition(int year, EditionStatus status = EditionStatus.InProgress, bool ordersOpen = false) => new()
    {
        Id = Guid.CreateVersion7(),
        Year = year,
        FestivalStartsOn = new DateOnly(year, 4, 22),
        FestivalEndsOn = new DateOnly(year, 4, 25),
        Status = status,
        OrdersOpen = ordersOpen,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    internal static ComparsaOrder NewOrder(FestivalEdition edition, Guid comparsaId, OrderStatus status = OrderStatus.Draft) => new()
    {
        Id = Guid.CreateVersion7(),
        EditionId = edition.Id,
        EditionYear = edition.Year,
        ComparsaId = comparsaId,
        Status = status,
        PreparedAt = DateTimeOffset.UtcNow,
        PreparedByUserId = Guid.CreateVersion7(),
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>An <c>ACTIVE</c> entry with no powder, caps, weapon or flask, and a synthetic identity copy.</summary>
    internal static EditionEntry NewEntry(ComparsaOrder order, Guid? arquebusierId, ArquebusierStatus status = ArquebusierStatus.Active) => new()
    {
        Id = Guid.CreateVersion7(),
        OrderId = order.Id,
        EditionId = order.EditionId,
        ArquebusierId = arquebusierId,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
        FirstName = "Arcabucero",
        LastName = "Sintético Copia",
        NationalId = RegistryData.NextIdentity().NationalId,
        FederationId = 799_999,
        CopiedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Saves editions in the editions schema.</summary>
    internal static async Task SaveEditionsAsync(this IServiceProvider services, params object[] entities)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Saves orders, entries and loans in the orders schema.</summary>
    internal static async Task SaveOrdersAsync(this IServiceProvider services, params object[] entities)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
