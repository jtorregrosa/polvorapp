using Microsoft.Extensions.Localization;
using PolvorApp.Api.Platform.Diagnostics;
using PolvorApp.Api.Platform.Localization;
using PolvorApp.SharedKernel.Diagnostics;

namespace PolvorApp.Api.Platform.Errors;

/// <summary>
/// Every error is an RFC 9457 problem-details document with a <c>traceId</c>; exception details
/// only reach clients in Development (spec: Problem details error responses).
/// </summary>
internal static class ErrorHandling
{
    public static IServiceCollection AddPlatformErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["traceId"] = TraceIds.Current(context.HttpContext);
            LocalizeTitle(context);
        });
        return services;
    }

    /// <summary>Titles follow the request culture; codes and types stay culture-independent.</summary>
    private static void LocalizeTitle(ProblemDetailsContext context)
    {
        var localizer = context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<SharedResources>>();
        var title = localizer[$"ProblemTitle.{context.ProblemDetails.Status}"];
        if (!title.ResourceNotFound)
        {
            context.ProblemDetails.Title = title.Value;
        }
    }

    public static IApplicationBuilder UsePlatformErrorHandling(this IApplicationBuilder app, IHostEnvironment environment)
    {
        // Development already has the developer exception page (added by WebApplication).
        if (!environment.IsDevelopment())
        {
            app.UseExceptionHandler();
        }

        app.UseStatusCodePages();
        return app;
    }
}
