using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PolvorApp.Api.Platform;

/// <summary>
/// Keeps enum components to their codes. When an enum is only ever used as a nullable property
/// (e.g. a pistol's <c>Handedness?</c>), the generator puts <c>null</c> into the shared component's
/// <c>enum</c>; the property already says it is nullable (<c>oneOf: [null, $ref]</c>), so the null
/// in the component is removed and generated clients get a clean union of codes.
/// </summary>
internal static class OpenApiEnums
{
    public static OpenApiOptions AddCodesOnlyEnums(this OpenApiOptions options) =>
        options.AddDocumentTransformer((document, _, _) =>
        {
            foreach (var schema in document.Components?.Schemas?.Values.OfType<OpenApiSchema>() ?? [])
            {
                if (schema.Enum is not { Count: > 0 } values || !values.Any(value => value is null))
                {
                    continue;
                }

                schema.Enum = [.. values.Where(value => value is not null)];
                if (schema.Type is { } type)
                {
                    schema.Type = type & ~JsonSchemaType.Null;
                }
            }

            return Task.CompletedTask;
        });
}
