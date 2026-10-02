using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Import;
using PolvorApp.ArquebusierRegistry.Persistence;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry.Import;

/// <summary>
/// Specs "All-or-nothing import (UC-09)" and "Imports are audited": every row is registered in one
/// transaction, or none; the registry is checked again at that moment; nothing personal is audited.
/// </summary>
public sealed class ImportEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _registry = await RegistryTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task An_Admin_imports_a_clean_file_into_a_comparsa()
    {
        var builder = ImportWorkbookBuilder.Template();
        for (var row = 0; row < 40; row++)
        {
            builder.ValidRow(lastName: $"Importado {row:D2}");
        }

        using var response = await ImportRequests.ImportAsync(_registry.Admin, _registry.Own.Id, builder.Build());

        var result = await ReadAsync<JsonElement>(response);
        Assert.Equal(40, result.GetProperty("importedCount").GetInt32());
        Assert.Equal(_registry.Own.Id, result.GetProperty("comparsaId").GetGuid());
        Assert.True(response.Headers.CacheControl?.NoStore);

        var imported = await ImportedAsync();
        Assert.Equal(40, imported.Count);
        Assert.All(imported, arquebusier =>
        {
            Assert.Equal(_registry.Own.Id, arquebusier.ComparsaId);
            Assert.Equal(ArquebusierStatus.Active, arquebusier.Status);
            Assert.Equal(LicenseType.Ae, arquebusier.LicenseType);
            Assert.Equal(new DateOnly(2024, 3, 10), arquebusier.LicenseIssuedOn);
            Assert.Equal(new DateOnly(2029, 3, 10), arquebusier.LicenseExpiresOn);
            Assert.Equal(new DateOnly(2023, 11, 4), arquebusier.TrainingCompletedOn);
            Assert.Empty(arquebusier.OwnedWeapons);
        });

        using var list = await _registry.FiringChief.GetAsync(new Uri($"/api/arquebusiers?comparsaId={_registry.Own.Id}", UriKind.Relative), Token);
        var rows = await ReadAsync<JsonElement>(list);
        Assert.Equal(40, rows.EnumerateArray().Count(row => row.GetProperty("lastName").GetString()!.StartsWith("Importado", StringComparison.Ordinal)));
        Assert.All(rows.EnumerateArray(), row => Assert.False(row.GetProperty("hasIdPhoto").GetBoolean()));
    }

    [Fact]
    public async Task A_thousand_rows_are_imported_in_one_go()
    {
        var builder = ImportWorkbookBuilder.Template();
        for (var row = 0; row < ImportWorkbookReader.MaxRows; row++)
        {
            builder.ValidRow();
        }

        using var response = await ImportRequests.ImportAsync(_registry.Admin, _registry.Own.Id, builder.Build());

        Assert.Equal(ImportWorkbookReader.MaxRows, (await ReadAsync<JsonElement>(response)).GetProperty("importedCount").GetInt32());
        Assert.Equal(ImportWorkbookReader.MaxRows, (await ImportedAsync()).Count);
        Assert.Equal(ImportWorkbookReader.MaxRows, (await _registry.Host.AuditEntriesAsync(ArquebusierAdministration.RegisteredAction)).Count);
    }

    [Fact]
    public async Task One_row_with_an_error_stops_the_import_and_returns_the_report()
    {
        var builder = ImportWorkbookBuilder.Template();
        for (var row = 0; row < 39; row++)
        {
            builder.ValidRow();
        }

        builder.Row(899990d, "Errónea", "Arcabucera", "12345678A", new DateTime(1990, 5, 1), "Mujer");

        using var response = await ImportRequests.ImportAsync(_registry.Admin, _registry.Own.Id, builder.Build());

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "arquebusierImport.rowErrors");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var report = body.RootElement.GetProperty("report");
        Assert.Equal(1, report.GetProperty("errorRowCount").GetInt32());
        Assert.Equal(41, report.GetProperty("rows")[0].GetProperty("rowNumber").GetInt32());
        Assert.Equal("checkLetter", report.GetProperty("rows")[0].GetProperty("errors")[0].GetProperty("reason").GetString());
        Assert.Empty(await ImportedAsync());
        Assert.Empty(await _registry.Host.AuditEntriesAsync(ArquebusierImporter.ImportedAction));
        Assert.Empty(await _registry.Host.AuditEntriesAsync(ArquebusierAdministration.RegisteredAction));
    }

    [Fact]
    public async Task A_registration_made_after_the_check_is_found_at_the_import()
    {
        var builder = ImportWorkbookBuilder.Template().ValidRow().ValidRow();
        var content = builder.Build();
        using (var preview = await ImportRequests.PreviewAsync(_registry.Admin, _registry.Own.Id, content))
        {
            Assert.Equal(0, (await ReadAsync<JsonElement>(preview)).GetProperty("errorRowCount").GetInt32());
        }

        var sheet = ImportWorkbookReader.Read(content, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).Sheet!;
        var (nationalId, _) = ImportCellReader.Identity(sheet.Rows[1]);
        await _registry.RegisterAsync(_registry.Other.Id, body => body["nationalId"] = nationalId);

        using var response = await ImportRequests.ImportAsync(_registry.Admin, _registry.Own.Id, content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "arquebusierImport.rowErrors");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var row = body.RootElement.GetProperty("report").GetProperty("rows")[0];
        Assert.Equal(3, row.GetProperty("rowNumber").GetInt32());
        Assert.Equal("taken", row.GetProperty("errors")[0].GetProperty("reason").GetString());
        Assert.Empty(await ImportedAsync());
    }

    [Fact]
    public async Task Importing_the_same_file_twice_changes_nothing_the_second_time()
    {
        var content = ImportWorkbookBuilder.Template().ValidRow().ValidRow().ValidRow().Build();
        using (var first = await ImportRequests.ImportAsync(_registry.Admin, _registry.Own.Id, content))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var second = await ImportRequests.ImportAsync(_registry.Admin, _registry.Own.Id, content);

        await AssertProblemAsync(second, HttpStatusCode.BadRequest, "arquebusierImport.rowErrors");
        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync(Token));
        Assert.Equal(3, body.RootElement.GetProperty("report").GetProperty("errorRowCount").GetInt32());
        Assert.Equal(3, (await ImportedAsync()).Count);
    }

    [Fact]
    public async Task Unreadable_files_and_missing_fields_are_refused_like_a_check()
    {
        using var empty = await ImportRequests.ImportAsync(_registry.Admin, _registry.Own.Id, ImportWorkbookBuilder.Template().Build());
        using var missing = await ImportRequests.ImportAsync(_registry.Admin, comparsaId: null, workbook: null);
        using var inactive = await ImportRequests.ImportAsync(_registry.Admin, _registry.Inactive.Id, ImportWorkbookBuilder.Template().ValidRow().Build());
        using var unknown = await ImportRequests.ImportAsync(_registry.Admin, Guid.CreateVersion7(), ImportWorkbookBuilder.Template().ValidRow().Build());

        Assert.Equal("empty", (await ErrorsAsync(empty))["file"]);
        Assert.Equal(["comparsaId", "file"], (await ErrorsAsync(missing)).Keys.Order());
        await AssertProblemAsync(inactive, HttpStatusCode.Conflict, "arquebusiers.comparsaInactive");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "arquebusiers.comparsaNotFound");
        Assert.Empty(await ImportedAsync());
    }

    [Fact]
    public async Task A_FiringChief_cannot_import()
    {
        using var response = await ImportRequests.ImportAsync(_registry.FiringChief, _registry.Own.Id, ImportWorkbookBuilder.Template().ValidRow().Build());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await ImportedAsync());
    }

    [Fact]
    public async Task An_import_is_audited_without_personal_values()
    {
        var content = ImportWorkbookBuilder.Template().ValidRow().ValidRow().ValidRow().Build();

        using var response = await ImportRequests.ImportAsync(_registry.Admin, _registry.Own.Id, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var imported = await ImportedAsync();
        var registered = await _registry.Host.AuditEntriesAsync(ArquebusierAdministration.RegisteredAction);
        Assert.Equal(3, registered.Count);
        Assert.Equal(imported.Select(a => a.Id.ToString()).Order(), registered.Select(e => e.EntityId).Order());
        Assert.All(registered, entry =>
        {
            Assert.Equal((_registry.AdminId, _registry.Own.Id), (entry.ActorUserId, entry.ComparsaId));
            Assert.Equal("import", JsonDocument.Parse(entry.Data!).RootElement.GetProperty("source").GetString());
        });

        var summary = Assert.Single(await _registry.Host.AuditEntriesAsync(ArquebusierImporter.ImportedAction));
        Assert.Equal((_registry.AdminId, _registry.Own.Id, "Comparsa", _registry.Own.Id.ToString()), (summary.ActorUserId, summary.ComparsaId, summary.EntityType, summary.EntityId));
        Assert.Equal(3, JsonDocument.Parse(summary.Data!).RootElement.GetProperty("count").GetInt32());

        var data = string.Join('\n', registered.Append(summary).Select(entry => entry.Data));
        foreach (var arquebusier in imported)
        {
            foreach (var value in new[] { arquebusier.NationalId, arquebusier.FederationId.ToString(System.Globalization.CultureInfo.InvariantCulture), arquebusier.LastName, arquebusier.FirstName, "1990-05-01", "2024-03-10", "arcabuceros.xlsx" })
            {
                Assert.DoesNotContain(value, data, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task A_row_held_by_an_unfinished_transaction_makes_the_import_busy()
    {
        var content = ImportWorkbookBuilder.Template().ValidRow().Build();
        var sheet = ImportWorkbookReader.Read(content, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).Sheet!;
        var (nationalId, _) = ImportCellReader.Identity(sheet.Rows[0]);

        await using var scope = _registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        await using var holder = await db.Database.BeginTransactionAsync(Token);
        var blocking = RegistryData.NewArquebusier(_registry.Other.Id);
        blocking.NationalId = nationalId!;
        db.Arquebusiers.Add(blocking);
        await db.SaveChangesAsync(Token);

        using var response = await ImportRequests.ImportAsync(_registry.Admin, _registry.Own.Id, content);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "registry.busy");
        await holder.RollbackAsync(Token);
        Assert.Empty(await ImportedAsync());
    }

    [Fact]
    public async Task What_the_cells_say_is_what_is_stored()
    {
        var (_, reserveFederationId) = RegistryData.NextIdentity();
        var (expiredNationalId, expiredFederationId) = RegistryData.NextIdentity();
        var builder = ImportWorkbookBuilder.Template()
            .Row(
                (double)reserveFederationId, "Sintético Reserva", "Arcabucero", "1234567-l", "01/05/1990", "Home",
                "Reserva@Example.Test", 600123456d, "Reserva", "AE", null, null, null)
            .Row(
                (double)expiredFederationId, "Sintética Caducada", "Arcabucera", expiredNationalId, new DateTime(1990, 5, 1), "Female",
                null, " 600 123 457 ", null, "A-PROF", new DateTime(2020, 1, 10), null, null);

        using var response = await ImportRequests.ImportAsync(_registry.Admin, _registry.Own.Id, builder.Build());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var imported = await ImportedAsync();
        var reserve = Assert.Single(imported, a => a.FederationId == reserveFederationId);
        Assert.Equal("01234567L", reserve.NationalId);
        Assert.Equal(new DateOnly(1990, 5, 1), reserve.BirthDate);
        Assert.Equal(Gender.Male, reserve.Gender);
        Assert.Equal("reserva@example.test", reserve.Email);
        Assert.Equal("600123456", reserve.Phone);
        Assert.Equal(ArquebusierStatus.Reserve, reserve.Status);
        Assert.Equal(LicenseType.Ae, reserve.LicenseType);
        Assert.True(reserve.LicensePending);
        Assert.Null(reserve.LicenseIssuedOn);
        Assert.Null(reserve.TrainingCompletedOn);

        // An expired license and no course are warnings: they never stop the import.
        var expired = Assert.Single(imported, a => a.FederationId == expiredFederationId);
        Assert.Equal(Gender.Female, expired.Gender);
        Assert.Equal("600 123 457", expired.Phone);
        Assert.Equal(ArquebusierStatus.Active, expired.Status);
        Assert.Equal(LicenseType.AProf, expired.LicenseType);
        Assert.Equal(new DateOnly(2021, 1, 10), expired.LicenseExpiresOn);
    }

    [Fact]
    public async Task A_federation_id_registered_meanwhile_stops_the_import()
    {
        var existing = await _registry.RegisterAsync(_registry.Other.Id);
        var (nationalId, _) = RegistryData.NextIdentity();
        var builder = ImportWorkbookBuilder.Template().ValidRow()
            .Row((double)existing.GetProperty("federationId").GetInt32(), "Sintética", "Arcabucera", nationalId, new DateTime(1990, 5, 1), "Mujer");

        using var response = await ImportRequests.ImportAsync(_registry.Admin, _registry.Own.Id, builder.Build());

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "arquebusierImport.rowErrors");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var error = body.RootElement.GetProperty("report").GetProperty("rows")[0].GetProperty("errors")[0];
        Assert.Equal(("federationId", "taken"), (error.GetProperty("field").GetString(), error.GetProperty("reason").GetString()));
        Assert.Empty(await ImportedAsync());
    }

    [Fact]
    public async Task An_existing_arquebusier_is_never_changed()
    {
        var existing = await _registry.RegisterAsync(_registry.Own.Id);
        var before = existing.GetProperty("version").GetUInt32();
        var builder = ImportWorkbookBuilder.Template()
            .Row((double)existing.GetProperty("federationId").GetInt32(), "Otro Apellido", "Otro", existing.GetProperty("nationalId").GetString(), new DateTime(1980, 1, 1), "Hombre");

        using var response = await ImportRequests.ImportAsync(_registry.Admin, _registry.Own.Id, builder.Build());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var detail = await _registry.Admin.GetAsync(new Uri($"/api/arquebusiers/{existing.GetProperty("id").GetGuid()}", UriKind.Relative), Token);
        var after = await ReadAsync<JsonElement>(detail);
        Assert.Equal(before, after.GetProperty("version").GetUInt32());
        Assert.Equal(existing.GetProperty("lastName").GetString(), after.GetProperty("lastName").GetString());
    }

    private async Task<List<Arquebusier>> ImportedAsync()
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>().Arquebusiers
            .AsNoTracking().Include(a => a.OwnedWeapons)
            .Where(a => a.ComparsaId == _registry.Own.Id)
            .ToListAsync(Token);
    }
}

/// <summary>
/// Spec "All-or-nothing import": two imports of the same people at the same time store one, and the
/// other is refused without storing anything; a registration that wins the race at commit time
/// makes the import a conflict.
/// </summary>
public sealed class ImportRaceTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Concurrent_imports_of_the_same_people_store_one()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var builder = ImportWorkbookBuilder.Template();
        for (var row = 0; row < 200; row++)
        {
            builder.ValidRow();
        }

        var content = builder.Build();

        var responses = await Task.WhenAll(
            ImportRequests.ImportAsync(registry.Admin, registry.Own.Id, content),
            ImportRequests.ImportAsync(registry.Admin, registry.Own.Id, content));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var refused = Assert.Single(responses, response => response.StatusCode != HttpStatusCode.OK);
            await AssertProblemAsync(refused, HttpStatusCode.BadRequest, "arquebusierImport.rowErrors");
            Assert.Single(await registry.Host.AuditEntriesAsync(ArquebusierImporter.ImportedAction));
            Assert.Equal(200, (await registry.Host.AuditEntriesAsync(ArquebusierAdministration.RegisteredAction)).Count);
        }
        finally
        {
            Array.ForEach(responses, response => response.Dispose());
        }
    }

    [Fact]
    public async Task A_registration_committed_while_the_import_inserts_makes_it_a_conflict()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var content = ImportWorkbookBuilder.Template().ValidRow().Build();
        var sheet = ImportWorkbookReader.Read(content, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).Sheet!;
        var (nationalId, _) = ImportCellReader.Identity(sheet.Rows[0]);

        // Another request has inserted the same nationalId but not committed yet: the import does
        // not see it when it checks, and its insert waits for that transaction.
        await using var scope = registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        await using var holder = await db.Database.BeginTransactionAsync(Token);
        var other = RegistryData.NewArquebusier(registry.Other.Id);
        other.NationalId = nationalId!;
        db.Arquebusiers.Add(other);
        await db.SaveChangesAsync(Token);

        var import = ImportRequests.ImportAsync(registry.Admin, registry.Own.Id, content);
        await WaitForABlockedInsertAsync(registry);
        await holder.CommitAsync(Token);
        using var response = await import;

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "arquebusierImport.conflict");
        Assert.Empty(await registry.Host.AuditEntriesAsync(ArquebusierImporter.ImportedAction));
        Assert.Empty(await ImportedAsync(registry));
    }

    [Fact]
    public async Task A_federation_id_committed_while_the_import_inserts_makes_it_a_conflict()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var content = ImportWorkbookBuilder.Template().ValidRow().Build();
        var sheet = ImportWorkbookReader.Read(content, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).Sheet!;
        var (_, federationId) = ImportCellReader.Identity(sheet.Rows[0]);

        await using var scope = registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        await using var holder = await db.Database.BeginTransactionAsync(Token);
        var other = RegistryData.NewArquebusier(registry.Other.Id);
        other.FederationId = federationId!.Value;
        db.Arquebusiers.Add(other);
        await db.SaveChangesAsync(Token);

        var import = ImportRequests.ImportAsync(registry.Admin, registry.Own.Id, content);
        await WaitForABlockedInsertAsync(registry);
        await holder.CommitAsync(Token);
        using var response = await import;

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "arquebusierImport.conflict");
        Assert.Empty(await ImportedAsync(registry));
    }

    [Fact]
    public async Task An_import_waiting_too_long_for_another_import_is_busy()
    {
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        await using var scope = registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        await using var holder = await db.Database.BeginTransactionAsync(Token);
        await db.LockImportsAsync(Token);

        using var response = await ImportRequests.ImportAsync(registry.Admin, registry.Own.Id, ImportWorkbookBuilder.Template().ValidRow().Build());

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "registry.busy");
        await holder.RollbackAsync(Token);
        Assert.Empty(await ImportedAsync(registry));
    }

    /// <summary>Waits until a session of this database waits for a lock, as the import does on the held insert.</summary>
    private static async Task WaitForABlockedInsertAsync(RegistryTestHost registry)
    {
        await using var scope = registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            // Only this test's database: other test classes run in parallel on their own copies.
            var waiting = await db.Database
                .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND pid <> pg_backend_pid()")
                .SingleAsync(Token);
            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(50, Token);
        }

        Assert.Fail("The import never waited for the held row.");
    }

    private static async Task<List<Arquebusier>> ImportedAsync(RegistryTestHost registry)
    {
        await using var scope = registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>().Arquebusiers
            .AsNoTracking().Where(a => a.ComparsaId == registry.Own.Id).ToListAsync(Token);
    }
}
