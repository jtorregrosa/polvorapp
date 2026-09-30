using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Users;

namespace PolvorApp.IdentityAccess.Security;

/// <summary>
/// Adds the role claim. The principal is rebuilt from the database on every request
/// (<c>ValidationInterval = 0</c>), so a role change applies on the user's next request.
/// </summary>
internal sealed class PolvorAppClaimsFactory(UserManager<User> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<User>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToCode()));
        return identity;
    }
}
