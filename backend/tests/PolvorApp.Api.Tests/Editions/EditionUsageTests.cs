using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;
using static PolvorApp.Api.Tests.Infrastructure.RegistryData;

namespace PolvorApp.Api.Tests.Editions;

/// <summary>
/// Spec "Edition management by Admins" (add-comparsa-orders): a draft edition that has comparsa
/// orders, because it went back to preparation, cannot be deleted (design D5, <c>IEditionUsage</c>).
/// </summary>
public sealed class EditionUsageTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task A_draft_with_orders_cannot_be_deleted()
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit);
        using var admin = await host.SignInAsync(await host.CreateUserAsync("admin.uso.edicion@example.test", UserRole.Admin));
        var edition = await DraftWithOrderAsync(host);

        using var response = await admin.DeleteAsync($"/api/editions/{edition}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "editions.inUse");
        Assert.True(await EditionExistsAsync(host, edition));
        Assert.Empty(await host.AuditEntriesAsync("EditionDeleted"));
    }

    [Fact]
    public async Task The_foreign_key_backs_up_the_veto()
    {
        await using var host = await IdentityTestHost.StartAsync(
            postgres,
            mailpit,
            configureServices: services =>
            {
                services.RemoveAll<IEditionUsage>();
                services.AddScoped<IEditionUsage, NeverInUse>();
            });
        using var admin = await host.SignInAsync(await host.CreateUserAsync("admin.fk.edicion@example.test", UserRole.Admin));
        var edition = await DraftWithOrderAsync(host);

        using var response = await admin.DeleteAsync($"/api/editions/{edition}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "editions.inUse");
        Assert.True(await EditionExistsAsync(host, edition));
        Assert.Empty(await host.AuditEntriesAsync("EditionDeleted"));
    }

    [Fact]
    public async Task A_draft_without_orders_is_still_deleted()
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit);
        using var admin = await host.SignInAsync(await host.CreateUserAsync("admin.sin.uso@example.test", UserRole.Admin));
        var (id, _) = await admin.CreateEditionAsync(2032);

        using var response = await admin.DeleteAsync($"/api/editions/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<Guid> DraftWithOrderAsync(IdentityTestHost host)
    {
        var comparsa = NewComparsa("Comparsa Sintética Uso Edición");
        await host.Services.SaveCatalogAsync(comparsa);
        var edition = NewEdition(2031, EditionStatus.Draft);
        await host.Services.SaveEditionsAsync(edition);
        await host.Services.SaveOrdersAsync(NewOrder(edition, comparsa.Id));
        return edition.Id;
    }

    private static async Task<bool> EditionExistsAsync(IdentityTestHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>().Editions
            .AnyAsync(e => e.Id == id, TestContext.Current.CancellationToken);
    }

    private sealed class NeverInUse : IEditionUsage
    {
        public Task<bool> IsEditionInUseAsync(Guid editionId, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
