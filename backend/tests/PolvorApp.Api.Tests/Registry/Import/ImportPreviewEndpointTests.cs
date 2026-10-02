using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Import;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry.Import;

/// <summary>
/// Spec "Import validation report (UC-09)" through the API: Admins check a file for an active
/// comparsa; duplicates are found in the file and across every comparsa; nothing is stored or audited.
/// </summary>
public sealed class ImportPreviewEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _registry = await RegistryTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_clean_file_is_reported_and_nothing_is_stored()
    {
        var builder = ImportWorkbookBuilder.Template();
        for (var row = 0; row < 40; row++)
        {
            builder.ValidRow();
        }

        var arquebusiers = await CountAsync();
        var audits = await AuditCountAsync();
        using var response = await ImportRequests.PreviewAsync(_registry.Admin, _registry.Own.Id, builder.Build());

        var report = await ReadAsync<JsonElement>(response);
        Assert.Equal(40, report.GetProperty("rowCount").GetInt32());
        Assert.Equal(40, report.GetProperty("validCount").GetInt32());
        Assert.Equal(0, report.GetProperty("errorRowCount").GetInt32());
        Assert.Equal(0, report.GetProperty("rows").GetArrayLength());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(arquebusiers, await CountAsync());
        Assert.Equal(audits, await AuditCountAsync());
    }

    [Fact]
    public async Task A_value_registered_in_another_comparsa_is_taken_without_saying_whose()
    {
        var existing = await _registry.RegisterAsync(_registry.Other.Id);
        var nationalId = existing.GetProperty("nationalId").GetString()!;
        var builder = ImportWorkbookBuilder.Template()
            .Row(899999d, "Sintética", "Arcabucera", nationalId, new DateTime(1990, 5, 1), "Mujer");

        using var response = await ImportRequests.PreviewAsync(_registry.Admin, _registry.Own.Id, builder.Build());

        var body = await response.Content.ReadAsStringAsync(Token);
        var row = JsonDocument.Parse(body).RootElement.GetProperty("rows")[0];
        Assert.Equal("nationalId", row.GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal("taken", row.GetProperty("errors")[0].GetProperty("reason").GetString());
        Assert.DoesNotContain(_registry.Other.Name, body, StringComparison.Ordinal);
        Assert.DoesNotContain(existing.GetProperty("lastName").GetString()!, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_federation_id_registered_in_another_comparsa_is_taken_without_saying_whose()
    {
        var existing = await _registry.RegisterAsync(_registry.Other.Id);
        var (nationalId, _) = RegistryData.NextIdentity();
        var builder = ImportWorkbookBuilder.Template()
            .Row((double)existing.GetProperty("federationId").GetInt32(), "Sintética", "Arcabucera", nationalId, new DateTime(1990, 5, 1), "Mujer");

        using var response = await ImportRequests.PreviewAsync(_registry.Admin, _registry.Own.Id, builder.Build());

        var body = await response.Content.ReadAsStringAsync(Token);
        var error = Assert.Single(JsonDocument.Parse(body).RootElement.GetProperty("rows")[0].GetProperty("errors").EnumerateArray());
        Assert.Equal(("federationId", "taken"), (error.GetProperty("field").GetString(), error.GetProperty("reason").GetString()));
        Assert.DoesNotContain(_registry.Other.Name, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_values_are_errors_on_each_row()
    {
        var builder = ImportWorkbookBuilder.Template()
            .Row(899998d, "Uno", "Arcabucero", "00000011B", new DateTime(1990, 5, 1), "Hombre")
            .Row(899998d, "Dos", "Arcabucero", "00000012N", new DateTime(1990, 5, 1), "Hombre");

        using var response = await ImportRequests.PreviewAsync(_registry.Admin, _registry.Own.Id, builder.Build());

        var rows = (await ReadAsync<JsonElement>(response)).GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Contains(
            row.GetProperty("errors").EnumerateArray(),
            error => error.GetProperty("field").GetString() == "federationId" && error.GetProperty("reason").GetString() == "duplicateInFile"));
    }

    [Fact]
    public async Task A_thousand_rows_are_checked()
    {
        using var response = await ImportRequests.PreviewAsync(_registry.Admin, _registry.Own.Id, Rows(ImportWorkbookReader.MaxRows));

        Assert.Equal(ImportWorkbookReader.MaxRows, (await ReadAsync<JsonElement>(response)).GetProperty("validCount").GetInt32());
    }

    [Fact]
    public async Task Warnings_are_reported_as_codes_and_do_not_block()
    {
        // 17 years old on the test clock, license expired a year ago, no course.
        var today = _registry.Today;
        var builder = ImportWorkbookBuilder.Template().Row(
            899997d, "Joven", "Arcabucero", "00000013J", today.AddYears(-17).ToDateTime(TimeOnly.MinValue), "Hombre", null, null, null, "AE",
            today.AddYears(-6).ToDateTime(TimeOnly.MinValue), today.AddYears(-1).ToDateTime(TimeOnly.MinValue), null);

        using var response = await ImportRequests.PreviewAsync(_registry.Admin, _registry.Own.Id, builder.Build());

        var report = await ReadAsync<JsonElement>(response);
        var row = report.GetProperty("rows")[0];
        Assert.Equal(["LICENSE_EXPIRED", "COURSE_MISSING", "UNDER_AGE"], row.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));
        Assert.Equal(0, row.GetProperty("errors").GetArrayLength());
        Assert.Equal(1, report.GetProperty("validCount").GetInt32());
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("empty")]
    [InlineData("tooManyRows")]
    public async Task A_file_that_cannot_be_read_names_the_reason(string reason)
    {
        var content = reason switch
        {
            "invalid" => Encoding.ASCII.GetBytes("%PDF-1.7 not a workbook"),
            "empty" => ImportWorkbookBuilder.Template().Build(),
            _ => Rows(ImportWorkbookReader.MaxRows + 1),
        };

        using var response = await ImportRequests.PreviewAsync(_registry.Admin, _registry.Own.Id, content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(reason, (await ErrorsAsync(response))["file"]);
    }

    [Fact]
    public async Task A_missing_required_column_is_named()
    {
        var builder = ImportWorkbookBuilder.WithHeaders("ID Unión", "Apellidos", "Nombre", "Fecha de nacimiento", "Género")
            .Row(900001d, "Sintética", "Arcabucera", new DateTime(1990, 5, 1), "Mujer");

        using var response = await ImportRequests.PreviewAsync(_registry.Admin, _registry.Own.Id, builder.Build());

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal("missingColumns", body.RootElement.GetProperty("errors").GetProperty("file").GetString());
        Assert.Equal(["nationalId"], body.RootElement.GetProperty("columns").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public async Task A_file_over_two_megabytes_is_too_large()
    {
        using var response = await ImportRequests.PreviewAsync(_registry.Admin, _registry.Own.Id, new byte[(int)ImportWorkbookReader.MaxFileBytes + 1]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("tooLarge", (await ErrorsAsync(response))["file"]);
    }

    [Fact]
    public async Task The_comparsa_and_the_file_are_required()
    {
        using var response = await ImportRequests.PreviewAsync(_registry.Admin, comparsaId: null, workbook: null);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        var errors = await ErrorsAsync(response);
        Assert.Equal("required", errors["comparsaId"]);
        Assert.Equal("required", errors["file"]);
    }

    [Fact]
    public async Task An_unknown_comparsa_is_not_found()
    {
        using var response = await ImportRequests.PreviewAsync(_registry.Admin, Guid.CreateVersion7(), ImportWorkbookBuilder.Template().ValidRow().Build());

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "arquebusiers.comparsaNotFound");
    }

    [Fact]
    public async Task An_inactive_comparsa_is_a_conflict()
    {
        using var response = await ImportRequests.PreviewAsync(_registry.Admin, _registry.Inactive.Id, ImportWorkbookBuilder.Template().ValidRow().Build());

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "arquebusiers.comparsaInactive");
    }

    [Fact]
    public async Task A_FiringChief_is_refused_even_for_their_own_comparsa()
    {
        using var response = await ImportRequests.PreviewAsync(_registry.FiringChief, _registry.Own.Id, ImportWorkbookBuilder.Template().ValidRow().Build());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_check_without_the_antiforgery_header_is_refused()
    {
        var client = await _registry.Host.SignInAsync(await _registry.Host.CreateUserAsync("admin.importacion.csrf@example.test", UserRole.Admin));
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using var response = await ImportRequests.PreviewAsync(client, _registry.Own.Id, ImportWorkbookBuilder.Template().ValidRow().Build());

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "antiforgery.invalid");
    }

    [Fact]
    public void The_endpoint_limits_its_body_and_uses_the_spreadsheet_rate_limit()
    {
        var preview = _registry.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText?.EndsWith("/import/preview", StringComparison.Ordinal) == true);

        Assert.Equal(ImportWorkbookReader.MaxFileBytes + (64 * 1024), preview.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
        Assert.Equal(
            PolvorApp.SharedKernel.Security.RateLimitPolicies.SpreadsheetImports,
            preview.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName);
    }

    private static byte[] Rows(int count)
    {
        var builder = ImportWorkbookBuilder.Template();
        for (var row = 0; row < count; row++)
        {
            builder.ValidRow();
        }

        return builder.Build();
    }

    private async Task<int> CountAsync()
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>().Arquebusiers.CountAsync(Token);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Set<AuditEntry>().CountAsync(Token);
    }
}

/// <summary>Design D9: with every workbook-reading slot taken, a check is answered as busy.</summary>
public sealed class ImportBusyTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task With_every_reading_slot_taken_a_check_is_busy_until_one_frees_up()
    {
        await using var registry = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => services.AddSingleton(new ImportSlots(TimeSpan.FromMilliseconds(100))));
        var slots = registry.Services.GetRequiredService<ImportSlots>();
        var taken = new List<IDisposable>();
        for (var slot = 0; slot < ImportSlots.Count; slot++)
        {
            taken.Add((await slots.EnterAsync(TestContext.Current.CancellationToken))!);
        }

        try
        {
            using var response = await ImportRequests.PreviewAsync(registry.Admin, registry.Own.Id, ImportWorkbookBuilder.Template().ValidRow().Build());

            await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "registry.busy");
        }
        finally
        {
            taken.ForEach(slot => slot.Dispose());
        }

        using var freed = await ImportRequests.PreviewAsync(registry.Admin, registry.Own.Id, ImportWorkbookBuilder.Template().ValidRow().Build());
        Assert.Equal(HttpStatusCode.OK, freed.StatusCode);
    }
}

