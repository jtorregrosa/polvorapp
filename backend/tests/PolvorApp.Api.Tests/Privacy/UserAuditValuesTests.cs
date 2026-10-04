using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>
/// Design D8/D9 regression: after every user-management and security action, no audit data holds a
/// user's name or email, except the attempted email of a failed sign-in for an unknown account (kept a
/// year and redacted when that user is erased).
/// </summary>
public sealed class UserAuditValuesTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private const string Name = "Persona Sintética Vigilada";
    private const string Email = "vigilada.auditoria@example.test";
    private IdentityTestHost _host = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.vigilante@example.test", UserRole.Admin));
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task No_user_action_leaves_a_name_or_an_email_in_the_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        using (var invited = await _admin.PostAsync("/api/users", new { email = Email, name = Name, role = "FIRING_CHIEF", locale = "es-ES" }))
        {
            Assert.True(invited.IsSuccessStatusCode);
        }

        var enrolled = await _host.CreateUserAsync("vigilada.activa@example.test");
        foreach (var request in new Func<Task<HttpResponseMessage>>[]
        {
            () => _admin.PutAsJsonAsync($"/api/users/{enrolled.Id}", new { name = Name + " Cambiada", role = "ADMIN", locale = "en" }, ct),
            () => _admin.PostAsync($"/api/users/{enrolled.Id}/deactivate", null, ct),
            () => _admin.PostAsync($"/api/users/{enrolled.Id}/reactivate", null, ct),
            () => _admin.PostAsync($"/api/users/{enrolled.Id}/two-factor/reset", null, ct),
        })
        {
            using var response = await request();
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(ct));
        }

        var signedIn = await _host.CreateUserAsync("vigilada.sesion@example.test");
        using (var session = await _host.SignInAsync(signedIn))
        using (var locale = await session.PutAsJsonAsync("/api/account/locale", new { locale = "en" }, ct))
        {
            Assert.True(locale.IsSuccessStatusCode);
        }

        using (var anonymous = await _host.NewClientAsync())
        {
            using var wrong = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = signedIn.Email, password = "contraseña-equivocada-1" }, ct);
            using var unknown = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = "nadie.existe@example.test", password = "contraseña-equivocada-1" }, ct);
        }

        await using var scope = _host.Services.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Set<AuditEntry>().AsNoTracking()
            .Where(e => e.Data != null).ToListAsync(ct);
        var leaks = entries
            .Where(e => !(e.Action == "SignInFailed" && e.EntityId == null))
            .Where(e => new[] { Name, Email, enrolled.Email, signedIn.Email }.Any(value => e.Data!.Contains(value, StringComparison.OrdinalIgnoreCase)))
            .Select(e => e.Action)
            .ToList();

        Assert.Empty(leaks);
        Assert.Contains(entries, e => e.Action == "SignInFailed" && e.EntityId == null
            && JsonDocument.Parse(e.Data!).RootElement.GetProperty("attemptedEmail").GetString() == "NADIE.EXISTE@EXAMPLE.TEST");
    }
}
