using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.SharedKernel.Hosting;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.IdentityAccess.Users;

/// <summary>
/// Synthetic users for development, staging and tests (SEC-11, design D10): one Admin, two
/// FiringChiefs, one invited and one deactivated user, all on the reserved <c>.example</c> domain.
/// Active users share the password and authenticator key from <c>Seed__UserPassword</c> and
/// <c>Seed__AuthenticatorKey</c>, so people and E2E tests can sign in. Runs only through the
/// guarded <c>seed</c> command; existing users are left untouched, so it can be run again.
/// </summary>
internal sealed partial class IdentitySeeder(
    UserManager<User> users,
    IConfiguration configuration,
    TimeProvider time,
    IHostEnvironment environment,
    ILogger<IdentitySeeder> logger) : IDataSeeder
{
    public const string PasswordKey = "Seed:UserPassword";
    public const string AuthenticatorKeyKey = "Seed:AuthenticatorKey";

    /// <summary>The values published in .env.example and compose.yaml; accepted only on developer machines and in tests.</summary>
    private static readonly string[] PublishedPlaceholders = ["local-only-seed-password", "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP"];

    /// <summary>At least 80 bits, as RFC 4226 recommends (the placeholder has 160).</summary>
    private const int MinKeyBytes = 10;

    /// <summary>Fixed identifiers, so every run produces the same data.</summary>
    internal static readonly IReadOnlyList<SyntheticUser> Users =
    [
        new(new Guid("0193a000-0000-7000-8000-000000000001"), "admin@polvorapp.example", "Admin Sintética", UserRole.Admin, "es-ES", SyntheticState.Active),
        new(new Guid("0193a000-0000-7000-8000-000000000002"), "jefe.uno@polvorapp.example", "Jefe Sintético Uno", UserRole.FiringChief, "ca-ES-valencia", SyntheticState.Active),
        new(new Guid("0193a000-0000-7000-8000-000000000003"), "jefa.dos@polvorapp.example", "Jefa Sintética Dos", UserRole.FiringChief, "es-ES", SyntheticState.Active),
        new(new Guid("0193a000-0000-7000-8000-000000000004"), "invitada@polvorapp.example", "Persona Invitada", UserRole.FiringChief, "en", SyntheticState.Invited),
        new(new Guid("0193a000-0000-7000-8000-000000000005"), "desactivada@polvorapp.example", "Persona Desactivada", UserRole.FiringChief, "es-ES", SyntheticState.Deactivated),
    ];

    public int Order => 10;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var password = Required(PasswordKey);
        var authenticatorKey = Required(AuthenticatorKeyKey).Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        if (!IsValidKey(authenticatorKey))
        {
            throw new InvalidOperationException($"The {AuthenticatorKeyKey} setting (Seed__AuthenticatorKey) must be a Base32 key of at least {MinKeyBytes} bytes.");
        }

        if (!LocalEnvironments.IsLocal(environment))
        {
            // Staging holds synthetic data but may be reachable: never with the published credentials,
            // and never on a database that holds anything but the synthetic users.
            if (PublishedPlaceholders.Contains(password, StringComparer.Ordinal) || PublishedPlaceholders.Contains(authenticatorKey, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"Outside Development and Testing, {PasswordKey} and {AuthenticatorKeyKey} must be secret values, not the published placeholders.");
            }

            var synthetic = Users.Select(u => u.Id).ToList();
            if (users.Users.Any(u => !synthetic.Contains(u.Id)))
            {
                throw new InvalidOperationException("The database holds users that are not synthetic; refusing to seed it (NFR-13).");
            }
        }

        foreach (var synthetic in Users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await users.FindByIdAsync(synthetic.Id.ToString()) is not null)
            {
                continue;
            }

            if (await users.FindByEmailAsync(synthetic.Email) is not null)
            {
                LogEmailTaken(logger, synthetic.Id);
                continue;
            }

            await CreateAsync(synthetic, password, authenticatorKey);
        }
    }

    private async Task CreateAsync(SyntheticUser synthetic, string password, string authenticatorKey)
    {
        var user = new User
        {
            Id = synthetic.Id,
            UserName = synthetic.Email,
            Email = synthetic.Email,
            Name = synthetic.Name,
            Role = synthetic.Role,
            Locale = synthetic.Locale,
            Active = synthetic.State != SyntheticState.Deactivated,
            CreatedAt = time.GetUtcNow(),
        };

        if (synthetic.State == SyntheticState.Invited)
        {
            await users.CreateAsync(user).ThrowIfFailedAsync($"Seeding {synthetic.Email}");
            return;
        }

        await users.CreateAsync(user, password).ThrowIfFailedAsync($"Seeding {synthetic.Email} (check {PasswordKey} against the password policy)");
        await users.SetAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey", authenticatorKey).ThrowIfFailedAsync("Seeding the authenticator key");
        await users.SetTwoFactorEnabledAsync(user, true).ThrowIfFailedAsync("Seeding two-factor authentication");
    }

    private static bool IsValidKey(string key)
    {
        try
        {
            return Totp.DecodeBase32(key).Length >= MinKeyBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private string Required(string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The {key} setting ({key.Replace(":", "__", StringComparison.Ordinal)}) is required to seed users.")
            : value;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Synthetic user {UserId} skipped: its email is already used by another user")]
    private static partial void LogEmailTaken(ILogger logger, Guid userId);

    internal enum SyntheticState
    {
        Active,
        Invited,
        Deactivated,
    }

    internal sealed record SyntheticUser(Guid Id, string Email, string Name, UserRole Role, string Locale, SyntheticState State);
}
