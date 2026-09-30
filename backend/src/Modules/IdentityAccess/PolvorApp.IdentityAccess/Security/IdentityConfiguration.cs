using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Hosting;

namespace PolvorApp.IdentityAccess.Security;

/// <summary>
/// ASP.NET Core Identity for PolvorApp (ADR-0004; design D5, D6): invitation-only users, lockout,
/// replay-safe TOTP, and HttpOnly SameSite=Strict cookies that answer the SPA with 401/403.
/// </summary>
internal static class IdentityConfiguration
{
    public const string SessionCookieName = "polvorapp.session";
    public const string SecondStepCookieName = "polvorapp.2fa";
    public const string RememberedDeviceCookieName = "polvorapp.device";

    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan AbsoluteLimit = TimeSpan.FromHours(12);
    public static readonly TimeSpan RememberedDeviceLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan SecondStepLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public const int MaxFailedAttempts = 5;

    /// <summary>PBKDF2-HMAC-SHA512 iterations (OWASP recommendation, 2023).</summary>
    public const int PasswordHashIterations = 210_000;
    public const int MinPasswordLength = 12;

    public static IServiceCollection AddPolvorAppIdentity(this IServiceCollection services)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.AddIdentityCore<User>(ConfigureIdentity)
            .AddSignInManager<PolvorAppSignInManager>()
            .AddClaimsPrincipalFactory<PolvorAppClaimsFactory>()
            .AddEntityFrameworkStores<IdentityAccessDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<ReplaySafeAuthenticatorTokenProvider>(ReplaySafeAuthenticatorTokenProvider.Name)
            .AddTokenProvider<InvitationTokenProvider>(InvitationTokenProvider.ProviderName)
            .AddPasswordValidator<PasswordPolicyValidator>();

        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = PasswordResetLifetime);
        services.AddOptions<InvitationTokenProviderOptions>();
        services.Configure<SecurityStampValidatorOptions>(o =>
        {
            // Deactivation, role change, password change and 2FA reset apply on the next request.
            o.ValidationInterval = TimeSpan.Zero;
            o.OnRefreshingPrincipal = KeepSessionClaims;
        });

        services.AddDataProtection().SetApplicationName("PolvorApp").PersistKeysToDbContext<IdentityAccessDbContext>();

        ConfigureCookie(services, IdentityConstants.ApplicationScheme, SessionCookieName, o =>
        {
            o.ExpireTimeSpan = IdleTimeout;
            o.SlidingExpiration = true;
            o.Events.OnRedirectToLogin = context => Status(context, StatusCodes.Status401Unauthorized);
            o.Events.OnRedirectToAccessDenied = context => Status(context, StatusCodes.Status403Forbidden);
            o.Events.OnValidatePrincipal = ValidateSessionAsync;
        });
        ConfigureCookie(services, IdentityConstants.TwoFactorUserIdScheme, SecondStepCookieName, o =>
        {
            o.ExpireTimeSpan = SecondStepLifetime;
            o.SlidingExpiration = false;
        });
        ConfigureCookie(services, IdentityConstants.TwoFactorRememberMeScheme, RememberedDeviceCookieName, o =>
        {
            o.ExpireTimeSpan = RememberedDeviceLifetime;
            o.SlidingExpiration = false;
            o.Events.OnValidatePrincipal = ValidateRememberedDeviceAsync;
        });
        services.Configure<PasswordHasherOptions>(o => o.IterationCount = PasswordHashIterations);
        ConfigureCookie(services, IdentityConstants.ExternalScheme, "polvorapp.external", _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireAuthenticatedUser().RequireRole(UserRoleCodes.Admin));
        return services;
    }

    private static void ConfigureIdentity(IdentityOptions options)
    {
        options.Password.RequiredLength = MinPasswordLength;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = MaxFailedAttempts;
        options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;

        // The user name is the email; any address character is allowed.
        options.User.RequireUniqueEmail = true;
        options.User.AllowedUserNameCharacters = string.Empty;

        options.Tokens.AuthenticatorTokenProvider = ReplaySafeAuthenticatorTokenProvider.Name;
    }

    private static void ConfigureCookie(IServiceCollection services, string scheme, string name, Action<CookieAuthenticationOptions> configure)
    {
        services.AddOptions<CookieAuthenticationOptions>(scheme).Configure<IHostEnvironment>((options, environment) =>
        {
            options.Cookie.Name = name;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            // Compose serves plain http on localhost; everywhere else cookies are Secure (SEC-01).
            options.Cookie.SecurePolicy = LocalEnvironments.IsLocal(environment) ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            configure(options);
        });
    }

    private static Task Status(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        // The API has no server-rendered pages: the SPA gets a status code, never a redirect.
        context.Response.StatusCode = statusCode;
        return Task.CompletedTask;
    }

    /// <summary>Absolute session limit, then Identity's security-stamp check (design D6).</summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var signedInAt = PolvorAppSignInManager.ReadAuthTime(context.Principal);
        if (signedInAt is null || now - signedInAt.Value > AbsoluteLimit)
        {
            context.RejectPrincipal();
            return;
        }

        await SecurityStampValidator.ValidatePrincipalAsync(context);
    }

    /// <summary>
    /// The device is remembered for 30 days from the moment it was remembered: the stamp check
    /// renews the cookie on every use, so the limit is kept in the ticket (design D6).
    /// </summary>
    private static async Task ValidateRememberedDeviceAsync(CookieValidatePrincipalContext context)
    {
        var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var remembered = context.Properties.Items.TryGetValue(PolvorAppSignInManager.RememberedAtItem, out var value)
            && long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : (DateTimeOffset?)null;
        if (remembered is null || now - remembered.Value > RememberedDeviceLifetime)
        {
            context.RejectPrincipal();
            return;
        }

        await SecurityStampValidator.ValidateAsync<ITwoFactorSecurityStampValidator>(context);
    }

    /// <summary>The principal is rebuilt on every request; keep how and when the session started.</summary>
    private static Task KeepSessionClaims(SecurityStampRefreshingPrincipalContext context)
    {
        if (context.NewPrincipal?.Identity is ClaimsIdentity identity && context.CurrentPrincipal is { } current)
        {
            foreach (var type in new[] { PolvorAppSignInManager.AuthTimeClaim, "amr" })
            {
                if (current.FindFirst(type) is { } claim && identity.FindFirst(type) is null)
                {
                    identity.AddClaim(new Claim(type, claim.Value));
                }
            }
        }

        return Task.CompletedTask;
    }
}
