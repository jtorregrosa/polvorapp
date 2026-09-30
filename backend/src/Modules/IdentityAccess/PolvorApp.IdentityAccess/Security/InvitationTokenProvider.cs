using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolvorApp.IdentityAccess.Users;

namespace PolvorApp.IdentityAccess.Security;

/// <summary>Invitation links live 7 days (spec: Invitation-only accounts).</summary>
internal sealed class InvitationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InvitationTokenProviderOptions()
    {
        Name = InvitationTokenProvider.ProviderName;
        TokenLifespan = TimeSpan.FromDays(7);
    }
}

/// <summary>
/// Single-use invitation tokens (design D4). Tokens embed the security stamp: resending,
/// deactivating and setting the password update it, so older links stop working.
/// </summary>
internal sealed class InvitationTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<InvitationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<User>> logger)
    : DataProtectorTokenProvider<User>(dataProtectionProvider, options, logger)
{
    public const string ProviderName = "Invitation";
    public const string Purpose = "Invitation";
}
