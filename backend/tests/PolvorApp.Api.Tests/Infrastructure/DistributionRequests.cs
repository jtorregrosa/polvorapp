using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Distribution.Persistence;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>Requests to the distribution routes.</summary>
public static class DistributionRequests
{
    public static Task<HttpResponseMessage> PlanAsync(HttpClient client, Guid editionId, string type, string date, string location) =>
        client.PostAsJsonAsync($"/api/distribution/editions/{editionId}/distributions", new { type, date, location }, TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> EditAsync(HttpClient client, Guid dayId, string date, string location, uint version) =>
        client.PutAsJsonAsync($"/api/distribution/distributions/{dayId}", new { date, location, version }, TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid dayId, uint version) =>
        client.DeleteAsync($"/api/distribution/distributions/{dayId}?version={version}", TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> SaveSlotsAsync(HttpClient client, Guid dayId, uint version, params (Guid ComparsaId, string StartsAt)[] slots) =>
        client.PutAsJsonAsync(
            $"/api/distribution/distributions/{dayId}/slots",
            new { version, slots = slots.Select(s => new { comparsaId = s.ComparsaId, startsAt = s.StartsAt }) },
            TestContext.Current.CancellationToken);

    public static async Task<JsonElement> PlanOfAsync(HttpClient client, Guid editionId)
    {
        using var response = await client.GetAsync($"/api/distribution/editions/{editionId}", TestContext.Current.CancellationToken);
        return await ReadAsync<JsonElement>(response);
    }

    public static async Task<int> CountProxiesAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Proxies.CountAsync(TestContext.Current.CancellationToken);
    }
}
