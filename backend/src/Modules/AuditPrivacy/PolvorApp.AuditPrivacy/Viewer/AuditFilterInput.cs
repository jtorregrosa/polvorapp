using System.Buffers.Binary;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using PolvorApp.SharedKernel.Time;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.AuditPrivacy.Viewer;

/// <summary>
/// The audit log's query string as sent; every value is validated by <see cref="AuditFilterInput"/>.
/// An empty value counts as absent.
/// </summary>
internal sealed class AuditLogQuery
{
    /// <summary>First day, <c>yyyy-MM-dd</c> in Europe/Madrid, included.</summary>
    [FromQuery(Name = "from")]
    public string? From { get; init; }

    /// <summary>Last day, <c>yyyy-MM-dd</c> in Europe/Madrid, included.</summary>
    [FromQuery(Name = "to")]
    public string? To { get; init; }

    /// <summary>The acting user's id, or <c>none</c> for entries without one.</summary>
    [FromQuery(Name = "actorUserId")]
    public string? ActorUserId { get; init; }

    [FromQuery(Name = "comparsaId")]
    public string? ComparsaId { get; init; }

    [FromQuery(Name = "entityType")]
    public string? EntityType { get; init; }

    /// <summary>Only together with <see cref="EntityType"/>.</summary>
    [FromQuery(Name = "entityId")]
    public string? EntityId { get; init; }

    [FromQuery(Name = "action")]
    public string? Action { get; init; }

    /// <summary>The <c>nextCursor</c> of the previous page.</summary>
    [FromQuery(Name = "cursor")]
    public string? Cursor { get; init; }

    /// <summary>1 to 100 entries; 50 when absent.</summary>
    [FromQuery(Name = "limit")]
    public string? Limit { get; init; }
}

/// <summary>Who acted: anyone, nobody (system or anonymous), or one user.</summary>
internal abstract record AuditActorFilter
{
    private AuditActorFilter()
    {
    }

    public static readonly AuditActorFilter Any = new AnyActor();

    public static readonly AuditActorFilter None = new NoActor();

    public sealed record AnyActor : AuditActorFilter;

    public sealed record NoActor : AuditActorFilter;

    public sealed record User(Guid UserId) : AuditActorFilter;
}

/// <summary>A record type and, optionally, one record of it: an id never comes without its type.</summary>
internal sealed record AuditEntityFilter(string Type, string? Id);

/// <summary>
/// A validated audit log query (design D10), built only by <see cref="AuditFilterInput.Parse"/> or
/// <see cref="Create"/>, which keep its invariants: the period is not inverted and the page size is 1 to
/// 100. <see cref="Before"/> is the end of the last day, excluded.
/// </summary>
internal sealed record AuditFilter
{
    private AuditFilter()
    {
    }

    public DateTimeOffset? From { get; private init; }

    public DateTimeOffset? Before { get; private init; }

    public AuditActorFilter Actor { get; private init; } = AuditActorFilter.Any;

    public Guid? ComparsaId { get; private init; }

    public AuditEntityFilter? Entity { get; private init; }

    public string? Action { get; private init; }

    public AuditCursor? Cursor { get; init; }

    public int Limit { get; private init; } = AuditFilterInput.DefaultLimit;

    /// <summary>A filter from already valid values; throws on a broken invariant (a programming error).</summary>
    public static AuditFilter Create(
        DateTimeOffset? from = null,
        DateTimeOffset? before = null,
        AuditActorFilter? actor = null,
        Guid? comparsaId = null,
        AuditEntityFilter? entity = null,
        string? action = null,
        AuditCursor? cursor = null,
        int limit = AuditFilterInput.DefaultLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, AuditFilterInput.MaxLimit);
        if (from is { } start && before is { } end && start >= end)
        {
            throw new ArgumentException("The period must not be inverted.", nameof(from));
        }

        return new AuditFilter
        {
            From = from,
            Before = before,
            Actor = actor ?? AuditActorFilter.Any,
            ComparsaId = comparsaId,
            Entity = entity,
            Action = action,
            Cursor = cursor,
            Limit = limit,
        };
    }
}

