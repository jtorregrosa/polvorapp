using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.Api.Tests.Persistence;

public sealed class Widget
{
    public int Id { get; set; }

    public required string Name { get; set; }
}

/// <summary>A module-like context: its own table plus the shared audit trail mapping.</summary>
public sealed class WidgetContext(DbContextOptions<WidgetContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    public static WidgetContext Create(NpgsqlDataSource dataSource) =>
        new(new DbContextOptionsBuilder<WidgetContext>().UseModuleDatabase(dataSource, "widgets").Options);

    /// <summary>A context that never connects: enough to inspect what <see cref="IAuditTrail"/> adds.</summary>
    public static WidgetContext Offline() =>
        new(new DbContextOptionsBuilder<WidgetContext>().UseModuleDatabase("Host=offline", "widgets").Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("widgets");
        modelBuilder.AddAuditTrail(ownsTable: true);
    }
}

/// <summary>Spec audit-privacy: "Audit trail of writes and security events" — what an entry contains.</summary>
public sealed class AuditTrailRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly DefaultHttpContext _httpContext = new();

    [Fact]
    public void An_entry_carries_time_action_target_and_data()
    {
        using var context = WidgetContext.Offline();

        Trail().Record(context, new AuditRecord("WidgetRenamed", "Widget", "7", new { previousName = "a", newName = "b" }, ComparsaId: Guid.Empty));

        var entry = SingleEntry(context);
        Assert.Equal(Now, entry.OccurredAt);
        Assert.Equal("WidgetRenamed", entry.Action);
        Assert.Equal("Widget", entry.EntityType);
        Assert.Equal("7", entry.EntityId);
        Assert.Equal(Guid.Empty, entry.ComparsaId);
        Assert.Equal("{\"previousName\":\"a\",\"newName\":\"b\"}", entry.Data);
    }

    [Fact]
    public void The_signed_in_user_is_the_actor()
    {
        var userId = Guid.CreateVersion7();
        _httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "cookie"));
        using var context = WidgetContext.Offline();

        Trail().Record(context, new AuditRecord("WidgetViewed", "Widget"));

        Assert.Equal(userId, SingleEntry(context).ActorUserId);
    }

    [Fact]
    public void An_anonymous_request_has_no_actor_unless_one_is_given()
    {
        var explicitActor = Guid.CreateVersion7();
        using var context = WidgetContext.Offline();

        Trail().Record(context, new AuditRecord("SignInFailed", "User"));
        Trail().Record(context, new AuditRecord("SignedIn", "User", ActorUserId: explicitActor));

        var entries = context.ChangeTracker.Entries<AuditEntry>().Select(e => e.Entity).ToList();
        Assert.Null(entries.Single(e => e.Action == "SignInFailed").ActorUserId);
        Assert.Equal(explicitActor, entries.Single(e => e.Action == "SignedIn").ActorUserId);
    }

    [Fact]
    public void The_trace_id_is_the_one_returned_to_the_client()
    {
        using var activity = new Activity("request").Start();
        using var context = WidgetContext.Offline();

        Trail().Record(context, new AuditRecord("WidgetViewed", "Widget"));

        Assert.Equal(activity.TraceId.ToHexString(), SingleEntry(context).TraceId);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("newPassword")]
    [InlineData("token")]
    [InlineData("resetToken")]
    [InlineData("code")]
    [InlineData("recoveryCode")]
    [InlineData("authenticatorKey")]
    public void Data_that_looks_like_a_secret_is_refused(string property)
    {
        using var context = WidgetContext.Offline();
        var data = new Dictionary<string, object> { ["nested"] = new Dictionary<string, string> { [property] = "value" } };

        Assert.Throws<InvalidOperationException>(() => Trail().Record(context, new AuditRecord("WidgetChanged", "Widget", Data: data)));
    }

    [Fact]
    public void Attacker_supplied_values_are_capped_and_lose_nul_characters_everywhere()
    {
        using var context = WidgetContext.Offline();
        var hostile = new string('x', 600) + "\0%00";
        var data = new Dictionary<string, object>
        {
            ["attemptedEmail"] = hostile,
            ["list"] = new[] { "a\0b" },
            ["key\0with nul"] = "v",
        };

        Trail().Record(context, new AuditRecord("SignInFailed", "User", hostile, data));

        var entry = SingleEntry(context);
        Assert.Equal(AuditTrail.MaxCodeLength, entry.EntityId!.Length);
        Assert.DoesNotContain("\\u0000", entry.Data, StringComparison.Ordinal);
        Assert.Contains("\"ab\"", entry.Data, StringComparison.Ordinal);
        Assert.Contains("\"keywith nul\"", entry.Data, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', AuditTrail.MaxStringLength + 1), entry.Data, StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_string_is_cleaned_too()
    {
        using var context = WidgetContext.Offline();

        Trail().Record(context, new AuditRecord("WidgetChanged", "Widget", Data: "x\0y"));

        Assert.Equal("\"xy\"", SingleEntry(context).Data);
    }

    [Fact]
    public void Truncation_never_splits_a_surrogate_pair_and_lone_surrogates_are_replaced()
    {
        var emoji = char.ConvertFromUtf32(0x1F525);
        var value = new string('x', AuditTrail.MaxCodeLength - 1) + emoji;

        Assert.Equal(new string('x', AuditTrail.MaxCodeLength - 1), AuditTrail.CleanText(value, AuditTrail.MaxCodeLength));
        Assert.Equal("a�b", AuditTrail.CleanText("a\uD83Db", 10));
    }

    [Fact]
    public void Oversized_data_is_replaced_by_a_marker_that_names_its_properties()
    {
        using var context = WidgetContext.Offline();
        var rows = Enumerable.Range(0, 100).Select(_ => new string('x', 400)).ToArray();

        Trail().Record(context, new AuditRecord("WidgetChanged", "Widget", Data: new { rows, note = "n" }));

        var data = SingleEntry(context).Data!;
        Assert.Contains("\"truncated\":true", data, StringComparison.Ordinal);
        Assert.Contains("\"properties\":[\"rows\",\"note\"]", data, StringComparison.Ordinal);
    }

    [Fact]
    public void An_authenticated_principal_without_a_user_id_is_not_recorded_as_anonymous()
    {
        _httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "someone")], "cookie"));
        using var context = WidgetContext.Offline();

        Assert.Throws<InvalidOperationException>(() => Trail().Record(context, new AuditRecord("WidgetViewed", "Widget")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_action_is_a_programming_error(string action)
    {
        using var context = WidgetContext.Offline();

        Assert.Throws<ArgumentException>(() => Trail().Record(context, new AuditRecord(action, "Widget")));
    }

    [Fact]
    public void A_context_without_the_audit_mapping_is_reported_clearly()
    {
        using var context = new DbContext(new DbContextOptionsBuilder().UseModuleDatabase("Host=offline", "other").Options);

        var exception = Assert.Throws<InvalidOperationException>(() => Trail().Record(context, new AuditRecord("X", "Y")));

        Assert.Contains("AddAuditTrail", exception.Message, StringComparison.Ordinal);
    }

    private AuditTrail Trail() => new(new HttpContextAccessor { HttpContext = _httpContext }, _time, NullLogger<AuditTrail>.Instance);

    private static AuditEntry SingleEntry(DbContext context) => Assert.Single(context.ChangeTracker.Entries<AuditEntry>()).Entity;
}

/// <summary>
/// Spec audit-privacy: an entry is stored in the same transaction as its change, and the trail is
/// append-only.
/// </summary>
public sealed class AuditTrailStorageTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly AuditTrail _trail = new(new HttpContextAccessor(), TimeProvider.System, NullLogger<AuditTrail>.Instance);
    private NpgsqlDataSource _dataSource = null!;

    public async ValueTask InitializeAsync()
    {
        _dataSource = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        await using var context = WidgetContext.Create(_dataSource);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _dataSource.DisposeAsync();

    [Fact]
    public async Task An_entry_is_stored_with_the_change_it_describes()
    {
        await using (var context = WidgetContext.Create(_dataSource))
        {
            context.Widgets.Add(new Widget { Id = 1, Name = "first" });
            _trail.Record(context, new AuditRecord("WidgetCreated", "Widget", "1"));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = WidgetContext.Create(_dataSource);
        Assert.True(await read.Widgets.AnyAsync(w => w.Id == 1, TestContext.Current.CancellationToken));
        Assert.Single(await read.Set<AuditEntry>().Where(e => e.EntityId == "1").ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failing_change_stores_no_entry()
    {
        await SaveWidgetAsync(2);

        await using (var context = WidgetContext.Create(_dataSource))
        {
            context.Widgets.Add(new Widget { Id = 2, Name = "duplicate key" });
            _trail.Record(context, new AuditRecord("WidgetCreated", "Widget", "2-duplicate"));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await using var read = WidgetContext.Create(_dataSource);
        Assert.False(await read.Set<AuditEntry>().AnyAsync(e => e.EntityId == "2-duplicate", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failing_entry_stores_no_change()
    {
        var existing = await StoreEntryAsync("3");

        await using (var context = WidgetContext.Create(_dataSource))
        {
            context.Widgets.Add(new Widget { Id = 3, Name = "third" });
            context.Add(new AuditEntry { Id = existing, OccurredAt = DateTimeOffset.UtcNow, Action = "Duplicate", EntityType = "Widget" });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await using var read = WidgetContext.Create(_dataSource);
        Assert.False(await read.Widgets.AnyAsync(w => w.Id == 3, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Audit_entries_cannot_be_modified()
    {
        var id = await StoreEntryAsync("4");

        await using (var context = WidgetContext.Create(_dataSource))
        {
            var entry = await context.Set<AuditEntry>().SingleAsync(e => e.Id == id, TestContext.Current.CancellationToken);
            context.Entry(entry).Property(e => e.Action).CurrentValue = "Tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await using var read = WidgetContext.Create(_dataSource);
        Assert.Equal("WidgetCreated", (await read.Set<AuditEntry>().SingleAsync(e => e.Id == id, TestContext.Current.CancellationToken)).Action);
    }

    [Fact]
    public async Task Audit_entries_cannot_be_deleted()
    {
        var id = await StoreEntryAsync("5");

        await using (var context = WidgetContext.Create(_dataSource))
        {
            var entry = await context.Set<AuditEntry>().SingleAsync(e => e.Id == id, TestContext.Current.CancellationToken);
            context.Remove(entry);
            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        }

        await using var read = WidgetContext.Create(_dataSource);
        Assert.True(await read.Set<AuditEntry>().AnyAsync(e => e.Id == id, TestContext.Current.CancellationToken));
    }

    private async Task SaveWidgetAsync(int id)
    {
        await using var context = WidgetContext.Create(_dataSource);
        context.Widgets.Add(new Widget { Id = id, Name = $"widget {id}" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> StoreEntryAsync(string entityId)
    {
        await using var context = WidgetContext.Create(_dataSource);
        _trail.Record(context, new AuditRecord("WidgetCreated", "Widget", entityId));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return context.Set<AuditEntry>().Local.Single().Id;
    }
}
