using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.AuditPrivacy.Privacy;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Handovers;
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

    /// <summary>Spec "Erasing a person's data": a proxy goes whether the erased entry is its holder or its proxy.</summary>
    [Fact]
    public async Task Erasing_the_holder_removes_the_authorisation_and_leaves_the_proxys_entry()
    {
        var edition = NewEdition(2030, EditionStatus.Closed);
        await _registry.Services.SaveEditionsAsync(edition);
        var order = NewOrder(edition, _registry.Own.Id, OrderStatus.Validated);
        var holder = NewEntry(order, null);
        holder.PowderKg = 1;
        var proxy = NewEntry(order, null);
        await _registry.Services.SaveOrdersAsync(order, holder, proxy);
        await _registry.Services.SaveDistributionAsync(new PickupProxy
        {
            Id = Guid.CreateVersion7(),
            EditionId = edition.Id,
            ComparsaId = _registry.Own.Id,
            Type = DistributionType.Powder,
            HolderEntryId = holder.Id,
            ProxyEntryId = proxy.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        var person = new PersonalDataSubject.Person(holder.NationalId!);

        var parts = await RunAsync(r => r.ExportAsync(person, TestContext.Current.CancellationToken));
        var result = await RunAsync(r => r.EraseAsync(person, Audit, TestContext.Current.CancellationToken));

        var sheet = Assert.Single(parts.SelectMany(p => p.Sheets), s => s.Code == "pickupProxies");
        Assert.Equal([2030, "POWDER", "holder"], Assert.Single(sheet.Rows));
        Assert.Equal(1, result!.Counts["pickupProxiesRemoved"]);
        await using var scope = _registry.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Proxies.AnyAsync(TestContext.Current.CancellationToken));
        var kept = await scope.ServiceProvider.GetRequiredService<PolvorApp.ComparsaOrders.Persistence.ComparsaOrdersDbContext>().Entries
            .AsNoTracking().SingleAsync(e => e.Id == proxy.Id, TestContext.Current.CancellationToken);
        Assert.Equal((proxy.NationalId, (DateTimeOffset?)null), (kept.NationalId, kept.ErasedAt));
    }

    /// <summary>Spec "Exporting a person's data": handovers by role; distribution "Powder handovers": kept, anonymised, on erasure.</summary>
    [Fact]
    public async Task A_persons_handovers_are_exported_by_role_and_kept_on_erasure()
    {
        var edition = NewEdition(2031, EditionStatus.Closed);
        await _registry.Services.SaveEditionsAsync(edition);
        var order = NewOrder(edition, _registry.Own.Id, OrderStatus.Validated);
        var holder = NewEntry(order, null);
        holder.PowderKg = 2;
        var proxy = NewEntry(order, null);
        await _registry.Services.SaveOrdersAsync(order, holder, proxy);
        var day = new DistributionDay
        {
            Id = Guid.CreateVersion7(),
            EditionId = edition.Id,
            Type = DistributionType.Powder,
            Date = new DateOnly(2031, 4, 18),
            Location = "Paraje Sintético",
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var collectedAt = new DateTimeOffset(2031, 4, 18, 9, 30, 0, TimeSpan.Zero);
        var handover = new Handover
        {
            Id = Guid.CreateVersion7(),
            DistributionId = day.Id,
            HolderEntryId = holder.Id,
            DistributionNumber = 3,
            CollectedBy = HandoverCollector.Proxy,
            CollectorEntryId = proxy.Id,
            PowderKg = 2,
            RentalFlaskNumber = "P-117",
            Traceability1 = "A3",
            CollectedAt = collectedAt,
            RecordedAt = DateTimeOffset.UtcNow,
        };
        await _registry.Services.SaveDistributionAsync(day, handover);
        var asHolder = new PersonalDataSubject.Person(holder.NationalId!);
        var asProxy = new PersonalDataSubject.Person(proxy.NationalId!);

        var holderParts = await RunAsync(r => r.ExportAsync(asHolder, TestContext.Current.CancellationToken));
        var proxyParts = await RunAsync(r => r.ExportAsync(asProxy, TestContext.Current.CancellationToken));
        await RunAsync(r => r.EraseAsync(asHolder, Audit, TestContext.Current.CancellationToken));

        var holderSheet = Assert.Single(holderParts.SelectMany(p => p.Sheets), s => s.Code == "handovers");
        Assert.Equal(["edition", "collectedAt", "powderKg", "rentalFlaskNumber", "traceability1", "traceability2", "participation"], holderSheet.Columns);
        Assert.Equal([2031, collectedAt, 2, "P-117", "A3", null, "holderByProxy"], Assert.Single(holderSheet.Rows));
        await using (var packaging = _registry.Services.CreateAsyncScope())
        {
            // Every column of the sheet has its translated name, or the export would fail.
            var package = await packaging.ServiceProvider.GetRequiredService<PersonalDataPackager>()
                .BuildAsync(holderParts, "REQ-PRUEBA-4", TestContext.Current.CancellationToken);
            Assert.True(package.Sheets.Count > 0);
        }

        var proxySheet = Assert.Single(proxyParts.SelectMany(p => p.Sheets), s => s.Code == "handovers");
        Assert.Equal([2031, collectedAt, null, "P-117", null, null, "proxy"], Assert.Single(proxySheet.Rows));
        Assert.DoesNotContain(holder.NationalId, proxySheet.Rows.SelectMany(r => r).OfType<string>());
        await using var scope = _registry.Services.CreateAsyncScope();
        var kept = await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Handovers.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((holder.Id, "P-117"), (kept.HolderEntryId, kept.RentalFlaskNumber));
    }

    private static AuditRecord Audit(IReadOnlyDictionary<string, int> counts) =>
        new("PersonalDataErased", "PersonalDataRequest", Data: new { reference = "REQ-PRUEBA-3", counts });

    private async Task<T> RunAsync<T>(Func<PersonalDataRequests, Task<T>> run)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await run(scope.ServiceProvider.GetRequiredService<PersonalDataRequests>());
    }
}
