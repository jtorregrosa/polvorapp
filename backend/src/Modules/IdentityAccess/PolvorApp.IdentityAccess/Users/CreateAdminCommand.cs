using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Emails;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Email;
using PolvorApp.SharedKernel.Localization;
using PolvorApp.SharedKernel.Modules;

namespace PolvorApp.IdentityAccess.Users;

/// <summary>
/// <c>create-admin --email &lt;email&gt; --name &lt;name&gt; [--locale es-ES]</c>: invites the first
/// Admin of an installation (spec: First Admin bootstrap). Refused once an Admin has set a
/// password, so it cannot be used to gain access to an initialised installation. Run again with
/// the same email while that Admin is still only invited, it sends a new link (the old one stops
/// working). Changes are only kept if the invitation email was sent. Logs never contain the email
/// address (NFR-12).
/// </summary>
internal sealed partial class CreateAdminCommand(IServiceScopeFactory scopes, ILogger<CreateAdminCommand> logger) : IHostCommand
{
    public string Verb => "create-admin";

    public async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var options = Parse(arguments);
        var errors = new Dictionary<string, string>();
        UserInput.Email(options.GetValueOrDefault("--email"), errors);
        UserInput.Name(options.GetValueOrDefault("--name"), errors);
        var locale = options.GetValueOrDefault("--locale") ?? SupportedLocales.Spanish;
        UserInput.Locale(locale, errors);
        if (errors.Count > 0)
        {
            LogUsage(logger, string.Join(", ", errors.Select(e => $"{e.Key}: {e.Value}")));
            return 2;
        }

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<IdentityAccessDbContext>();
        var users = services.GetRequiredService<UserManager<User>>();

        await using var transaction = await db.LockAdminsAsync(null, cancellationToken);
        var trail = services.GetRequiredService<IAuditTrail>();
        if (await db.Users.AnyAsync(u => u.Role == UserRole.Admin && u.PasswordHash != null, cancellationToken))
        {
            LogRefused(logger);
            trail.Record(db, new AuditRecord(AdminBootstrapRefused, SecurityEvents.EntityType, Data: new { source = Verb }, Anonymous: true));
            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            return 1;
        }

        var email = options["--email"]!.Trim();
        var user = await users.FindByEmailAsync(email);
        if (user is { Role: UserRole.Admin, Status: UserStatus.Invited })
        {
            await users.UpdateSecurityStampAsync(user).ThrowIfFailedAsync("Invalidating the previous invitation");
            trail.Record(db, SecurityEvents.InvitationResent, user, new { source = Verb });
        }
        else if (user is null)
        {
            user = new User
            {
                UserName = email,
                Email = email,
                Name = options["--name"]!.Trim(),
                Role = UserRole.Admin,
                Locale = locale,
                CreatedAt = services.GetRequiredService<TimeProvider>().GetUtcNow(),
            };
            await users.CreateAsync(user).ThrowIfFailedAsync("Creating the first Admin");
            trail.Record(db, SecurityEvents.UserInvited, user, new { role = UserRoleCodes.Admin, locale, source = Verb });
        }
        else
        {
            LogEmailInUse(logger);
            return 1;
        }

        var token = await users.GenerateUserTokenAsync(user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose);
        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            await services.GetRequiredService<IdentityEmails>().SendInvitationAsync(user, token, cancellationToken);
        }
        catch (EmailDeliveryException)
        {
            // Rolled back: without the email nobody could use the account, and the command could not be rerun.
            LogEmailFailed(logger);
            return 1;
        }

        await transaction.CommitAsync(CancellationToken.None);
        LogInvited(logger);
        return 0;
    }

    /// <summary>Audit action of a refused bootstrap: using it on an initialised installation is a security event.</summary>
    public const string AdminBootstrapRefused = "AdminBootstrapRefused";

    private static Dictionary<string, string?> Parse(IReadOnlyList<string> arguments)
    {
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].StartsWith("--", StringComparison.Ordinal))
            {
                options[arguments[i]] = i + 1 < arguments.Count ? arguments[++i] : null;
            }
        }

        return options;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "create-admin: invalid arguments ({Errors}). Usage: create-admin --email <email> --name <name> [--locale es-ES|ca-ES-valencia|en]")]
    private static partial void LogUsage(ILogger logger, string errors);

    [LoggerMessage(Level = LogLevel.Error, Message = "create-admin refused: an Admin can already sign in; invite further users from the application")]
    private static partial void LogRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "create-admin: the email belongs to a user who is not an invited Admin; nothing was changed")]
    private static partial void LogEmailInUse(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "create-admin: the invitation email could not be sent; nothing was created")]
    private static partial void LogEmailFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "create-admin: invitation sent to the new Admin")]
    private static partial void LogInvited(ILogger logger);
}
