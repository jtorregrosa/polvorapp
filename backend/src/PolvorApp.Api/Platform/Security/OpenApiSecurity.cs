using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PolvorApp.Api.Platform.Security;

/// <summary>
/// Documents in the API contract how calls are authenticated (spec: Authenticated API by default;
/// design D6): the session cookie on every operation that is not anonymous, and the anti-forgery
/// header on every state-changing operation.
/// </summary>
internal static class OpenApiSecurity
{
    public const string SessionScheme = "session";

    public static OpenApiOptions AddPlatformSecurity(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[SessionScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = "polvorapp.session",
                Description = "HttpOnly session cookie set by a completed two-factor sign-in.",
            };
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            if (!metadata.OfType<IAllowAnonymous>().Any())
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(SessionScheme, context.Document)] = [],
                });
            }

            if (context.Description.HttpMethod is not ("GET" or "HEAD" or "OPTIONS" or "TRACE"))
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = Antiforgery.HeaderName,
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "The anti-forgery token from the XSRF-TOKEN cookie (GET /api/auth/antiforgery).",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                });
            }

            return Task.CompletedTask;
        });
        return options;
    }
}
