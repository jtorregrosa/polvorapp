using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Diagnostics;

namespace PolvorApp.AuditPrivacy;

/// <summary>
/// Fills time, actor and correlation id of an audit entry (design D3) and keeps attacker-supplied
/// values (e.g. an attempted sign-in email) from breaking the caller's save: every string, key and
/// identifier loses NUL characters and lone surrogates (PostgreSQL rejects both) and is capped, and
/// oversized data is replaced by a marker that still names its properties.
/// </summary>
internal sealed partial class AuditTrail(
    IHttpContextAccessor httpContextAccessor, TimeProvider timeProvider, ILogger<AuditTrail> logger) : IAuditTrail
{
    public const int MaxCodeLength = 100;
    public const int MaxStringLength = 512;
    public const int MaxDataLength = 16 * 1024;

    private static readonly JsonSerializerOptions DataJson = new(JsonSerializerDefaults.Web);

    public void Record(DbContext context, AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(record);
        if (context.Model.FindEntityType(typeof(AuditEntry)) is null)
        {
            throw new InvalidOperationException(
                $"{context.GetType().Name} does not map the audit trail; call modelBuilder.AddAuditTrail() in OnModelCreating.");
        }

        RequireCode(record.Action, nameof(record.Action));
        RequireCode(record.EntityType, nameof(record.EntityType));

        var httpContext = httpContextAccessor.HttpContext;
        var now = timeProvider.GetUtcNow();
        context.Add(new AuditEntry
        {
            Id = Guid.CreateVersion7(now),
            OccurredAt = now,
            ActorUserId = record.Anonymous ? null : record.ActorUserId ?? SignedInUserId(httpContext),
            Action = record.Action,
            EntityType = record.EntityType,
            EntityId = record.EntityId is null ? null : CleanText(record.EntityId, MaxCodeLength),
            ComparsaId = record.ComparsaId,
            TraceId = TraceIds.Find(httpContext),
            Data = Serialize(record),
        });
    }

    /// <summary>Removes NUL and lone surrogates and caps the length without splitting a surrogate pair.</summary>
    internal static string CleanText(string value, int maxLength)
    {
        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        for (var i = 0; i < value.Length && builder.Length < maxLength; i++)
        {
            var c = value[i];
            if (c == '\0')
            {
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                if (builder.Length + 2 > maxLength)
                {
                    break;
                }

                builder.Append(c).Append(value[++i]);
            }
            else
            {
                builder.Append(char.IsSurrogate(c) ? '�' : c);
            }
        }

        return builder.ToString();
    }

    private static void RequireCode(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxCodeLength)
        {
            throw new ArgumentException($"{name} must be a code of 1 to {MaxCodeLength} characters.", name);
        }
    }

    private string? Serialize(AuditRecord record)
    {
        if (record.Data is null)
        {
            return null;
        }

        JsonNode? node;
        try
        {
            node = Clean(JsonSerializer.SerializeToNode(record.Data, DataJson));
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Audit data of {record.Action} on {record.EntityType} cannot be serialised.", exception);
        }

        var json = node?.ToJsonString(DataJson);
        if (json is null || json.Length <= MaxDataLength)
        {
            return json;
        }

        LogDataTruncated(logger, record.Action, record.EntityType, json.Length);
        var marker = new JsonObject { ["truncated"] = true, ["originalLength"] = json.Length };
        if (node is JsonObject obj)
        {
            marker["properties"] = new JsonArray([.. obj.Select(p => (JsonNode?)JsonValue.Create(p.Key))]);
        }

        return marker.ToJsonString(DataJson);
    }

    /// <summary>
    /// Rebuilds the tree with clean keys and strings. A property named like a secret is a
    /// programming error and fails the operation (spec: never passwords, codes or tokens).
    /// </summary>
    private static JsonNode? Clean(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var cleanObject = new JsonObject();
                foreach (var (name, value) in obj)
                {
                    if (SecretName().IsMatch(name))
                    {
                        throw new InvalidOperationException($"Audit data must not contain secrets (property '{name}').");
                    }

                    cleanObject[CleanText(name, MaxCodeLength)] = Clean(value?.DeepClone());
                }

                return cleanObject;
            case JsonArray array:
                return new JsonArray([.. array.Select(item => Clean(item?.DeepClone()))]);
            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(CleanText(text, MaxStringLength));
            default:
                return node;
        }
    }

    private static Guid? SignedInUserId(HttpContext? httpContext)
    {
        var user = httpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // An authenticated principal without a user id must not be recorded as anonymous.
        return Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("The signed-in principal has no user identifier; the actor cannot be audited.");
    }

    [GeneratedRegex("password|token|secret|^code$|^recoverycodes?$|authenticatorkey|^key$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretName();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit data of {Action} on {EntityType} was {Length} characters and was replaced by a truncation marker")]
    private static partial void LogDataTruncated(ILogger logger, string action, string entityType, int length);
}
