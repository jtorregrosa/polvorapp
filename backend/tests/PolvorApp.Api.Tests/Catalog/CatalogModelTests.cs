using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>Design D3: the catalog context owns schema <c>catalog</c>, writes audit entries but never migrates them.</summary>
public sealed class CatalogModelTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new("Host=offline");
    private AsyncServiceScope _scope;

    public ValueTask InitializeAsync()
    {
        _scope = _factory.Services.CreateAsyncScope();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _factory.DisposeAsync();
    }

    [Fact]
    public void The_catalog_context_excludes_the_audit_table_from_its_migrations()
    {
        var entry = Model<FederationCatalogDbContext>().FindEntityType(typeof(AuditEntry))!;

        Assert.Equal((AuditTrailModel.Schema, AuditTrailModel.Table), (entry.GetSchema(), entry.GetTableName()));
        Assert.True(entry.IsTableExcludedFromMigrations());
        Assert.Equal(Columns(Model<AuditDbContext>().FindEntityType(typeof(AuditEntry))!), Columns(entry));
    }

    [Fact]
    public void The_catalog_tables_live_in_the_catalog_schema()
    {
        var tables = Model<FederationCatalogDbContext>().GetEntityTypes()
            .Where(e => !e.IsTableExcludedFromMigrations())
            .Select(e => $"{e.GetSchema()}.{e.GetTableName()}")
            .Order(StringComparer.Ordinal);

        Assert.Equal(["catalog.comparsas", "catalog.firing_chief_assignments", "catalog.weapon_models"], tables);
    }

    private IModel Model<TContext>()
        where TContext : DbContext =>
        _scope.ServiceProvider.GetRequiredService<TContext>().GetService<IDesignTimeModel>().Model;

    private static List<string> Columns(IEntityType entity)
    {
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        return [.. entity.GetProperties()
            .Select(p => $"{p.GetColumnName(table)}:{p.GetColumnType()}:{p.IsNullable}:{p.GetMaxLength()}")
            .Order(StringComparer.Ordinal)];
    }
}
