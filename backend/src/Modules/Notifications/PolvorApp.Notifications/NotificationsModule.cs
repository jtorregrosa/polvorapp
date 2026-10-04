using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Emails;
using PolvorApp.Notifications.Endpoints;
using PolvorApp.Notifications.Persistence;
using PolvorApp.Notifications.Preferences;
using PolvorApp.Notifications.Scheduling;
using PolvorApp.Notifications.Seeding;
using PolvorApp.Notifications.Sending;
using PolvorApp.SharedKernel.Modules;
using PolvorApp.SharedKernel.Persistence;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.Notifications;

/// <summary>
/// Capability notifications (UC-23): the license digest, the order window and order status emails and
/// the milestone reminders, each kind turned on or off by its user, recorded with the change that
/// causes it and sent in the background (change add-notifications).
/// </summary>
public sealed class NotificationsModule : IModule
{
    /// <summary>After every module it reads (60 is distribution); it has no cross-schema foreign keys.</summary>
    public const int MigrationOrder = 70;

    /// <summary>
    /// Build-time OpenAPI generation launches the entry point through <c>GetDocument.Insider</c>: no
    /// background work then (the host's <c>BuildTimeDocumentGeneration</c> is not visible to modules).
    /// </summary>
    private static readonly bool IsBuildTimeDocumentGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddModuleDbContext<NotificationsDbContext>(NotificationsDbContext.Schema, MigrationOrder);

        services.AddSingleton<IValidateOptions<NotificationsOptions>, NotificationsOptionsValidator>();
        var options = services.AddOptions<NotificationsOptions>().Configure(o => NotificationsOptions.Bind(o, configuration));
        if (!IsBuildTimeDocumentGeneration)
        {
            options.ValidateOnStart();
        }

        services.AddLocalization();
        services.AddSingleton<INotificationOutbox, NotificationOutbox>();
        services.AddSingleton<NotificationEmails>();
        services.AddScoped<NotificationPreferences>();
        services.AddScoped<IDataSeeder, NotificationSeeder>();

        services.AddScoped<EventExpander>();
        services.AddScoped<DeliveryContents>();
        services.AddScoped<DeliverySender>();
        services.AddScoped<ScheduledNotifications>();
        services.AddSingleton<NotificationPump>();
        services.AddSingleton<NotificationDispatcher>();
        services.AddSingleton<NotificationScheduler>();
        services.AddSingleton<IHostCommand, SendNotificationsCommand>();
        if (!IsBuildTimeDocumentGeneration)
        {
            // Both read Notifications:Enabled; test hosts turn them off and call their runs directly.
            services.AddHostedService(provider => provider.GetRequiredService<NotificationDispatcher>());
            services.AddHostedService(provider => provider.GetRequiredService<NotificationScheduler>());
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapNotificationPreferenceEndpoints();
}