/// <summary>The position after the last entry of a page: its time and id, in the order of the log (newest first).</summary>
internal readonly record struct AuditCursor(DateTimeOffset OccurredAt, Guid Id)
{
    private const int Length = sizeof(long) + 16;

    /// <summary>Base64url of the UTC ticks and the id.</summary>
    public string Encode()
    {
        Span<byte> bytes = stackalloc byte[Length];
        BinaryPrimitives.WriteInt64BigEndian(bytes, OccurredAt.UtcTicks);
        Id.TryWriteBytes(bytes[sizeof(long)..], bigEndian: true, out _);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string value, out AuditCursor cursor)
    {
        cursor = default;
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        Span<byte> bytes = stackalloc byte[Length + 2];
        if (!Convert.TryFromBase64String(base64, bytes, out var written) || written != Length)
        {
            return false;
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes);
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        cursor = new AuditCursor(new DateTimeOffset(ticks, TimeSpan.Zero), new Guid(bytes.Slice(sizeof(long), 16), bigEndian: true));
        return true;
    }
}

/// <summary>Validates the audit log's query string; every invalid field is named with <c>invalid</c>.</summary>
internal static class AuditFilterInput
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
    public const int MaxEntityIdLength = 100;
    public const string NoActor = "none";

    /// <summary>Days outside these years are refused, so the end of the last day can always be computed.</summary>
    public const int MinYear = 2000;
    public const int MaxYear = 2999;

    public static AuditFilter? Parse(AuditLogQuery query, AuditActionCatalog catalog, IDictionary<string, string> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(errors);

        var from = Day(Present(query.From), "from", errors);
        var to = Day(Present(query.To), "to", errors);
        if (from is { } first && to is { } last && first > last)
        {
            errors["from"] = InputFields.Invalid;
        }

        var actor = Actor(Present(query.ActorUserId), errors);
        var comparsa = Id(Present(query.ComparsaId), "comparsaId", errors);
        var entity = Entity(Present(query.EntityType), Present(query.EntityId), catalog, errors);

        var action = Present(query.Action);
        if (action is not null && !catalog.Contains(action))
        {
            errors["action"] = InputFields.Invalid;
        }

        AuditCursor? cursor = null;
        if (Present(query.Cursor) is { } rawCursor)
        {
            if (AuditCursor.TryDecode(rawCursor, out var decoded))
            {
                cursor = decoded;
            }
            else
            {
                errors["cursor"] = InputFields.Invalid;
            }
        }

        var limit = DefaultLimit;
        if (Present(query.Limit) is { } rawLimit
            && (!int.TryParse(rawLimit, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > MaxLimit))
        {
            errors["limit"] = InputFields.Invalid;
        }

        if (errors.Count > 0)
        {
            return null;
        }

        return AuditFilter.Create(
            from is { } start ? FederationCalendar.StartOf(start) : null,
            to is { } end ? FederationCalendar.StartOf(end.AddDays(1)) : null,
            actor,
            comparsa,
            entity,
            action,
            cursor,
            limit);
    }

    private static string? Present(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static AuditActorFilter Actor(string? value, IDictionary<string, string> errors)
    {
        if (value is null)
        {
            return AuditActorFilter.Any;
        }

        if (string.Equals(value, NoActor, StringComparison.OrdinalIgnoreCase))
        {
            return AuditActorFilter.None;
        }

        return Id(value, "actorUserId", errors) is { } id ? new AuditActorFilter.User(id) : AuditActorFilter.Any;
    }

    private static AuditEntityFilter? Entity(string? type, string? id, AuditActionCatalog catalog, IDictionary<string, string> errors)
    {
        if (type is not null && !catalog.ContainsEntityType(type))
        {
            errors["entityType"] = InputFields.Invalid;
        }

        if (id is not null && (type is null || id.Length > MaxEntityIdLength))
        {
            errors["entityId"] = InputFields.Invalid;
        }

        // Record ids are stored in the canonical GUID form; accept any casing of it.
        var canonicalId = id is not null && Guid.TryParse(id, out var guid) ? guid.ToString() : id;
        return type is null ? null : new AuditEntityFilter(type, canonicalId);
    }

    private static DateOnly? Day(string? value, string field, IDictionary<string, string> errors)
    {
        if (value is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            && day.Year is >= MinYear and <= MaxYear)
        {
            return day;
        }

        errors[field] = InputFields.Invalid;
        return null;
    }

    private static Guid? Id(string? value, string field, IDictionary<string, string> errors)
    {
        if (value is null)
        {
            return null;
        }

        if (Guid.TryParseExact(value, "D", out var id))
        {
            return id;
        }

        errors[field] = InputFields.Invalid;
        return null;
    }
}
