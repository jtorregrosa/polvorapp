using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Design D2/D3: the identity context writes audit entries but never migrates the audit table.</summary>
public sealed class IdentityModelTests : IAsyncLifetime
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
    public void The_identity_context_excludes_the_audit_table_from_its_migrations()
    {
        var entry = Entity<IdentityAccessDbContext>();

        Assert.Equal((AuditTrailModel.Schema, AuditTrailModel.Table), (entry.GetSchema(), entry.GetTableName()));
        Assert.True(entry.IsTableExcludedFromMigrations());
    }

    [Fact]
    public void The_audit_mapping_is_identical_in_the_owning_and_the_writing_context()
    {
        Assert.Equal(Columns(Entity<AuditDbContext>()), Columns(Entity<IdentityAccessDbContext>()));
    }

    private IEntityType Entity<TContext>()
        where TContext : DbContext =>
        _scope.ServiceProvider.GetRequiredService<TContext>().GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AuditEntry))!;

    private static List<string> Columns(IEntityType entity)
    {
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        return [.. entity.GetProperties()
            .Select(p => $"{p.GetColumnName(table)}:{p.GetColumnType()}:{p.IsNullable}:{p.GetMaxLength()}")
            .Order(StringComparer.Ordinal)];
    }
}
