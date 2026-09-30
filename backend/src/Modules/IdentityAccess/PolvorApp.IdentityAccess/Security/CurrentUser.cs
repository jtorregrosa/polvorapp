using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.IdentityAccess.Security;

/// <summary>The signed-in user from the application cookie's principal.</summary>
internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal =>
        httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;

    public bool IsAuthenticated => UserId is not null;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public UserRole? Role => UserId is null ? null : UserRoleCodes.FromCode(Principal?.FindFirstValue(ClaimTypes.Role));
}
