using PolvorApp.AuditPrivacy.Viewer;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.Api.Tests.Audit;

/// <summary>Spec audit-privacy "Audit log query (UC-25)" and design D10: the filters, the page size and the cursor.</summary>
public sealed class AuditFilterInputTests
{
    private static readonly AuditActionCatalog Catalog = new(
    [
        new Source([new("ArquebusierUpdated", "Arquebusier"), new("ComparsaOrderValidated", "ComparsaOrder")]),
    ]);

    [Fact]
    public void No_filter_reads_the_newest_fifty()
    {
        var filter = Parse(new AuditLogQuery());

        Assert.NotNull(filter);
        Assert.Equal(AuditFilterInput.DefaultLimit, filter.Limit);
        Assert.Null(filter.From);
        Assert.Null(filter.Cursor);
    }

    [Fact]
    public void Every_filter_is_read()
    {
        var actor = Guid.CreateVersion7();
        var comparsa = Guid.CreateVersion7();
        var entity = Guid.CreateVersion7().ToString();

        var filter = Parse(new AuditLogQuery
        {
            From = "2030-03-01",
            To = "2030-03-31",
            ActorUserId = actor.ToString(),
            ComparsaId = comparsa.ToString(),
            EntityType = "Arquebusier",
            EntityId = entity,
            Action = "ArquebusierUpdated",
            Limit = "100",
        });

        Assert.NotNull(filter);
        Assert.Equal(FederationCalendar.StartOf(new DateOnly(2030, 3, 1)), filter.From);
        Assert.Equal(FederationCalendar.StartOf(new DateOnly(2030, 4, 1)), filter.Before);
        Assert.Equal(new AuditActorFilter.User(actor), filter.Actor);
        Assert.Equal(comparsa, filter.ComparsaId);
        Assert.Equal(new AuditEntityFilter("Arquebusier", entity), filter.Entity);
        Assert.Equal(("ArquebusierUpdated", 100), (filter.Action, filter.Limit));
    }

    [Fact]
    public void Entries_without_a_user_are_asked_for_with_none()
    {
        var filter = Parse(new AuditLogQuery { ActorUserId = "none" });

        Assert.NotNull(filter);
        Assert.Equal(AuditActorFilter.None, filter.Actor);
    }

    [Fact]
    public void Empty_values_count_as_absent()
    {
        var filter = Parse(new AuditLogQuery { From = "", EntityType = "", Action = "", ActorUserId = "", Limit = "" });

        Assert.NotNull(filter);
        Assert.Equal((null, AuditActorFilter.Any, null, null), (filter.From, filter.Actor, filter.Entity, filter.Action));
        Assert.Equal(AuditFilterInput.DefaultLimit, filter.Limit);
    }

    [Fact]
    public void A_record_id_in_capitals_is_read_in_its_stored_form()
    {
        var id = Guid.CreateVersion7();

        var filter = Parse(new AuditLogQuery { EntityType = "Arquebusier", EntityId = id.ToString().ToUpperInvariant() });

        Assert.NotNull(filter);
        Assert.Equal(id.ToString(), filter.Entity?.Id);
    }

    [Theory]
    [InlineData("2030-03-29", "2030-03-29T00:00:00+01:00", "2030-03-30T00:00:00+01:00")]
    [InlineData("2030-03-31", "2030-03-31T00:00:00+01:00", "2030-04-01T00:00:00+02:00")]
    [InlineData("2030-10-27", "2030-10-27T00:00:00+02:00", "2030-10-28T00:00:00+01:00")]
    public void A_day_is_a_whole_day_in_Madrid_also_when_the_clocks_change(string day, string start, string end)
    {
        var filter = Parse(new AuditLogQuery { From = day, To = day });

        Assert.NotNull(filter);
        Assert.Equal(DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture), filter.From);
        Assert.Equal(DateTimeOffset.Parse(end, System.Globalization.CultureInfo.InvariantCulture), filter.Before);
    }

    [Theory]
    [InlineData("from", "2030-04-01", "2030-03-01", null, null, null, null, null, null)]
    [InlineData("from", "1 de marzo", null, null, null, null, null, null, null)]
    [InlineData("to", null, "2030-02-30", null, null, null, null, null, null)]
    [InlineData("to", null, "9999-12-31", null, null, null, null, null, null)]
    [InlineData("from", "0001-01-01", null, null, null, null, null, null, null)]
    [InlineData("actorUserId", null, null, "alguien", null, null, null, null, null)]
    [InlineData("comparsaId", null, null, null, "norte", null, null, null, null)]
    [InlineData("entityType", null, null, null, null, "Gadget", null, null, null)]
    [InlineData("entityId", null, null, null, null, null, "42", null, null)]
    [InlineData("action", null, null, null, null, null, null, "Teleported", null)]
    [InlineData("limit", null, null, null, null, null, null, null, "0")]
    [InlineData("limit", null, null, null, null, null, null, null, "101")]
    [InlineData("limit", null, null, null, null, null, null, null, "cincuenta")]
    public void An_invalid_filter_is_named(
        string field, string? from, string? to, string? actor, string? comparsa, string? entityType, string? entityId, string? action, string? limit)
    {
        var errors = new Dictionary<string, string>();

        var filter = AuditFilterInput.Parse(
            new AuditLogQuery
            {
                From = from,
                To = to,
                ActorUserId = actor,
                ComparsaId = comparsa,
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                Limit = limit,
            },
            Catalog,
            errors);

        Assert.Null(filter);
        Assert.Equal("invalid", Assert.Contains(field, errors));
    }

    [Fact]
    public void A_long_entity_id_is_invalid()
    {
        var errors = new Dictionary<string, string>();

        AuditFilterInput.Parse(new AuditLogQuery { EntityType = "Arquebusier", EntityId = new string('7', 101) }, Catalog, errors);

        Assert.Contains("entityId", errors);
    }

    [Fact]
    public void A_filter_cannot_be_built_inverted_or_with_a_page_out_of_range()
    {
        var day = new DateTimeOffset(2030, 3, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => AuditFilter.Create(from: day, before: day.AddDays(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AuditFilter.Create(limit: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AuditFilter.Create(limit: 101));
    }

    [Fact]
    public void The_cursor_round_trips()
    {
        var cursor = new AuditCursor(new DateTimeOffset(2030, 3, 1, 10, 0, 0, 123, TimeSpan.Zero).AddTicks(4560), Guid.CreateVersion7());

        var filter = Parse(new AuditLogQuery { Cursor = cursor.Encode() });

        Assert.NotNull(filter);
        Assert.Equal(cursor, filter.Cursor);
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("AAAA")]
    [InlineData("%%%")]
    [InlineData("f_________________________________8")] // 24 bytes whose ticks are out of range
    public void A_malformed_cursor_is_invalid(string cursor)
    {
        var errors = new Dictionary<string, string>();

        Assert.Null(AuditFilterInput.Parse(new AuditLogQuery { Cursor = cursor }, Catalog, errors));
        Assert.Equal("invalid", errors["cursor"]);
    }

    private static AuditFilter? Parse(AuditLogQuery query)
    {
        var errors = new Dictionary<string, string>();
        var filter = AuditFilterInput.Parse(query, Catalog, errors);
        Assert.Empty(errors);
        return filter;
    }

    private sealed record Source(IReadOnlyList<AuditActionDefinition> Actions) : IAuditActionSource;
}
