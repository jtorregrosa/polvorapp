using System.Collections.Frozen;
using Microsoft.AspNetCore.Identity;
using PolvorApp.IdentityAccess.Users;

namespace PolvorApp.IdentityAccess.Security;

/// <summary>
/// Spec "Password policy": at most 128 characters, not the user's email and not a commonly used
/// password. The 12-character minimum is Identity's own <c>RequiredLength</c>; no character-class
/// rules are imposed. Error codes are culture-independent; the UI translates them.
/// </summary>
internal sealed class PasswordPolicyValidator : IPasswordValidator<User>
{
    public const int MaxLength = 128;
    public const string TooLong = "PasswordTooLong";
    public const string IsEmail = "PasswordIsEmail";
    public const string TooCommon = "PasswordTooCommon";

    private static readonly Lazy<FrozenSet<string>> CommonPasswords = new(LoadCommonPasswords);

    public Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string? password)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrEmpty(password))
        {
            // Identity's own validator reports the missing/short password.
            return Task.FromResult(IdentityResult.Success);
        }

        var errors = new List<IdentityError>();
        if (password.Length > MaxLength)
        {
            errors.Add(new IdentityError { Code = TooLong, Description = $"Passwords have at most {MaxLength} characters." });
        }

        if (user.Email is not null && string.Equals(password.Trim(), user.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new IdentityError { Code = IsEmail, Description = "The password cannot be the email address." });
        }

        if (IsCommon(password))
        {
            errors.Add(new IdentityError { Code = TooCommon, Description = "The password is too common." });
        }

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]));
    }

    internal static bool IsCommon(string password) => CommonPasswords.Value.Contains(password.ToLowerInvariant());

    private static FrozenSet<string> LoadCommonPasswords()
    {
        using var stream = typeof(PasswordPolicyValidator).Assembly.GetManifestResourceStream("PolvorApp.IdentityAccess.CommonPasswords.txt")
            ?? throw new InvalidOperationException("The common-password list is not embedded.");
        using var reader = new StreamReader(stream);
        var entries = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                entries.Add(line);
            }
        }

        return entries.ToFrozenSet(StringComparer.Ordinal);
    }
}