/// <summary>Design D9: checks are throttled per user by the <c>SpreadsheetImports</c> policy.</summary>
public sealed class ImportRateLimitTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Checks_are_throttled_per_user()
    {
        await using var host = await RegistryTestHost.StartAsync(
            postgres, mailpit, settings: new Dictionary<string, string?> { ["RateLimits:SpreadsheetImports:PermitLimit"] = "2" });
        using var second = await host.Host.SignInAsync(await host.Host.CreateUserAsync("admin.importacion.dos@example.test", UserRole.Admin));
        var workbook = ImportWorkbookBuilder.Template().ValidRow().Build();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await ImportRequests.PreviewAsync(host.Admin, host.Own.Id, workbook);
            statuses.Add(response.StatusCode);
        }

        using var other = await ImportRequests.PreviewAsync(second, host.Own.Id, workbook);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Imports_share_the_same_budget()
    {
        await using var host = await RegistryTestHost.StartAsync(
            postgres, mailpit, settings: new Dictionary<string, string?> { ["RateLimits:SpreadsheetImports:PermitLimit"] = "2" });
        var workbook = ImportWorkbookBuilder.Template().ValidRow().Build();

        using var check = await ImportRequests.PreviewAsync(host.Admin, host.Own.Id, workbook);
        using var import = await ImportRequests.ImportAsync(host.Admin, host.Own.Id, workbook);
        using var again = await ImportRequests.ImportAsync(host.Admin, host.Own.Id, workbook);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], new[] { check.StatusCode, import.StatusCode, again.StatusCode });
    }
}

