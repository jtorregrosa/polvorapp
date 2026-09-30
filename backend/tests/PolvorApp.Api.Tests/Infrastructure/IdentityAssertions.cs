using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>Helpers shared by the identity tests: problem codes, audit entries, sessions.</summary>
public static class IdentityAssertions
{
    public static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The <c>errors</c> of a <c>validation</c> problem: field name to reason code.</summary>
    public static async Task<Dictionary<string, string>> ErrorsAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("errors").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }

    public static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, await ProblemCodeAsync(response));
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>Whether the client's session is signed in, as the UI checks it.</summary>
    public static async Task<bool> IsSignedInAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/account", TestContext.Current.CancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => true,
            HttpStatusCode.Unauthorized => false,
            _ => throw new InvalidOperationException($"Unexpected {(int)response.StatusCode} from /api/account"),
        };
    }

    public static async Task<List<AuditEntry>> AuditEntriesAsync(this IdentityTestHost host, string action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await db.Set<AuditEntry>().AsNoTracking().Where(e => e.Action == action).ToListAsync(TestContext.Current.CancellationToken);
    }

    public static Task<HttpResponseMessage> PostAsync(this HttpClient client, string path, object body) =>
        client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
}
