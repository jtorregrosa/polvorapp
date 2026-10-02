using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Modules;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Spec "First Admin bootstrap".</summary>
public sealed partial class CreateAdminCommandTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task On_an_empty_installation_the_first_admin_is_invited_and_the_email_is_not_logged()
    {
        const string email = "primera.admin@example.test";

        var exitCode = await RunAsync("--email", email, "--name", "Primera Admin", "--locale", "en");

        Assert.Equal(0, exitCode);
        Assert.Equal("PolvorApp invitation", (await mailpit.WaitForMessageAsync(email)).Subject);
        Assert.Contains(await _host.AuditEntriesAsync("UserInvited"), e => e.Data!.Contains("create-admin", StringComparison.Ordinal));
        Assert.DoesNotContain(_host.Factory.Logs.Entries, e => e.Message.Contains(email, StringComparison.OrdinalIgnoreCase));
        await using var scope = _host.Services.CreateAsyncScope();
        var created = (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email))!;
        Assert.Equal((UserRole.Admin, UserStatus.Invited, "en"), (created.Role, created.Status, created.Locale));
    }

    [Fact]
    public async Task Running_it_again_sends_a_new_link_and_the_previous_one_stops_working()
    {
        const string email = "admin.reenvio@example.test";
        Assert.Equal(0, await RunAsync("--email", email, "--name", "Admin Reenvío"));
        var first = await LinkAsync(email);

        Assert.Equal(0, await RunAsync("--email", email, "--name", "Admin Reenvío"));
        var second = await LinkAsync(email, token => token != first.Token);

        using var client = await _host.NewClientAsync();
        using var old = await client.PostAsync("/api/auth/invitations/accept", new { userId = first.UserId, token = first.Token, password = IdentityTestHost.Password });
        await AssertProblemAsync(old, HttpStatusCode.Gone, "auth.invalidLink");
        using var current = await client.PostAsync("/api/auth/invitations/accept", new { userId = second.UserId, token = second.Token, password = IdentityTestHost.Password });
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    [Fact]
    public async Task Nothing_is_kept_when_the_invitation_cannot_be_sent()
    {
        const string email = "admin.sin-correo@example.test";
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit, new Dictionary<string, string?>
        {
            ["Email:SmtpHost"] = "127.0.0.1",
            ["Email:SmtpPort"] = RejectingSmtpServer.ClosedPort().ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

        var exitCode = await host.Services.GetServices<IHostCommand>().Single(c => c.Verb == "create-admin")
            .RunAsync(["--email", email, "--name", "Admin Sin Correo"], TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        await using var scope = host.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email));
        Assert.Empty(await host.AuditEntriesAsync("UserInvited"));
    }

    [Fact]
    public async Task It_is_refused_once_an_admin_exists()
    {
        await _host.CreateUserAsync("admin.existente@example.test", UserRole.Admin);

        var exitCode = await RunAsync("--email", "intrusa@example.test", "--name", "Intrusa");

        Assert.Equal(1, exitCode);
        Assert.Contains(_host.Factory.Logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("refused", StringComparison.Ordinal));
        Assert.Equal(0, await mailpit.CountMessagesAsync("intrusa@example.test"));
    }

    [Fact]
    public async Task Invalid_arguments_create_nothing()
    {
        var exitCode = await RunAsync("--email", "no-es-email", "--name", "Nadie");

        Assert.Equal(2, exitCode);
    }

    private async Task<(Guid UserId, string Token)> LinkAsync(string email, Func<string, bool>? accept = null)
    {
        var message = await mailpit.WaitForMessageAsync(email, m =>
        {
            var link = InvitationLink().Match(m.Text);
            return link.Success && (accept is null || accept(Uri.UnescapeDataString(link.Groups["token"].Value)));
        });
        var match = InvitationLink().Match(message.Text);
        return (Guid.Parse(match.Groups["user"].Value), Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    [GeneratedRegex(@"/invitations/accept\?user=(?<user>[0-9a-f-]+)&token=(?<token>\S+)")]
    private static partial Regex InvitationLink();

    private Task<int> RunAsync(params string[] arguments) =>
        _host.Services.GetServices<IHostCommand>().Single(c => c.Verb == "create-admin")
            .RunAsync(arguments, TestContext.Current.CancellationToken);
}
