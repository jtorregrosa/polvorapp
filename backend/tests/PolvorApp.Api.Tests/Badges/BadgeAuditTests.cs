using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Badges.Documents;
using PolvorApp.SharedKernel.Auditing;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Badges;

/// <summary>
/// Spec "Badge downloads are audited" (design D7): one entry per returned sheet with ids, counts and the
/// language only; none for a refusal; no file when the audit cannot be written; and a busy answer when
/// both document slots stay taken (design D3).
/// </summary>
public sealed class BadgeAuditTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    private static readonly Uri SheetUri = new("/api/badges/sheet", UriKind.Relative);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_returned_sheet_is_audited_with_ids_and_no_personal_data()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var first = RegistryData.NewArquebusier(registry.Own.Id, "Abad Sintético");
        var second = RegistryData.NewArquebusier(registry.Other.Id, "Zapata Sintética");
        await registry.Services.SaveRegistryAsync(first, second);

        using var selection = await registry.Admin.PostAsJsonAsync(SheetUri, new { arquebusierIds = new[] { first.Id, second.Id }, language = "en" }, Token);
        using var comparsa = await registry.Admin.PostAsJsonAsync(SheetUri, new { comparsaId = registry.Own.Id, language = "es-ES" }, Token);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (selection.StatusCode, comparsa.StatusCode));
        // Told apart by their batch, not by time: both may be recorded in the same instant.
        var entries = (await registry.Host.AuditEntriesAsync("BadgesDownloaded")).OrderByDescending(e => e.Data!.Contains("SELECTION", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(("Badges", registry.AdminId), (e.EntityType, e.ActorUserId)));

        using var selected = JsonDocument.Parse(entries[0].Data!);
        Assert.Equal(("SELECTION", "en", 2, "1"), (Text(selected, "batch"), Text(selected, "language"), selected.RootElement.GetProperty("count").GetInt32(), Text(selected, "version")));
        Assert.Equal([second.Id, first.Id], selected.RootElement.GetProperty("arquebusierIds").EnumerateArray().Select(e => e.GetGuid()));
        Assert.Null(entries[0].ComparsaId);

        using var whole = JsonDocument.Parse(entries[1].Data!);
        Assert.Equal(("COMPARSA", "es-ES", 1), (Text(whole, "batch"), Text(whole, "language"), whole.RootElement.GetProperty("count").GetInt32()));
        Assert.Equal(registry.Own.Id, entries[1].ComparsaId);

        foreach (var entry in entries)
        {
            Assert.DoesNotContain("Sintétic", entry.Data!, StringComparison.Ordinal);
            Assert.DoesNotContain(first.NationalId, entry.Data!, StringComparison.Ordinal);
            Assert.DoesNotContain(second.NationalId, entry.Data!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_refused_request_is_not_audited()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);

        using var empty = await registry.Admin.PostAsJsonAsync(SheetUri, new { comparsaId = registry.Other.Id, language = "es-ES" }, Token);
        using var invalid = await registry.Admin.PostAsJsonAsync(SheetUri, new { language = "es-ES" }, Token);
        using var chief = await registry.FiringChief.PostAsJsonAsync(SheetUri, new { comparsaId = registry.Own.Id, language = "es-ES" }, Token);

        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.BadRequest, HttpStatusCode.Forbidden), (empty.StatusCode, invalid.StatusCode, chief.StatusCode));
        Assert.Empty(await registry.Host.AuditEntriesAsync("BadgesDownloaded"));
    }

    [Fact]
    public async Task No_audit_no_file()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit, services => services.AddScoped<IAuditLog, FailingAuditLog>());
        await registry.Services.SaveRegistryAsync(RegistryData.NewArquebusier(registry.Own.Id));

        using var response = await registry.Admin.PostAsJsonAsync(SheetUri, new { comparsaId = registry.Own.Id, language = "es-ES" }, Token);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "badges.auditUnavailable");
    }

    [Fact]
    public async Task A_sheet_that_waits_too_long_for_a_slot_is_busy()
    {
        var slots = new BadgeSlots(TimeSpan.FromMilliseconds(50));
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit, services => services.AddSingleton(slots));
        await registry.Services.SaveRegistryAsync(RegistryData.NewArquebusier(registry.Own.Id));
        using var first = await slots.EnterAsync(Token);
        using var second = await slots.EnterAsync(Token);

        using var busy = await registry.Admin.PostAsJsonAsync(SheetUri, new { comparsaId = registry.Own.Id, language = "es-ES" }, Token);
        first!.Dispose();
        using var served = await registry.Admin.PostAsJsonAsync(SheetUri, new { comparsaId = registry.Own.Id, language = "es-ES" }, Token);

        await AssertProblemAsync(busy, HttpStatusCode.ServiceUnavailable, "badges.busy");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Single(await registry.Host.AuditEntriesAsync("BadgesDownloaded"));
    }

    private static string? Text(JsonDocument document, string property) => document.RootElement.GetProperty(property).GetString();

    private sealed class FailingAuditLog : IAuditLog
    {
        public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic audit outage.");
    }
}
