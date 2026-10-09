using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>
/// Design D5: a module that stores personal data takes part in GDPR requests. A table owned by a module
/// with a column that identifies a person or links to one must have its module's participant, so a new
/// table cannot be forgotten by an export or an erasure.
/// </summary>
public sealed class PersonalDataCoverageTests
{
    /// <summary>Columns that hold a person's data or point to a person (snake_case, as stored).</summary>
    private static readonly HashSet<string> PersonalColumns =
    [
        "national_id", "lender_national_id", "first_name", "last_name", "lender_first_name", "lender_last_name", "email",
        "phone", "birth_date", "user_id", "actor_user_id", "holder_entry_id", "proxy_entry_id", "object_key",
    ];

    /// <summary>
    /// Every table with personal data and the participant that exports and erases it. A new table with a
    /// personal column fails the test until it is listed here with the participant that handles it.
    /// </summary>
    private static readonly Dictionary<string, string> HandledBy = new()
    {
        ["audit.audit_entries"] = "AuditPersonalData",
        ["catalog.firing_chief_assignments"] = "CatalogPersonalData",
        ["distribution.handovers"] = "DistributionPersonalData",
        ["distribution.pickup_proxies"] = "DistributionPersonalData",
        ["identity.user_claims"] = "IdentityPersonalData",
        ["identity.user_logins"] = "IdentityPersonalData",
        ["identity.user_tokens"] = "IdentityPersonalData",
        ["identity.users"] = "IdentityPersonalData",
        ["notifications.notification_deliveries"] = "NotificationsPersonalData",
        ["notifications.notification_opt_outs"] = "NotificationsPersonalData",
        ["orders.edition_entries"] = "OrdersPersonalData",
        ["orders.weapon_loans"] = "OrdersPersonalData",
        ["registry.arquebusier_photos"] = "RegistryPersonalData",
        ["registry.arquebusiers"] = "RegistryPersonalData",
    };

    [Fact]
    public async Task Every_table_with_personal_data_is_handled_by_a_participant_of_its_module()
    {
        await using var factory = new ApiFactory("Host=offline");
        await using var scope = factory.Services.CreateAsyncScope();
        var participants = scope.ServiceProvider.GetServices<IPersonalDataParticipant>().ToDictionary(p => p.GetType().Name);

        var tables = ModuleContexts(scope.ServiceProvider)
            .SelectMany(context => PersonalTables(context).Select(table => (Table: table, Module: context.GetType().Assembly)))
            .ToList();

        Assert.Equal(HandledBy.Keys.Order(StringComparer.Ordinal), tables.Select(t => t.Table).Order(StringComparer.Ordinal));
        Assert.All(tables, t => Assert.Equal(t.Module, participants[HandledBy[t.Table]].GetType().Assembly));
    }

    [Fact]
    public async Task The_participants_run_in_the_documented_order()
    {
        await using var factory = new ApiFactory("Host=offline");
        await using var scope = factory.Services.CreateAsyncScope();

        var order = scope.ServiceProvider.GetServices<IPersonalDataParticipant>().OrderBy(p => p.Order).Select(p => p.GetType().Name);

        Assert.Equal(
            ["RegistryPersonalData", "OrdersPersonalData", "DistributionPersonalData", "IdentityPersonalData", "CatalogPersonalData",
             "NotificationsPersonalData", "AuditPersonalData"],
            order);
    }

    private static IEnumerable<DbContext> ModuleContexts(IServiceProvider services) =>
        services.GetServices<IDatabaseMigrator>()
            .Select(migrator => migrator.GetType().GetGenericArguments()[0])
            .Select(type => (DbContext)services.GetRequiredService(type));

    private static IEnumerable<string> PersonalTables(DbContext context) =>
        context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null && !entity.IsTableExcludedFromMigrations())
            .Where(entity => entity.GetProperties().Any(p => p.GetColumnName() is { } column && PersonalColumns.Contains(column)))
            .Select(entity => $"{entity.GetSchema()}.{entity.GetTableName()}")
            .Distinct();
}
