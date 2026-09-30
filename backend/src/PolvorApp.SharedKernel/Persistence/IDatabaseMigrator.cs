using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PolvorApp.SharedKernel.Persistence;

/// <summary>Applies one module's migrations; run by the host <c>migrate</c> command, never on web startup.</summary>
public interface IDatabaseMigrator
{
    /// <summary>Execution order across modules; lower runs first.</summary>
    int Order { get; }

    /// <summary>Name for logs (the context type).</summary>
    string Name { get; }

    Task MigrateAsync(IServiceProvider scopedServices, CancellationToken cancellationToken);
}

internal sealed class DbContextMigrator<TContext>(int order) : IDatabaseMigrator
    where TContext : DbContext
{
    public int Order => order;

    public string Name => typeof(TContext).Name;

    public Task MigrateAsync(IServiceProvider scopedServices, CancellationToken cancellationToken) =>
        scopedServices.GetRequiredService<TContext>().Database.MigrateAsync(cancellationToken);
}