/// <summary>NFR-12: checking a file logs counts and reasons, never a value from it.</summary>
public sealed class ImportLoggingTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Checking_a_file_with_errors_logs_no_personal_values()
    {
        var logs = new CapturingLoggerProvider();
        await using var registry = await RegistryTestHost.StartAsync(postgres, mailpit, services => services.AddSingleton<ILoggerProvider>(logs));
        var builder = ImportWorkbookBuilder.Template()
            .Row(877001d, "Apellidoregistrable", "Nombreregistrable", "12345678A", new DateTime(1990, 5, 1), "Mujer", "marcador@example.test", "600111222")
            .Row("#####", "Otroapellido", "Otronombre", "87654321X", new DateTime(2999, 5, 1), "Otro");

        using var response = await ImportRequests.PreviewAsync(registry.Admin, registry.Own.Id, builder.Build());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var logged = string.Join('\n', logs.Entries.Select(entry =>
            entry.Message + " " + entry.Exception + " " + JsonSerializer.Serialize(entry.State.ToDictionary(p => p.Key, p => p.Value?.ToString()))));
        Assert.Contains("checked", logged, StringComparison.Ordinal);
        foreach (var value in new[] { "877001", "Apellidoregistrable", "Nombreregistrable", "12345678A", "marcador@example.test", "600111222", "Otroapellido", "87654321X" })
        {
            Assert.DoesNotContain(value, logged, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(1990.ToString(CultureInfo.InvariantCulture) + "-05-01", logged, StringComparison.Ordinal);
    }
}
