using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Platform;

/// <summary>
/// Publishes the audit action catalogue in the contract (add-audit-privacy, design D2): the
/// <c>code</c> and <c>entityType</c> of <c>AuditActionResponse</c> become enums of every declared
/// code and entity type, so the generated client lists them, a frontend test finds any without a
/// label, and the contract drift check fails when a module adds a code without regenerating.
/// Recorded entries keep plain strings: they may hold codes no longer declared.
/// </summary>
internal static class OpenApiAuditActions
{
    private const string Schema = "AuditActionResponse";

    public static OpenApiOptions AddAuditActionCatalogue(this OpenApiOptions options) =>
        options.AddDocumentTransformer((document, context, _) =>
        {
            if (document.Components?.Schemas is not { } schemas
                || !schemas.TryGetValue(Schema, out var schema) || schema is not OpenApiSchema { Properties: { } properties })
            {
                return Task.CompletedTask;
            }

            var actions = context.ApplicationServices.GetServices<IAuditActionSource>().SelectMany(source => source.Actions).ToList();
            Restrict(properties, "code", actions.Select(a => a.Code));
            Restrict(properties, "entityType", actions.Select(a => a.EntityType));
            return Task.CompletedTask;
        });

    private static void Restrict(IDictionary<string, IOpenApiSchema> properties, string name, IEnumerable<string> values)
    {
        if (properties.TryGetValue(name, out var schema) && schema is OpenApiSchema property)
        {
            property.Enum = [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(value => (JsonNode)JsonValue.Create(value))];
        }
    }
}
