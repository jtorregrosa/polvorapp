using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.AuditPrivacy.Viewer;

/// <summary>An audit entry's acting user: their current name, or none once erased.</summary>
internal sealed record AuditActorResponse(Guid Id, string? Name, bool Erased);

/// <summary>An audit entry as the audit log shows it (spec: Audit log query (UC-25)).</summary>
internal sealed record AuditEntryResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    string EntityType,
    string? EntityId,
    bool RecordExists,
    Guid? ComparsaId,
    string? ComparsaName,
    string? TraceId,
    AuditActorResponse? Actor,
    JsonElement? Data);

/// <summary>A page of the audit log; <c>nextCursor</c> is null on the last page.</summary>
internal sealed record AuditPageResponse(List<AuditEntryResponse> Items, string? NextCursor);

/// <summary>An action code of the catalogue, for the action filter.</summary>
internal sealed record AuditActionResponse(string Code, string EntityType);

/// <summary>
/// The audit log (UC-25; design D10), for Admins only. Reading it writes and exports nothing, so it is
/// not audited.
/// </summary>
internal static class AuditLogEndpoints
{
    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var audit = endpoints.MapGroup("/audit-entries").WithTags("Audit").RequireAuthorization(AuthorizationPolicies.Admin)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        audit.MapGet("/", ListAsync).WithName("ListAuditEntries")
            .WithSummary("Audit entries, newest first, one page at a time, optionally filtered.")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        audit.MapGet("/actions", (AuditActionCatalog catalog) =>
                TypedResults.Ok(catalog.Actions.Select(a => new AuditActionResponse(a.Code, a.EntityType)).ToList()))
            .WithName("ListAuditActions").WithSummary("Every audit action code and the entity type it is recorded under.");
        return endpoints;
    }

    private static async Task<Results<Ok<AuditPageResponse>, ProblemHttpResult>> ListAsync(
        [AsParameters] AuditLogQuery query,
        AuditActionCatalog catalog,
        AuditQuery audit,
        IUserDirectory users,
        ICatalogDirectory comparsas,
        IEnumerable<IAuditRecordResolver> resolvers,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var filter = AuditFilterInput.Parse(query, catalog, errors);
        if (filter is null)
        {
            return ProblemResults.Invalid(errors);
        }

        var page = await audit.ReadAsync(filter, cancellationToken);
        var entries = page.Entries;

        Guid[] actorIds = [.. entries.Select(e => e.ActorUserId).OfType<Guid>().Distinct()];
        var actors = (await users.FindManyAsync(actorIds, cancellationToken)).ToDictionary(u => u.Id);
        Guid[] comparsaIds = [.. entries.Select(e => e.ComparsaId).OfType<Guid>().Distinct()];
        var comparsaNames = (await comparsas.FindComparsasAsync(comparsaIds, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var existing = await ExistingRecordsAsync(entries, resolvers, cancellationToken);

        var items = entries.Select(e => new AuditEntryResponse(
            e.Id,
            e.OccurredAt,
            e.Action,
            e.EntityType,
            e.EntityId,
            e.EntityId is not null && existing.Contains((e.EntityType, e.EntityId)),
            e.ComparsaId,
            e.ComparsaId is { } c && comparsaNames.TryGetValue(c, out var name) ? name : null,
            e.TraceId,
            Actor(e.ActorUserId, actors),
            e.Data is null ? null : JsonSerializer.Deserialize<JsonElement>(e.Data))).ToList();
        return TypedResults.Ok(new AuditPageResponse(items, page.Next?.Encode()));
    }

    private static AuditActorResponse? Actor(Guid? id, Dictionary<Guid, UserSummary> actors)
    {
        if (id is not { } actorId)
        {
            return null;
        }

        if (!actors.TryGetValue(actorId, out var user))
        {
            return new AuditActorResponse(actorId, null, Erased: false);
        }

        // An erased user keeps only their id: the UI shows "Erased user" instead of the marker name.
        var erased = user.Status == UserStatus.Erased;
        return new AuditActorResponse(actorId, erased ? null : user.Name, erased);
    }

    /// <summary>The (type, id) pairs of the page whose record still exists, one query per linkable type.</summary>
    private static async Task<HashSet<(string, string)>> ExistingRecordsAsync(
        IReadOnlyList<AuditEntry> entries, IEnumerable<IAuditRecordResolver> resolvers, CancellationToken cancellationToken)
    {
        var existing = new HashSet<(string, string)>();
        foreach (var resolver in resolvers)
        {
            string[] ids = [.. entries.Where(e => e.EntityType == resolver.EntityType && e.EntityId is not null).Select(e => e.EntityId!).Distinct()];
            if (ids.Length == 0)
            {
                continue;
            }

            foreach (var id in await resolver.ExistingAsync(ids, cancellationToken))
            {
                existing.Add((resolver.EntityType, id));
            }
        }

        return existing;
    }
}
