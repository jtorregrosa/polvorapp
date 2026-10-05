using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.Api.Tests.Notifications;

/// <summary>
/// Specs "Email content" and "Federation settings" (add-federation-settings, design D4) end to end:
/// a reminder pumped through the real SMTP sender to Mailpit carries the settings' sender name,
/// reply-to and footer.
/// </summary>
public sealed class NotificationSenderSettingsTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    private IServiceProvider Services => _orders.Services;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit);
        await Services.OptOutOfEverythingAsync(_orders.Registry.AdminId, _orders.Registry.FiringChiefId);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task A_reminder_goes_out_with_the_sender_name_reply_to_and_footer_of_the_settings()
    {
        var admin = await _orders.Host.CreateUserAsync("admin.remitente.ajustes@example.test", UserRole.Admin);
        await using (var scope = Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>().Database.ExecuteSqlRawAsync(
                "UPDATE catalog.federation_settings SET sender_name = 'Unión Sintética · PolvorApp', reply_to = 'secretaria@federacion.example', short_name = 'Unión Sintética', contact_email = 'info@federacion.example'",
                TestContext.Current.CancellationToken);
        }

        await Services.SaveEditionsAsync(new CalendarMilestone
        {
            Id = Guid.CreateVersion7(),
            EditionId = _orders.Current.Id,
            Date = new DateOnly(2031, 1, 3),
            Title = "Plazo sintético del remitente",
            Notify = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await Services.RunScheduledAsync(new DateOnly(2031, 1, 1));
        await Services.PumpAsync();

        var message = await mailpit.WaitForMessageAsync(admin.Email);
        Assert.Equal(new MailpitAddress("Unión Sintética · PolvorApp", "no-reply@polvorapp.example"), message.From);
        Assert.Equal([new MailpitAddress(string.Empty, "secretaria@federacion.example")], message.ReplyTo);
        Assert.Contains("Unión Sintética\nContacto: info@federacion.example", message.Text.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
