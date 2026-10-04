using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Definitions;
using PolvorApp.Exports.Endpoints;
using PolvorApp.Exports.Writers;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Modules;

namespace PolvorApp.Exports;

/// <summary>
/// Capability exports (UC-17; SEC-05, SEC-06; ADR-0008): the files the Federation sends to the powder
/// supplier, the rental company and the Arms Authority, and each comparsa's list, as Excel and PDF.
/// It owns definitions and writers, not data: no schema, no migrations (change add-exports, design D1).
/// Other modules render their documents through <see cref="IDocumentRenderer"/> (add-distribution-planning).
/// </summary>
public sealed class ExportsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuditActions(ExportsAuditActions.All);
        // Fonts and license once, at start-up: a missing font fails the start, not a user's export.
        PdfSetup.EnsureApplied();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IExportDefinition, PowderSupplierExport>();
        services.AddSingleton<IExportDefinition, RentalCompanyExport>();
        services.AddSingleton<IExportDefinition, ArmsAuthorityExport>();
        services.AddSingleton<IExportDefinition, ComparsaListExport>();
        services.AddScoped<ExportDataLoader>();
        services.AddScoped<ExportService>();
        services.AddSingleton<IDocumentRenderer, DocumentRenderer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapExportEndpoints();
}
