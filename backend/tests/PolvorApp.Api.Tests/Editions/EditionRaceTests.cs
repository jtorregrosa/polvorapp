using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.EditionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Editions;

/// <summary>
/// Design D3: concurrent edition writes end in a blocking problem, never a 500. The unique year
/// index decides two creates of one year, and a write that waits on a held row lock longer than the
/// 5 s lock timeout is answered as retryable (503).
/// </summary>
public sealed class EditionRaceTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Two_creates_of_the_same_year_at_once_store_one()
    {
        BarrierAuditTrail barrier = null!;
        await using var host = await IdentityTestHost.StartAsync(
            postgres, mailpit, configureServices: services => barrier = BarrierAuditTrail.Decorate(services, "EditionCreated", parties: 2));
        using var admin = await host.SignInAsync(await host.CreateUserAsync("admin.carrera.anio@example.test", UserRole.Admin));

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            admin.PostAsync("/api/editions", new { year = 2031, festivalStartsOn = "2031-04-22", festivalEndsOn = "2031-04-25" })));

        Assert.Equal(2, barrier.Arrived);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        await AssertProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.Created), HttpStatusCode.Conflict, "editions.yearTaken");
        Assert.Single(await host.AuditEntriesAsync("EditionCreated"));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task A_write_waiting_on_a_held_lock_is_answered_as_busy()
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit);
        using var admin = await host.SignInAsync(await host.CreateUserAsync("admin.ocupado@example.test", UserRole.Admin));
        var (id, version) = await admin.CreateCompleteEditionAsync(2031);

        await using var scope = host.Services.CreateAsyncScope();
        var holder = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        await using var held = await holder.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await holder.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM editions.festival_editions WHERE id = {id} FOR UPDATE")
            .ToListAsync(TestContext.Current.CancellationToken);

        using var response = await admin.PutAsJsonAsync($"/api/editions/{id}", CompleteBody(2031, version, capsBox: 5m), TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "editions.busy");
        await held.RollbackAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4.50m, (await admin.GetEditionAsync(id)).Prices.CapsBox);
    }
}
