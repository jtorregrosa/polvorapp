using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy;
using PolvorApp.AuditPrivacy.Viewer;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Persistence;

/// <summary>Design D2: the audit action catalogue that every module contributes to.</summary>
public sealed class AuditActionCatalogTests
{
    private static readonly string[] SecurityCodes =
    [
        "AdminBootstrapRefused", "LoanLenderLookedUp", "LockedOut", "PasswordResetRequested",
        "PersonLookedUp", "RecoveryCodeUsed", "SignInFailed", "SignedIn",
    ];

    [Fact]
    public void The_catalogue_is_the_union_of_the_sources_ordered_by_code()
    {
        var catalog = new AuditActionCatalog(
        [
            Source(new AuditActionDefinition("WidgetRenamed", "Widget")),
            Source(new AuditActionDefinition("GadgetCreated", "Gadget"), new AuditActionDefinition("GadgetChecked", "Gadget", AuditRetentionClass.Security)),
        ]);

        Assert.Equal(["GadgetChecked", "GadgetCreated", "WidgetRenamed"], catalog.Actions.Select(a => a.Code));
        Assert.Equal(["GadgetChecked"], catalog.SecurityActions);
        Assert.True(catalog.ContainsEntityType("Gadget"));
        Assert.False(catalog.ContainsEntityType("Gizmo"));
    }

    [Fact]
    public void A_code_declared_twice_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new AuditActionCatalog(
            [Source(new AuditActionDefinition("WidgetRenamed", "Widget")), Source(new AuditActionDefinition("WidgetRenamed", "Widget"))]));

        Assert.Contains("WidgetRenamed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_undeclared_code_is_kept_for_the_standard_period()
    {
        var catalog = new AuditActionCatalog([Source(new AuditActionDefinition("SignedIn", "User", AuditRetentionClass.Security))]);

        Assert.Equal(AuditRetentionClass.Security, catalog.RetentionOf("SignedIn"));
        Assert.Equal(AuditRetentionClass.Standard, catalog.RetentionOf("RenamedLongAgo"));
    }

    [Fact]
    public async Task The_host_catalogue_declares_every_module_and_the_security_codes()
    {
        await using var factory = new ApiFactory("Host=offline");

        var catalog = factory.Services.GetRequiredService<AuditActionCatalog>();

        Assert.Equal(SecurityCodes, catalog.SecurityActions.Order(StringComparer.Ordinal));
        foreach (var code in new[]
        {
            "UserUpdated", "ArquebusierDeleted", "ComparsaCreated", "EditionCreated", "ComparsaOrderValidated",
            "PickupProxyAuthorised", "ExportDownloaded", "NotificationPreferencesChanged", "PersonalDataErased", "AuditEntriesPurged",
        })
        {
            Assert.True(catalog.Contains(code), code);
        }
    }

    /// <summary>
    /// Every action constant of every module's <c>*AuditActions</c> class is declared, so a code a call
    /// site uses but a module forgot to list fails here instead of as a 500 at run time (review, 9.2).
    /// </summary>
    [Fact]
    public async Task Every_action_constant_of_the_modules_is_in_the_host_catalogue()
    {
        await using var factory = new ApiFactory("Host=offline");
        var catalog = factory.Services.GetRequiredService<AuditActionCatalog>();

        var constants = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.StartsWith("PolvorApp.", StringComparison.Ordinal) == true)
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
                // The modules' `*AuditActions` classes, and the `AuditAction` constants of their documents.
                .Where(field => type.Name.EndsWith("AuditActions", StringComparison.Ordinal) || field.Name == "AuditAction")
                .Select(field => (Name: $"{type.Name}.{field.Name}", Code: (string)field.GetRawConstantValue()!)))
            .ToList();

        // `*EntityType` constants name the records, the rest the actions.
        static bool IsEntityType(string name) => name.EndsWith("EntityType", StringComparison.Ordinal);
        Assert.True(constants.Count > 50, $"Only {constants.Count} constants were found.");
        Assert.Empty(constants
            .Where(c => IsEntityType(c.Name) ? !catalog.ContainsEntityType(c.Code) : !catalog.Contains(c.Code))
            .Select(c => c.Name));
    }

    [Fact]
    public void Recording_an_undeclared_action_is_a_programming_error()
    {
        var catalog = new AuditActionCatalog([Source(new AuditActionDefinition("WidgetCreated", "Widget"))]);
        var trail = new AuditTrail(new HttpContextAccessor(), TimeProvider.System, NullLogger<AuditTrail>.Instance, catalog);
        using var context = WidgetContext.Offline();

        trail.Record(context, new AuditRecord("WidgetCreated", "Widget"));
        var error = Assert.Throws<InvalidOperationException>(() => trail.Record(context, new AuditRecord("WidgetMelted", "Widget")));

        Assert.Contains("WidgetMelted", error.Message, StringComparison.Ordinal);
    }

    private static TestSource Source(params AuditActionDefinition[] actions) => new(actions);

    private sealed record TestSource(IReadOnlyList<AuditActionDefinition> Actions) : IAuditActionSource;
}
