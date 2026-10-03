using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Spec "Deleting an arquebusier (UC-05, BR-14)" (add-comparsa-orders, design D3): the registry asks
/// every deletion participant inside its own transaction, records their effect by ids, and a
/// participant's lock failure aborts the whole deletion as retryable.
/// </summary>
public sealed class ArquebusierDeletionParticipantTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Participants_run_in_the_deletion_transaction_and_their_effect_is_audited()
    {
        RecordingParticipant participant = null!;
        await using var registry = await RegistryTestHost.StartAsync(
            postgres,
            mailpit,
            services => services.AddSingleton<IArquebusierDeletionParticipant>(
                provider => participant = new RecordingParticipant(provider.GetRequiredService<NpgsqlDataSource>())));
        var id = Guid.Parse(Id(await registry.RegisterAsync(registry.Own.Id)));
        var weapon = await AddWeaponAsync(registry, id);

        using var response = await registry.Admin.DeleteAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(id, participant.ArquebusierId);
        Assert.Equal([weapon], participant.OwnedWeaponIds);
        Assert.True(participant.RanInTheDeletionTransaction);
        Assert.True(participant.RowWasLockedForOthers);
        var entry = Assert.Single(await registry.Host.AuditEntriesAsync("ArquebusierDeleted"));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal([RecordingParticipant.EntryId.ToString()], data.RootElement.GetProperty("removedEntryIds").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal([RecordingParticipant.OrderId.ToString()], data.RootElement.GetProperty("orderIds").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task A_deletion_without_effects_records_no_order_ids()
    {
        await using var registry = await StartAsync(new NoEffectParticipant());
        var id = Id(await registry.RegisterAsync(registry.Own.Id));

        using var response = await registry.Admin.DeleteAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(await registry.Host.AuditEntriesAsync("ArquebusierDeleted"));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.False(data.RootElement.TryGetProperty("removedEntryIds", out _));
    }

    [Fact]
    public async Task A_participant_lock_failure_rolls_the_deletion_back_as_busy()
    {
        await using var registry = await StartAsync(new TimingOutParticipant());
        var id = Id(await registry.RegisterAsync(registry.Own.Id));

        using var response = await registry.Admin.DeleteAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "registry.busy");
        using var still = await registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
        Assert.Empty(await registry.Host.AuditEntriesAsync("ArquebusierDeleted"));
    }

    private Task<RegistryTestHost> StartAsync(IArquebusierDeletionParticipant participant) =>
        RegistryTestHost.StartAsync(postgres, mailpit, services => services.AddSingleton(participant));

    private static async Task<Guid> AddWeaponAsync(RegistryTestHost registry, Guid arquebusierId)
    {
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO " + Guid.NewGuid().ToString("N")[..8]);
        await registry.Services.SaveCatalogAsync(model);
        var weapon = RegistryData.NewOwnedWeapon(arquebusierId, model.Id, "GUIA-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant());
        await registry.Services.SaveRegistryAsync(weapon);
        return weapon.Id;
    }

    private static string Id(JsonElement arquebusier) => arquebusier.GetProperty("id").GetString()!;

    /// <summary>Records what it was given, and proves it runs inside the deletion after the row lock.</summary>
    private sealed class RecordingParticipant(NpgsqlDataSource dataSource) : IArquebusierDeletionParticipant
    {
        public static readonly Guid EntryId = Guid.CreateVersion7();
        public static readonly Guid OrderId = Guid.CreateVersion7();

        public Guid ArquebusierId { get; private set; }

        public IReadOnlyCollection<Guid> OwnedWeaponIds { get; private set; } = [];

        public bool RanInTheDeletionTransaction { get; private set; }

        public bool RowWasLockedForOthers { get; private set; }

        public async Task<ArquebusierDeletionEffect> OnDeletingAsync(
            Guid arquebusierId, IReadOnlyCollection<Guid> ownedWeaponIds, DbTransaction transaction, CancellationToken cancellationToken)
        {
            (ArquebusierId, OwnedWeaponIds) = (arquebusierId, [.. ownedWeaponIds]);

            // On the deletion's connection the row can be locked again: it is this transaction's lock.
            await using (var own = transaction.Connection!.CreateCommand())
            {
                own.Transaction = transaction;
                own.CommandText = "SELECT 1 FROM registry.arquebusiers WHERE id = $1 FOR UPDATE NOWAIT";
                own.Parameters.Add(new NpgsqlParameter { Value = arquebusierId });
                RanInTheDeletionTransaction = await own.ExecuteScalarAsync(cancellationToken) is 1;
            }

            // From another connection the row is skipped: the deletion locked it before calling us.
            await using var other = dataSource.CreateCommand("SELECT 1 FROM registry.arquebusiers WHERE id = $1 FOR UPDATE SKIP LOCKED");
            other.Parameters.Add(new NpgsqlParameter { Value = arquebusierId });
            RowWasLockedForOthers = await other.ExecuteScalarAsync(cancellationToken) is null;
            return new ArquebusierDeletionEffect([EntryId], [OrderId]);
        }
    }

    private sealed class NoEffectParticipant : IArquebusierDeletionParticipant
    {
        public Task<ArquebusierDeletionEffect> OnDeletingAsync(
            Guid arquebusierId, IReadOnlyCollection<Guid> ownedWeaponIds, DbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(ArquebusierDeletionEffect.None);
    }

    private sealed class TimingOutParticipant : IArquebusierDeletionParticipant
    {
        public Task<ArquebusierDeletionEffect> OnDeletingAsync(
            Guid arquebusierId, IReadOnlyCollection<Guid> ownedWeaponIds, DbTransaction transaction, CancellationToken cancellationToken) =>
            throw new PostgresException("canceling statement due to lock timeout", "ERROR", "ERROR", PostgresErrorCodes.LockNotAvailable);
    }
}
