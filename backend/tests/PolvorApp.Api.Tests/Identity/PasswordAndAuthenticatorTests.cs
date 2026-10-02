using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Specs "Password policy" and "Sign-in with two-factor authentication" (replay-safe codes).</summary>
public sealed class PasswordAndAuthenticatorTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData("corta12345", "PasswordTooShort")]
    [InlineData("password1234", PasswordPolicyValidator.TooCommon)]
    [InlineData("Password1234", PasswordPolicyValidator.TooCommon)]
    [InlineData("persona.clave@example.test", PasswordPolicyValidator.IsEmail)]
    public async Task Weak_passwords_are_refused(string password, string expectedError)
    {
        var email = password.Contains('@', StringComparison.Ordinal) ? password : $"debil.{Guid.NewGuid():N}@example.test";
        var errors = await SetPasswordAsync(email, password);

        Assert.Contains(expectedError, errors);
    }

    [Fact]
    public async Task A_too_long_password_is_refused()
    {
        var errors = await SetPasswordAsync("larga@example.test", new string('x', PasswordPolicyValidator.MaxLength + 1));

        Assert.Contains(PasswordPolicyValidator.TooLong, errors);
    }

    [Theory]
    [InlineData("solo minusculas sin numeros")]
    [InlineData("polvora y arcabuces")]
    public async Task Long_passwords_need_no_character_classes(string password)
    {
        var errors = await SetPasswordAsync($"clases.{password.Length}@example.test", password);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Passwords_are_stored_only_as_hashes()
    {
        var user = await _host.CreateUserAsync("hash@example.test");

        await using var scope = _host.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(user.Id.ToString());
        Assert.NotNull(stored!.PasswordHash);
        Assert.DoesNotContain(IdentityTestHost.Password, stored.PasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_authenticator_code_is_accepted_only_once()
    {
        var user = await _host.CreateUserAsync("replay@example.test");
        var code = _host.NextCode(user);

        await using var scope = _host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var stored = (await users.FindByIdAsync(user.Id.ToString()))!;

        Assert.True(await users.VerifyTwoFactorTokenAsync(stored, ReplaySafeAuthenticatorTokenProvider.Name, code));
        Assert.False(await users.VerifyTwoFactorTokenAsync(stored, ReplaySafeAuthenticatorTokenProvider.Name, code));
        Assert.True(await users.VerifyTwoFactorTokenAsync(stored, ReplaySafeAuthenticatorTokenProvider.Name, _host.NextCode(user)));
    }

    [Fact]
    public async Task A_wrong_code_is_refused()
    {
        var user = await _host.CreateUserAsync("wrong-code@example.test");
        var code = _host.NextCode(user);
        var wrong = ((int.Parse(code, System.Globalization.CultureInfo.InvariantCulture) + 1) % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

        await using var scope = _host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        Assert.False(await users.VerifyTwoFactorTokenAsync((await users.FindByIdAsync(user.Id.ToString()))!, ReplaySafeAuthenticatorTokenProvider.Name, wrong));
    }

    private async Task<List<string>> SetPasswordAsync(string email, string password)
    {
        var user = await _host.CreateUserAsync(email, withPassword: false, enrolled: false);
        await using var scope = _host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var result = await users.AddPasswordAsync((await users.FindByIdAsync(user.Id.ToString()))!, password);
        return [.. result.Errors.Select(e => e.Code)];
    }
}
