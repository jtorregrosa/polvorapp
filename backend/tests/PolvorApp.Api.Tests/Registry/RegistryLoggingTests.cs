using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// NFR-12 and design D11: rejected registrations log no personal value, neither on the pre-check
/// path, nor when the database rejects a duplicate that lost a race, nor on validation failures.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class RegistryLoggingTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Rejected_registrations_log_no_personal_values()
    {
        var logs = new CapturingLoggerProvider();
        BarrierAuditTrail barrier = null!;
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit, services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            barrier = BarrierAuditTrail.Decorate(services, "ArquebusierRegistered", parties: 2);
        });

        // A duplicate that loses the race reaches the unique index (the database error path).
        var first = Personal(RegistryTestHost.NewArquebusier(registry.Own.Id));
        var second = Personal(RegistryTestHost.NewArquebusier(registry.Own.Id));
        second["nationalId"] = first["nationalId"];
        var raced = await Task.WhenAll(new[] { first, second }.Select(body => registry.Admin.PostAsync("/api/arquebusiers", body)));
        Assert.Contains(raced, r => r.StatusCode == HttpStatusCode.Conflict);

        // The same duplicate again is caught by the pre-check, and an invalid body by validation.
        barrier.Dispose();
        var again = Personal(RegistryTestHost.NewArquebusier(registry.Own.Id));
        again["nationalId"] = first["nationalId"];
        using var duplicate = await registry.FiringChief.PostAsync("/api/arquebusiers", again);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var invalid = Personal(RegistryTestHost.NewArquebusier(registry.Own.Id));
        invalid["birthDate"] = "2999-01-01";
        using var rejected = await registry.FiringChief.PostAsync("/api/arquebusiers", invalid);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var logged = string.Join('\n', logs.Entries.Select(Describe));

        // Proves the database error path ran and was logged, so its absence of values is meaningful.
        Assert.Contains("ix_arquebusiers_national_id", logged, StringComparison.Ordinal);
        foreach (var body in new[] { first, second, again, invalid })
        {
            foreach (var field in new[] { "nationalId", "firstName", "lastName", "email", "phone" })
            {
                Assert.DoesNotContain((string)body[field]!, logged, StringComparison.OrdinalIgnoreCase);
            }

            Assert.DoesNotContain(((int)body["federationId"]!).ToString(System.Globalization.CultureInfo.InvariantCulture), logged, StringComparison.Ordinal);
        }
    }

    /// <summary>Distinct, recognisable synthetic values, so a leak cannot hide behind a common word.</summary>
    private static Dictionary<string, object?> Personal(Dictionary<string, object?> body)
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        body["firstName"] = "Nombre" + marker;
        body["lastName"] = "Apellido" + marker;
        body["email"] = $"correo{marker}@polvorapp.example";
        body["phone"] = "+34 699 " + marker[..3].Select(c => (c % 10).ToString(System.Globalization.CultureInfo.InvariantCulture)).Aggregate(string.Concat);
        return body;
    }

    private static string Describe(CapturedLog entry) =>
        entry.Message + " " + entry.Exception + " "
        + JsonSerializer.Serialize(entry.State.ToDictionary(p => p.Key, p => p.Value?.ToString()))
        + JsonSerializer.Serialize(entry.Scopes.Select(s => s.ToDictionary(p => p.Key, p => p.Value?.ToString())));
}
