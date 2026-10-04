using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.EditionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Editions;

/// <summary>Spec "Calendar milestones" and its audit, with the <c>notify</c> flag and the reminder contract (add-notifications).</summary>
public sealed class CalendarMilestoneTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.hitos@example.test", UserRole.Admin));
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_admin_adds_a_milestone_and_it_is_listed_in_date_order()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        await AddAsync(id, "2031-03-01", "Reparto de pólvora sintético");
        await AddAsync(id, "2030-11-30", "Plazo de nuevos arcabuceros");

        using var response = await AddAsync(id, "2030-11-30", "  Curso de formación sintético  ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var milestone = await ReadAsync<MilestoneJson>(response);
        Assert.Equal((new DateOnly(2030, 11, 30), "Curso de formación sintético", false), (milestone.Date, milestone.Title, milestone.Notify));
        Assert.Equal(
            ["Curso de formación sintético", "Plazo de nuevos arcabuceros", "Reparto de pólvora sintético"],
            (await _admin.GetEditionAsync(id)).Milestones.Select(m => m.Title));
        var entry = (await _host.AuditEntriesAsync("CalendarMilestoneAdded")).Single(e => e.EntityId == milestone.Id.ToString());
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(id, data.RootElement.GetProperty("editionId").GetGuid());
    }

    [Fact]
    public async Task An_admin_edits_and_removes_a_milestone()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        using var created = await AddAsync(id, "2030-11-30", "Plazo sintético");
        var milestone = await ReadAsync<MilestoneJson>(created);

        using var edited = await _admin.PutAsJsonAsync(
            $"/api/editions/{id}/milestones/{milestone.Id}", new { date = "2030-12-05", title = "Plazo sintético ampliado" }, TestContext.Current.CancellationToken);
        using var unchanged = await _admin.PutAsJsonAsync(
            $"/api/editions/{id}/milestones/{milestone.Id}", new { date = "2030-12-05", title = "Plazo sintético ampliado" }, TestContext.Current.CancellationToken);
        using var removed = await _admin.DeleteAsync($"/api/editions/{id}/milestones/{milestone.Id}", TestContext.Current.CancellationToken);

        var saved = await ReadAsync<MilestoneJson>(edited);
        Assert.Equal((new DateOnly(2030, 12, 5), "Plazo sintético ampliado"), (saved.Date, saved.Title));
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty((await _admin.GetEditionAsync(id)).Milestones);
        Assert.Single(await _host.AuditEntriesAsync("CalendarMilestoneUpdated"));
        Assert.Single(await _host.AuditEntriesAsync("CalendarMilestoneRemoved"));
    }

    [Fact]
    public async Task A_milestone_added_with_a_reminder_keeps_it_and_the_audit_records_it()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);

        using var response = await _admin.PostAsync($"/api/editions/{id}/milestones", new { date = "2030-11-15", title = "Curso sintético", notify = true });

        var milestone = await ReadAsync<MilestoneJson>(response);
        Assert.True(milestone.Notify);
        Assert.True(Assert.Single((await _admin.GetEditionAsync(id)).Milestones).Notify);
        var entry = (await _host.AuditEntriesAsync("CalendarMilestoneAdded")).Single(e => e.EntityId == milestone.Id.ToString());
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.True(data.RootElement.GetProperty("notify").GetBoolean());
    }

    [Fact]
    public async Task An_edit_without_notify_keeps_it_and_turning_it_off_is_audited()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        using var created = await _admin.PostAsync($"/api/editions/{id}/milestones", new { date = "2030-11-15", title = "Curso sintético", notify = true });
        var milestone = await ReadAsync<MilestoneJson>(created);
        var url = $"/api/editions/{id}/milestones/{milestone.Id}";

        using var kept = await _admin.PutAsJsonAsync(url, new { date = "2030-11-16", title = "Curso sintético" }, TestContext.Current.CancellationToken);
        using var off = await _admin.PutAsJsonAsync(url, new { date = "2030-11-16", title = "Curso sintético", notify = false }, TestContext.Current.CancellationToken);

        Assert.True((await ReadAsync<MilestoneJson>(kept)).Notify);
        Assert.False((await ReadAsync<MilestoneJson>(off)).Notify);
        var updates = await _host.AuditEntriesAsync("CalendarMilestoneUpdated");
        Assert.Equal(2, updates.Count);
        var turnedOff = updates.Select(e => JsonDocument.Parse(e.Data!)).Single(d => !d.RootElement.GetProperty("current").GetProperty("notify").GetBoolean());
        Assert.True(turnedOff.RootElement.GetProperty("previous").GetProperty("notify").GetBoolean());
    }

    [Fact]
    public async Task A_notify_that_is_not_a_boolean_is_rejected()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);

        using var response = await _admin.PostAsync($"/api/editions/{id}/milestones", new { date = "2030-11-15", title = "Curso sintético", notify = "yes" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await _admin.GetEditionAsync(id)).Milestones);
    }

    [Fact]
    public async Task A_firing_chief_reads_the_reminder_flag()
    {
        var (id, _) = await _admin.CreateCompleteEditionAsync(2031);
        await AddReminderAsync(id, "2030-11-15", "Curso sintético", notify: true);
        await _host.SetStateAsync(id, EditionStatus.InProgress);
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.hitos.aviso@example.test"));

        Assert.True(Assert.Single((await chief.GetEditionAsync(id)).Milestones).Notify);
    }

    [Fact]
    public async Task Existing_milestones_default_to_no_reminder()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();

        var column = await db.Database.SqlQuery<string>(
                $"SELECT column_default || ' ' || is_nullable AS \"Value\" FROM information_schema.columns WHERE table_schema = 'editions' AND table_name = 'calendar_milestones' AND column_name = 'notify'")
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("false NO", column);
    }

    [Fact]
    public async Task The_directory_lists_milestones_to_notify_of_editions_not_closed_within_the_range()
    {
        var (draft, _) = await _admin.CreateCompleteEditionAsync(2031);
        var (inProgress, _) = await _admin.CreateCompleteEditionAsync(2030);
        var (closed, _) = await _admin.CreateCompleteEditionAsync(2029);
        await _host.SetStateAsync(inProgress, EditionStatus.InProgress);
        await _host.SetStateAsync(closed, EditionStatus.Closed);
        var first = await AddReminderAsync(draft, "2030-11-20", "Curso sintético", notify: true);
        var last = await AddReminderAsync(inProgress, "2030-11-27", "Último día sintético", notify: true);
        await AddReminderAsync(draft, "2030-11-21", "Sin aviso", notify: false);
        await AddReminderAsync(draft, "2030-11-19", "Antes del rango", notify: true);
        await AddReminderAsync(draft, "2030-11-28", "Fuera del rango", notify: true);
        await AddReminderAsync(closed, "2030-11-20", "Edición cerrada", notify: true);
        await using var scope = _host.Services.CreateAsyncScope();

        var found = await scope.ServiceProvider.GetRequiredService<IEditionDirectory>()
            .ListMilestonesToNotifyAsync(new DateOnly(2030, 11, 20), new DateOnly(2030, 11, 27), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new MilestoneFacts(first, draft, 2031, EditionStatus.Draft, new DateOnly(2030, 11, 20), "Curso sintético"),
                new MilestoneFacts(last, inProgress, 2030, EditionStatus.InProgress, new DateOnly(2030, 11, 27), "Último día sintético"),
            ],
            found.OrderBy(m => m.Date));
    }

    [Fact]
    public async Task The_snapshot_carries_the_planned_close_date_on_both_reading_paths()
    {
        var (planned, _) = await _admin.CreateCompleteEditionAsync(2031);
        var (unplanned, _) = await _admin.CreateEditionAsync(2032);
        await using var scope = _host.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IEditionDirectory>();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();

        var found = await directory.FindAsync(planned, TestContext.Current.CancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var locked = await directory.ReadForOrderWriteAsync(planned, transaction.GetDbTransaction(), TestContext.Current.CancellationToken);
        var lockedUnplanned = await directory.ReadForOrderWriteAsync(unplanned, transaction.GetDbTransaction(), TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2031, 2, 10), found?.OrdersCloseOn);
        Assert.Equal(found?.OrdersCloseOn, locked?.OrdersCloseOn);
        Assert.NotNull(lockedUnplanned);
        Assert.Null(lockedUnplanned.OrdersCloseOn);
    }

    [Theory]
    [InlineData("   ", "required")]
    [InlineData("Plazo\ncon salto", "invalid")]
    [InlineData(null, "required")]
    public async Task An_invalid_title_is_named(string? title, string reason)
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);

        using var response = await _admin.PostAsync($"/api/editions/{id}/milestones", new { date = "2030-11-30", title });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(reason, (await ErrorsAsync(response))["title"]);
    }

    [Fact]
    public async Task A_title_too_long_or_a_bad_date_is_named()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);

        using var response = await _admin.PostAsync($"/api/editions/{id}/milestones", new { date = "30/11/2030", title = new string('a', 101) });

        Assert.Equal(new Dictionary<string, string> { ["date"] = "invalid", ["title"] = "tooLong" }, await ErrorsAsync(response));
    }

    [Fact]
    public async Task The_fifty_first_milestone_is_blocking()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        for (var i = 0; i < 50; i++)
        {
            using var added = await AddAsync(id, "2031-01-01", $"Hito sintético {i:00}");
            added.EnsureSuccessStatusCode();
        }

        using var response = await AddAsync(id, "2031-01-01", "Hito de más");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "editions.tooManyMilestones");
    }

    [Fact]
    public async Task Admins_manage_milestones_in_any_status()
    {
        var (id, _) = await _admin.CreateCompleteEditionAsync(2031);
        await _host.SetStateAsync(id, EditionStatus.Closed);

        using var response = await AddAsync(id, "2031-05-01", "Balance sintético");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_editions_and_milestones_are_not_found()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        var (other, _) = await _admin.CreateEditionAsync(2032);
        using var created = await AddAsync(other, "2031-11-30", "Hito de otra edición");
        var foreign = await ReadAsync<MilestoneJson>(created);

        using var edition = await AddAsync(Guid.CreateVersion7(), "2030-11-30", "Hito");
        using var edit = await _admin.PutAsJsonAsync($"/api/editions/{id}/milestones/{foreign.Id}", new { date = "2030-11-30", title = "Hito" }, TestContext.Current.CancellationToken);
        using var delete = await _admin.DeleteAsync($"/api/editions/{id}/milestones/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(edition, HttpStatusCode.NotFound, "editions.notFound");
        await AssertProblemAsync(edit, HttpStatusCode.NotFound, "editions.milestoneNotFound");
        await AssertProblemAsync(delete, HttpStatusCode.NotFound, "editions.milestoneNotFound");
    }

    [Fact]
    public async Task A_firing_chief_cannot_manage_milestones()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        using var created = await AddAsync(id, "2030-11-30", "Plazo sintético");
        var milestone = await ReadAsync<MilestoneJson>(created);
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.hitos@example.test"));

        using var add = await chief.PostAsync($"/api/editions/{id}/milestones", new { date = "2030-11-30", title = "Hito" });
        using var edit = await chief.PutAsJsonAsync($"/api/editions/{id}/milestones/{milestone.Id}", new { date = "2030-11-30", title = "Hito" }, TestContext.Current.CancellationToken);
        using var delete = await chief.DeleteAsync($"/api/editions/{id}/milestones/{milestone.Id}", TestContext.Current.CancellationToken);

        Assert.All([add, edit, delete], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    private async Task<Guid> AddReminderAsync(Guid editionId, string date, string title, bool notify)
    {
        using var response = await _admin.PostAsync($"/api/editions/{editionId}/milestones", new { date, title, notify });
        return (await ReadAsync<MilestoneJson>(response)).Id;
    }

    private Task<HttpResponseMessage> AddAsync(Guid editionId, string date, string title) =>
        _admin.PostAsync($"/api/editions/{editionId}/milestones", new { date, title });
}
