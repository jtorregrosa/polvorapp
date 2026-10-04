using PolvorApp.Api.Platform;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Platform.Diagnostics;
using PolvorApp.Api.Platform.Email;
using PolvorApp.Api.Platform.Errors;
using PolvorApp.Api.Platform.Health;
using PolvorApp.Api.Platform.Images;
using PolvorApp.Api.Platform.Localization;
using PolvorApp.Api.Platform.Security;
using PolvorApp.Api.Platform.Storage;
using PolvorApp.Api.Platform.SystemInfo;
using PolvorApp.ArquebusierRegistry;
using PolvorApp.AuditPrivacy;
using PolvorApp.Billing;
using PolvorApp.ComparsaOrders;
using PolvorApp.ComplianceInsights;
using PolvorApp.Distribution;
using PolvorApp.Exports;
using PolvorApp.FederationCatalog;
using PolvorApp.FestivalEditions;
using PolvorApp.IdentityAccess;
using PolvorApp.Notifications;
using PolvorApp.SharedKernel.Modules;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddPlatformLogging();

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddPlatformEmail(builder.Configuration);
builder.Services.AddPlatformStorage(builder.Configuration);
builder.Services.AddPlatformImages();
builder.Services.AddPlatformHealthChecks();
builder.Services.AddPlatformLocalization();
builder.Services.AddPlatformErrorHandling();
builder.Services.AddPlatformAuthorization();
builder.Services.AddPlatformAntiforgery();
builder.Services.AddPlatformRateLimits(builder.Configuration);
// Numbers are numbers: no quoted numbers on input, and the contract types integers as integers.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict);
builder.Services.AddOpenApi(options => options
    .AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "PolvorApp API";
        return Task.CompletedTask;
    })
    .AddPlatformSecurity()
    .AddCodesOnlyEnums()
    .AddAuditActionCatalogue());

// Modules are registered explicitly, one per capability (ADR-0001).
builder.Services.AddModules(builder.Configuration, new AuditPrivacyModule(), new IdentityAccessModule(), new FederationCatalogModule(), new ArquebusierRegistryModule(), new ComplianceInsightsModule(), new FestivalEditionsModule(), new ComparsaOrdersModule(), new BillingModule(), new ExportsModule(), new DistributionModule(), new NotificationsModule());

var app = builder.Build();

// migrate, seed, create-admin, ...: run the command instead of the web server.
if (await HostCommands.TryRunAsync(app, args) is { } exitCode)
{
    return exitCode;
}

// Client address from the trusted proxy only (rate limits per client).
app.UseForwardedHeaders();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
// Culture before error handling, so error responses produced further down are localised.
app.UseRequestLocalization();
app.UsePlatformErrorHandling(app.Environment);
app.UseAuthentication();
app.UsePlatformAntiforgery();
app.UseAuthorization();
app.UseRateLimiter();

var api = app.MapGroup("/api").RequireSignedInUserByDefault();
if (app.Environment.IsDevelopment())
{
    api.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
}

api.MapPlatformHealthChecks();
api.MapSystemInfo();
api.MapAntiforgeryToken();
api.MapModules();

// Invalid configuration makes RunAsync throw: the host logs "Hosting failed to start" (Error)
// with the failing setting names, never their values, and the process exits non-zero.
await app.RunAsync();
return 0;

public partial class Program;
