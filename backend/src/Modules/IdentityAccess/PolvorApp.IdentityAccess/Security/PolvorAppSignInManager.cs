using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolvorApp.IdentityAccess.Users;

namespace PolvorApp.IdentityAccess.Security;

/// <summary>
/// Identity's sign-in manager with two PolvorApp rules (design D5, D6): deactivated users can never
/// sign in, and every session carries the time of its original sign-in (<c>auth_time</c>) so the
/// 12-hour absolute limit survives cookie renewal and password changes.
/// </summary>
internal sealed class PolvorAppSignInManager(
    UserManager<User> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<User> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<User>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<User> confirmation,
    TimeProvider timeProvider)
    : SignInManager<User>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public const string AuthTimeClaim = "auth_time";

    /// <summary>When a device was remembered; the 30-day limit counts from here, not from the last use.</summary>
    public const string RememberedAtItem = "rememberedAt";

    private bool _refreshing;

    public override async Task<bool> CanSignInAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Active && await base.CanSignInAsync(user);
    }

    /// <summary>
    /// The session check on every request (design D6): besides the security stamp, the user must
    /// still be active and have two-factor authentication, even if some path forgot the stamp.
    /// </summary>
    public override async Task<User?> ValidateSecurityStampAsync(ClaimsPrincipal? principal)
    {
        var user = await base.ValidateSecurityStampAsync(principal);
        return user is { Active: true, TwoFactorEnabled: true } ? user : null;
    }

    /// <summary>Keeps the original sign-in time when the same session is re-issued (e.g. after a password change).</summary>
    public override async Task RefreshSignInAsync(User user)
    {
        _refreshing = true;
        try
        {
            await base.RefreshSignInAsync(user);
        }
        finally
        {
            _refreshing = false;
        }
    }

    public override Task SignInWithClaimsAsync(User user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(additionalClaims);
        var claims = additionalClaims.Where(c => c.Type != AuthTimeClaim).ToList();
        var existing = _refreshing ? Context.User.FindFirst(AuthTimeClaim) : null;
        claims.Add(existing is not null
            ? new Claim(AuthTimeClaim, existing.Value)
            : new Claim(AuthTimeClaim, timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        return base.SignInWithClaimsAsync(user, authenticationProperties, claims);
    }

    /// <summary>Remembers the browser for 30 days from now; renewals never extend it (spec: Remembered devices).</summary>
    public override async Task RememberTwoFactorClientAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = new ClaimsIdentity(IdentityConstants.TwoFactorRememberMeScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Id.ToString()));
        identity.AddClaim(new Claim(Options.ClaimsIdentity.SecurityStampClaimType, await UserManager.GetSecurityStampAsync(user)));
        var properties = new AuthenticationProperties { IsPersistent = true };
        properties.Items[RememberedAtItem] = timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        await Context.SignInAsync(IdentityConstants.TwoFactorRememberMeScheme, new ClaimsPrincipal(identity), properties);
    }

    /// <summary>
    /// Puts the browser in the "password verified, second factor or enrolment pending" state, the
    /// way Identity does internally, without signing in (design D5).
    /// </summary>
    public Task SignInPendingSecondStepAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = new ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Id.ToString()));
        // Bound to the stamp: a password reset, 2FA reset or sign-out-everywhere cancels a pending step.
        identity.AddClaim(new Claim(Options.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp ?? string.Empty));
        return Context.SignInAsync(IdentityConstants.TwoFactorUserIdScheme, new ClaimsPrincipal(identity));
    }

    /// <summary>The password-verified user, only while the pending step's stamp is still current.</summary>
    public override async Task<User?> GetTwoFactorAuthenticationUserAsync()
    {
        var user = await base.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return null;
        }

        var pending = await Context.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        var stamp = pending.Principal?.FindFirstValue(Options.ClaimsIdentity.SecurityStampClaimType);
        return stamp is not null && stamp == user.SecurityStamp ? user : null;
    }

    public Task SignOutPendingSecondStepAsync() => Context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);

    /// <summary>Reads a sign-in time claim; null when missing or malformed.</summary>
    public static DateTimeOffset? ReadAuthTime(ClaimsPrincipal? principal) =>
        long.TryParse(principal?.FindFirstValue(AuthTimeClaim), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
