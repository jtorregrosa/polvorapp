using System.Globalization;
using Microsoft.AspNetCore.Identity;
using PolvorApp.IdentityAccess.Users;

namespace PolvorApp.IdentityAccess.Security;

/// <summary>
/// Authenticator-app codes (RFC 6238) that are accepted at most once: the last accepted time step
/// is stored per user and equal or older steps are refused (design D5). Replaces Identity's
/// provider, which accepts the same code again within its window.
/// </summary>
internal sealed class ReplaySafeAuthenticatorTokenProvider(TimeProvider timeProvider) : IUserTwoFactorTokenProvider<User>
{
    public const string Name = "PolvorAppAuthenticator";
    private const string TokenLoginProvider = "PolvorApp";
    private const string LastStepTokenName = "LastTotpStep";

    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<User> manager, User user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return !string.IsNullOrWhiteSpace(await manager.GetAuthenticatorKeyAsync(user));
    }

    /// <summary>Codes come from the user's app; the server never generates them.</summary>
    public Task<string> GenerateAsync(string purpose, UserManager<User> manager, User user) => Task.FromResult(string.Empty);

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<User> manager, User user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        var key = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var step = Totp.MatchStep(Totp.DecodeBase32(key), token, timeProvider.GetUtcNow());
        if (step is null)
        {
            return false;
        }

        var last = await manager.GetAuthenticationTokenAsync(user, TokenLoginProvider, LastStepTokenName);
        if (long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var lastStep) && step <= lastStep)
        {
            return false;
        }

        // A failed write must not look like a wrong code (it would consume an attempt).
        await manager.SetAuthenticationTokenAsync(user, TokenLoginProvider, LastStepTokenName, step.Value.ToString(CultureInfo.InvariantCulture))
            .ThrowIfFailedAsync("Recording the last accepted code");
        return true;
    }

    /// <summary>Forgets the replay marker when the authenticator is reset.</summary>
    public static Task<IdentityResult> ForgetAsync(UserManager<User> manager, User user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.RemoveAuthenticationTokenAsync(user, TokenLoginProvider, LastStepTokenName);
    }
}
