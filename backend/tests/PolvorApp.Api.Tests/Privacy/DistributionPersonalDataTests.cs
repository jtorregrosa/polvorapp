using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.AuditPrivacy.Privacy;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Persistence;
using PolvorApp.Distribution.Proxies;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Auditing;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>Spec audit-privacy "Erasing a person's data" (proxies removed), distribution "Erased entries in distribution".</summary>
public sealed class DistributionPersonalDataTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync() => _registry = await RegistryTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_persons_pickup_authorisations_are_shown_by_role_and_removed_on_erasure()
    {
        var edition = NewEdition(2031, EditionStatus.Closed);
        await _registry.Services.SaveEditionsAsync(edition);
        var order = NewOrder(edition, _registry.Own.Id, OrderStatus.Validated);
        var holder = NewEntry(order, null);
        holder.PowderKg = 1;
        var proxy = NewEntry(order, null);
        await _registry.Services.SaveOrdersAsync(order, holder, proxy);
        var authorisation = new PickupProxy
        {
            Id = Guid.CreateVersion7(),
            EditionId = edition.Id,
            ComparsaId = _registry.Own.Id,
            Type = DistributionType.Powder,
            HolderEntryId = holder.Id,
            ProxyEntryId = proxy.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _registry.Services.SaveDistributionAsync(authorisation);
        var person = new PersonalDataSubject.Person(proxy.NationalId!);

        var summary = await RunAsync(r => r.DescribeAsync(person, TestContext.Current.CancellationToken));
        var parts = await RunAsync(r => r.ExportAsync(person, TestContext.Current.CancellationToken));
        var result = await RunAsync(r => r.EraseAsync(person, Audit, TestContext.Current.CancellationToken));

        Assert.Equal(1, summary.Counts["pickupProxies"]);
        var sheet = Assert.Single(parts.SelectMany(p => p.Sheets), s => s.Code == "pickupProxies");
        Assert.Equal([2031, "POWDER", "proxy"], Assert.Single(sheet.Rows));
        Assert.DoesNotContain(holder.NationalId, sheet.Rows.SelectMany(r => r).OfType<string>());
        Assert.Equal(1, result!.Counts["pickupProxiesRemoved"]);
        await using var scope = _registry.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Proxies.AnyAsync(TestContext.Current.CancellationToken));
    }

    private static AuditRecord Audit(IReadOnlyDictionary<string, int> counts) =>
        new("PersonalDataErased", "PersonalDataRequest", Data: new { reference = "REQ-PRUEBA-3", counts });

    private async Task<T> RunAsync<T>(Func<PersonalDataRequests, Task<T>> run)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await run(scope.ServiceProvider.GetRequiredService<PersonalDataRequests>());
    }
}
