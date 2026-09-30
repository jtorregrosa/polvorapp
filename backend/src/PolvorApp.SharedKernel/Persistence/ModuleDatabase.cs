using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.SharedKernel.Persistence;

/// <summary>
/// Persistence conventions for every module (ADR-0001; backend/src/Modules/README.md): one DbContext
/// per module, one PostgreSQL schema per module with its own migrations-history table, snake_case
/// names, and the append-only audit guard.
/// </summary>
public static class ModuleDatabase
{
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>Configures a module context on the host's shared data source.</summary>
    public static TBuilder UseModuleDatabase<TBuilder>(this TBuilder options, NpgsqlDataSource dataSource, string schema)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(options);
        options.UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema));
        return ApplyConventions(options);
    }

    /// <summary>Configures a module context from a connection string (design-time factories only).</summary>
    public static TBuilder UseModuleDatabase<TBuilder>(this TBuilder options, string connectionString, string schema)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(options);
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema));
        return ApplyConventions(options);
    }

    private static TBuilder ApplyConventions<TBuilder>(TBuilder options)
        where TBuilder : DbContextOptionsBuilder
    {
        options.UseSnakeCaseNamingConvention().AddInterceptors(AppendOnlyAuditGuard.Instance);
        return options;
    }

    /// <summary>
    /// Registers a module DbContext on the host's <see cref="NpgsqlDataSource"/> and its migrator.
    /// Migrators run in ascending <paramref name="migrationOrder"/> (the audit trail first).
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, string schema, int migrationOrder)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddDbContext<TContext>((provider, options) =>
            options.UseModuleDatabase(provider.GetRequiredService<NpgsqlDataSource>(), schema));
        services.AddSingleton<IDatabaseMigrator>(new DbContextMigrator<TContext>(migrationOrder));
        return services;
    }
}
