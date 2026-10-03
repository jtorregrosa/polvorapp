using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Auditing;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Persistence;

/// <summary>
/// The standalone audit log (change add-exports, design D2): an action that writes nothing, such as
/// an export, is recorded in its own transaction.
/// </summary>
public sealed class AuditLogTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task An_entry_is_stored_on_its_own()
    {
        var actor = Guid.CreateVersion7();
        var comparsa = Guid.CreateVersion7();

        await LogAsync(new AuditRecord("ExportDownloaded", "Export", "arms-authority", new { format = "PDF", rows = 3 }, comparsa, actor));

        var entry = Assert.Single(await _host.AuditEntriesAsync("ExportDownloaded"));
        Assert.Equal((actor, "Export", "arms-authority", comparsa), (entry.ActorUserId, entry.EntityType, entry.EntityId, entry.ComparsaId));
        using var data = System.Text.Json.JsonDocument.Parse(entry.Data!);
        Assert.Equal(3, data.RootElement.GetProperty("rows").GetInt32());
    }

    [Fact]
    public async Task An_invalid_entry_throws_and_stores_nothing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => LogAsync(new AuditRecord(" ", "Export")));

        Assert.Empty(await _host.AuditEntriesAsync(" "));
    }

    private async Task LogAsync(AuditRecord record)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAuditLog>().RecordAsync(record, TestContext.Current.CancellationToken);
    }
}
