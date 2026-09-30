using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>
/// The read contract other modules use to look up users (change add-federation-catalog, design D2):
/// name, email, role and derived status, never credentials.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class UserDirectoryTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task An_unknown_user_is_not_found()
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit);
        await using var scope = host.Services.CreateAsyncScope();

        var found = await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
            .FindAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        Assert.Null(found);
    }

    [Fact]
    public async Task Users_are_returned_with_role_and_derived_status_and_unknown_ids_are_skipped()
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit);
        var active = await host.CreateUserAsync("directorio.activo@example.test");
        var invited = await host.CreateUserAsync("directorio.invitado@example.test", withPassword: false, enrolled: false);
        var deactivated = await host.CreateUserAsync("directorio.baja@example.test", active: false);
        var admin = await host.CreateUserAsync("directorio.admin@example.test", UserRole.Admin);
        await using var scope = host.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var found = await directory.FindManyAsync(
            [active.Id, invited.Id, deactivated.Id, admin.Id, Guid.CreateVersion7()], TestContext.Current.CancellationToken);

        Assert.Equal(4, found.Count);
        var byId = found.ToDictionary(u => u.Id);
        Assert.Equal(new UserSummary(active.Id, "Persona Sintética directorio.activo", active.Email, UserRole.FiringChief, UserStatus.Active), byId[active.Id]);
        Assert.Equal(UserStatus.Invited, byId[invited.Id].Status);
        Assert.Equal(UserStatus.Deactivated, byId[deactivated.Id].Status);
        Assert.Equal(UserRole.Admin, byId[admin.Id].Role);

        var single = await directory.FindAsync(invited.Id, TestContext.Current.CancellationToken);
        Assert.Equal(byId[invited.Id], single);
    }

    [Fact]
    public async Task Asking_for_no_users_returns_none()
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit);
        await using var scope = host.Services.CreateAsyncScope();

        var found = await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
            .FindManyAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(found);
    }

    [Fact]
    public async Task Duplicate_ids_return_each_user_once_and_deactivated_users_are_found()
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit);
        var deactivated = await host.CreateUserAsync("directorio.repetido@example.test", active: false);
        await using var scope = host.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var found = await directory.FindManyAsync([deactivated.Id, deactivated.Id], TestContext.Current.CancellationToken);
        var single = await directory.FindAsync(deactivated.Id, TestContext.Current.CancellationToken);

        Assert.Equal(deactivated.Id, Assert.Single(found).Id);
        Assert.Equal(UserStatus.Deactivated, single?.Status);
    }

    [Fact]
    public async Task A_null_id_list_is_rejected()
    {
        await using var host = await IdentityTestHost.StartAsync(postgres, mailpit);
        await using var scope = host.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => directory.FindManyAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_summary_exposes_no_credentials_or_security_data()
    {
        Assert.Equal(
            ["Email", "Id", "Name", "Role", "Status"],
            typeof(UserSummary).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
    }
}
