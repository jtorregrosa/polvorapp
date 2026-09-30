using System.Net.Mail;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Localization;

namespace PolvorApp.IdentityAccess.Users;

/// <summary>Validation of user fields; failures name the field with a reason code (spec: Users and roles).</summary>
internal static class UserInput
{
    public const int MaxEmailLength = 254;
    public const string Required = "required";
    public const string Invalid = "invalid";
    public const string TooLong = "tooLong";

    public static void Email(string? value, IDictionary<string, string> errors)
    {
        var email = value?.Trim();
        if (string.IsNullOrEmpty(email))
        {
            errors["email"] = Required;
        }
        else if (email.Length > MaxEmailLength)
        {
            errors["email"] = TooLong;
        }
        else if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || !parsed.Host.Contains('.', StringComparison.Ordinal)
            || email.Any(c => c > '~' || char.IsWhiteSpace(c) || char.IsControl(c) || "\"[]\\,;:()<>".Contains(c, StringComparison.Ordinal)))
        {
            // Plain ASCII addresses only: the value is the sign-in name and the SMTP recipient.
            errors["email"] = Invalid;
        }
    }

    public static void Name(string? value, IDictionary<string, string> errors)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            errors["name"] = Required;
        }
        else if (name.Length > User.NameMaxLength)
        {
            errors["name"] = TooLong;
        }
        else if (name.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format
            or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator
            or System.Globalization.UnicodeCategory.PrivateUse or System.Globalization.UnicodeCategory.OtherNotAssigned))
        {
            // No line breaks or bidi controls: the name is printed in emails sent by the Federation.
            errors["name"] = Invalid;
        }
    }

    public static UserRole? Role(string? value, IDictionary<string, string> errors)
    {
        var role = UserRoleCodes.FromCode(value);
        if (role is null)
        {
            errors["role"] = value is null ? Required : Invalid;
        }

        return role;
    }

    public static void Locale(string? value, IDictionary<string, string> errors)
    {
        if (!SupportedLocales.IsSupported(value))
        {
            errors["locale"] = value is null ? Required : Invalid;
        }
    }
}
