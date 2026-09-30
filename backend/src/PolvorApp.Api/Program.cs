using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Platform.Diagnostics;
using PolvorApp.Api.Platform.Errors;
using PolvorApp.Api.Platform.Health;
using PolvorApp.Api.Platform.Localization;
using PolvorApp.Api.Platform.Security;
using PolvorApp.Api.Platform.Seeding;
using PolvorApp.Api.Platform.SystemInfo;
using PolvorApp.SharedKernel.Modules;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddPlatformLogging();

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddPlatformHealthChecks();
builder.Services.AddPlatformLocalization();
builder.Services.AddPlatformErrorHandling();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "PolvorApp API";
    return Task.CompletedTask;
}));

// Modules are registered explicitly, one per capability (ADR-0001). None exist yet.
builder.Services.AddModules(builder.Configuration);

var app = builder.Build();

if (args is [SeedCommand.Verb, ..])
{
    await using var seedApp = app;
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    return await SeedCommand.RunAsync(app.Services, app.Environment, cancellation.Token);
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
// Culture before error handling, so error responses produced further down are localised.
app.UseRequestLocalization();
app.UsePlatformErrorHandling(app.Environment);

var api = app.MapGroup("/api");
if (app.Environment.IsDevelopment())
{
    api.MapOpenApi("/openapi/{documentName}.json");
}

api.MapPlatformHealthChecks();
api.MapSystemInfo();
api.MapModules();

// Invalid configuration makes RunAsync throw: the host logs "Hosting failed to start" (Critical)
// with the failing setting names, never their values, and the process exits non-zero.
await app.RunAsync();
return 0;

public partial class Program;
