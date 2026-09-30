using System.Text.Json.Serialization;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.IdentityAccess.Endpoints;

/// <summary>What the UI shows after a sign-in step (design D5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SignInStep>))]
internal enum SignInStep
{
    /// <summary>Ask for an authenticator code or a recovery code.</summary>
    [JsonStringEnumMemberName("SECOND_FACTOR")]
    SecondFactor,

    /// <summary>Enrol an authenticator before the first use (invitation, 2FA reset).</summary>
    [JsonStringEnumMemberName("ENROL")]
    Enrol,

    /// <summary>Signed in.</summary>
    [JsonStringEnumMemberName("DONE")]
    Done,
}

/// <param name="Email">Sign-in email.</param>
/// <param name="Password">Password.</param>
internal sealed record LoginRequest(string Email, string Password);

/// <param name="Next">The next step for the UI.</param>
internal sealed record LoginResponse(SignInStep Next);

/// <param name="Code">Six-digit authenticator code.</param>
/// <param name="RememberDevice">Skip the second factor on this browser for 30 days.</param>
internal sealed record SecondFactorRequest(string Code, bool RememberDevice);

/// <param name="Code">One unused recovery code.</param>
internal sealed record RecoveryCodeRequest(string Code);

/// <param name="Next">Always <c>DONE</c>.</param>
/// <param name="RecoveryCodesLeft">Unused recovery codes after this one.</param>
internal sealed record RecoveryCodeResponse(SignInStep Next, int RecoveryCodesLeft);

/// <param name="SharedKey">The key to type into an authenticator app, in groups of four.</param>
/// <param name="AuthenticatorUri">The <c>otpauth://</c> URI the UI renders as a QR code.</param>
internal sealed record EnrolmentResponse(string SharedKey, string AuthenticatorUri);

/// <param name="Code">A code from the newly configured authenticator.</param>
internal sealed record EnrolmentConfirmRequest(string Code);

/// <param name="RecoveryCodes">Ten single-use codes, shown once.</param>
internal sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

/// <param name="Email">The account's email.</param>
internal sealed record ForgotPasswordRequest(string Email);

/// <param name="UserId">The user from the emailed link.</param>
/// <param name="Token">The single-use token from the emailed link.</param>
/// <param name="Password">The new password.</param>
internal sealed record ResetPasswordRequest(Guid UserId, string Token, string Password);

/// <summary>The signed-in user's own account (spec: Account self-service).</summary>
/// <param name="Id">User identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Email">Sign-in email.</param>
/// <param name="Role">The user's role.</param>
/// <param name="Locale">Preferred UI and email language.</param>
/// <param name="RecoveryCodesLeft">Unused recovery codes.</param>
internal sealed record AccountResponse(Guid Id, string Name, string Email, UserRole Role, string Locale, int RecoveryCodesLeft);
