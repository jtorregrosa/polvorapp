namespace PolvorApp.IdentityAccess.Security;

/// <summary>Where ASP.NET Core Identity's user store keeps the authenticator key and recovery codes.</summary>
internal static class IdentityTokens
{
    public const string StoreProvider = "[AspNetUserStore]";
    public const string AuthenticatorKey = "AuthenticatorKey";
    public const string RecoveryCodes = "RecoveryCodes";
}
