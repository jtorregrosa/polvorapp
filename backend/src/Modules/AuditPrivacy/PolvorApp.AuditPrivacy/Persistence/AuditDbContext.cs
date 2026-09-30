using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.AuditPrivacy.Persistence;

/// <summary>Owns the <c>audit</c> schema and the audit trail table and migrations (design D3).</summary>
internal sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AuditTrailModel.Schema);
        modelBuilder.AddAuditTrail(ownsTable: true);
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
internal sealed class AuditDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AuditDbContext>()
            .UseModuleDatabase("Host=design-time-only", AuditTrailModel.Schema)
            .Options);
}
