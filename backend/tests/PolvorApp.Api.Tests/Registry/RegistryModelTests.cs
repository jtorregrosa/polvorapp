using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.OwnedWeapons;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>Design D3: the registry context owns schema <c>registry</c>, writes audit entries but never migrates them.</summary>
public sealed class RegistryModelTests : IAsyncLifetime
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
    public void The_registry_context_excludes_the_audit_table_from_its_migrations()
    {
        var entry = Model<ArquebusierRegistryDbContext>().FindEntityType(typeof(AuditEntry))!;

        Assert.Equal((AuditTrailModel.Schema, AuditTrailModel.Table), (entry.GetSchema(), entry.GetTableName()));
        Assert.True(entry.IsTableExcludedFromMigrations());
        Assert.Equal(Columns(Model<AuditDbContext>().FindEntityType(typeof(AuditEntry))!), Columns(entry));
    }

    [Fact]
    public void The_registry_tables_live_in_the_registry_schema()
    {
        var tables = Model<ArquebusierRegistryDbContext>().GetEntityTypes()
            .Where(e => !e.IsTableExcludedFromMigrations())
            .Select(e => $"{e.GetSchema()}.{e.GetTableName()}")
            .Order(StringComparer.Ordinal);

        Assert.Equal(["registry.arquebusier_photos", "registry.arquebusiers", "registry.owned_weapons", "registry.registry_settings"], tables);
    }

    [Fact]
    public void Rows_carry_xmin_as_their_version()
    {
        var model = Model<ArquebusierRegistryDbContext>();

        foreach (var type in new[] { typeof(Arquebusier), typeof(OwnedWeapon) })
        {
            var version = model.FindEntityType(type)!.FindProperty("Version")!;
            Assert.True(version.IsConcurrencyToken, type.Name);
            Assert.Equal("xmin", version.GetColumnName());
        }
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
