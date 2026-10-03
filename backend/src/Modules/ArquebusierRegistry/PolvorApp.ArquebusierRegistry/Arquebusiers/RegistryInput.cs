using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Licenses;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ArquebusierRegistry.Arquebusiers;

/// <summary>The arquebusier fields as received: codes and dates arrive as text so errors name the field.</summary>
internal sealed record ArquebusierFields(
    int? FederationId,
    string? NationalId,
    string? FirstName,
    string? LastName,
    string? BirthDate,
    string? Email,
    string? Phone,
    string? Gender,
    string? Status,
    string? TrainingCompletedOn,
    LicenseFields? License);

/// <summary>The license fields as received; a null license means the arquebusier has none.</summary>
internal sealed record LicenseFields(string? Type, bool? Pending, string? IssuedOn, string? ExpiresOn);

/// <summary>Validated and normalised arquebusier fields, ready to store.</summary>
internal sealed record ArquebusierInput(
    int FederationId,
    string NationalId,
    string FirstName,
    string LastName,
    DateOnly BirthDate,
    string? Email,
    string? Phone,
    Gender Gender,
    ArquebusierStatus Status,
    DateOnly? TrainingCompletedOn,
    License? License);

/// <summary>The owned-weapon fields as received.</summary>
internal sealed record OwnedWeaponFields(Guid? WeaponModelId, string? WeaponNumber, string? OwnershipGuideNumber);

/// <summary>Validated owned-weapon fields; the guide number is upper-cased so uniqueness ignores case.</summary>
internal sealed record OwnedWeaponInput(Guid WeaponModelId, string WeaponNumber, string OwnershipGuideNumber);

/// <summary>
/// Blocking field rules of the registry (spec: Arquebusier data, National ID validation, Current
/// license, Training course, Active and Reserve status; design D5). Every invalid field is reported
/// at once by name with a reason code, so the form can mark them all. License fields are reported as
/// <c>license.&lt;field&gt;</c>.
/// </summary>
internal static class RegistryInput
{
    public const string Future = "future";
    public const string TooOld = "tooOld";
    public const string NotAfterIssued = "notAfterIssued";
    public const string DatesWhilePending = "datesWhilePending";

    public const int MaxFederationId = 999_999_999;

    private static readonly DateOnly EarliestBirthDate = new(1900, 1, 1);

    /// <summary>
    /// Validates <paramref name="fields"/> against <paramref name="today"/> (Europe/Madrid). A new
    /// arquebusier without status is <c>ACTIVE</c>; an edit replaces every field, so it must state the status.
    /// </summary>
    public static (ArquebusierInput? Input, IReadOnlyDictionary<string, string> Errors) Read(
        ArquebusierFields fields, DateOnly today, bool statusRequired)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        var federationId = FederationId(fields.FederationId, errors);
        var nationalId = NationalIdValue(fields.NationalId, errors);
        var firstName = InputFields.Text(fields.FirstName, "firstName", Arquebusier.NameMaxLength, errors);
        var lastName = InputFields.Text(fields.LastName, "lastName", Arquebusier.NameMaxLength, errors);
        var birthDate = BirthDate(fields.BirthDate, today, errors);
        var email = Email(fields.Email, errors);
        var phone = Phone(fields.Phone, errors);
        var gender = InputFields.RequiredCode<Gender>(fields.Gender, "gender", errors);
        var status = statusRequired
            ? InputFields.RequiredCode<ArquebusierStatus>(fields.Status, "status", errors)
            : InputFields.OptionalCode<ArquebusierStatus>(fields.Status, "status", errors) ?? ArquebusierStatus.Active;
        var training = NotInFuture(InputFields.OptionalDate(fields.TrainingCompletedOn, "trainingCompletedOn", errors), "trainingCompletedOn", today, errors);
        var license = fields.License is null ? null : LicenseValue(fields.License, today, errors);

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (new ArquebusierInput(
            federationId!.Value, nationalId!, firstName!, lastName!, birthDate!.Value, email, phone,
            gender!.Value, status!.Value, training, license), errors);
    }

    private static int? FederationId(int? value, Dictionary<string, string> errors)
    {
        if (value is null)
        {
            errors["federationId"] = InputFields.Required;
        }
        else if (value is < 1 or > MaxFederationId)
        {
            errors["federationId"] = InputFields.Invalid;
        }

        return value;
    }

    private static string? NationalIdValue(string? value, Dictionary<string, string> errors)
    {
        var (normalised, error) = NationalId.Parse(value);
        if (error is not null)
        {
            errors["nationalId"] = error;
        }

        return normalised;
    }

    private static DateOnly? BirthDate(string? value, DateOnly today, Dictionary<string, string> errors)
    {
        var date = NotInFuture(InputFields.RequiredDate(value, "birthDate", errors), "birthDate", today, errors);
        if (date < EarliestBirthDate)
        {
            errors["birthDate"] = TooOld;
            return null;
        }

        return date;
    }

    private static DateOnly? NotInFuture(DateOnly? date, string field, DateOnly today, Dictionary<string, string> errors)
    {
        if (date > today)
        {
            errors[field] = Future;
            return null;
        }

        return date;
    }

    /// <summary>Optional; stored trimmed and lower-cased so the database check and comparisons agree.</summary>
    private static string? Email(string? value, Dictionary<string, string> errors)
    {
        var email = value?.Trim();
        if (string.IsNullOrEmpty(email))
        {
            return null;
        }

        if (email.Length > Arquebusier.EmailMaxLength)
        {
            errors["email"] = InputFields.TooLong;
            return null;
        }

        if (!InputFields.IsPlainEmail(email))
        {
            errors["email"] = InputFields.Invalid;
            return null;
        }

        return email.ToLowerInvariant();
    }

    /// <summary>Optional; digits and single spaces with an optional leading <c>+</c>, at most 20 characters.</summary>
    private static string? Phone(string? value, Dictionary<string, string> errors)
    {
        if (value?.Length > Arquebusier.PhoneMaxLength * InputFields.MaxRawLengthFactor)
        {
            errors["phone"] = InputFields.TooLong;
            return null;
        }

        var phone = value is null ? null : string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrEmpty(phone))
        {
            return null;
        }

        var digits = phone.StartsWith('+') ? phone[1..] : phone;
        if (digits.Length == 0 || !digits.All(c => char.IsAsciiDigit(c) || c == ' '))
        {
            errors["phone"] = InputFields.Invalid;
            return null;
        }

        if (phone.Length > Arquebusier.PhoneMaxLength)
        {
            errors["phone"] = InputFields.TooLong;
            return null;
        }

        return phone;
    }

    private static License? LicenseValue(LicenseFields fields, DateOnly today, Dictionary<string, string> errors)
    {
        var type = InputFields.RequiredCode<LicenseType>(fields.Type, "license.type", errors);
        var issuedOn = InputFields.OptionalDate(fields.IssuedOn, "license.issuedOn", errors);
        var expiresOn = InputFields.OptionalDate(fields.ExpiresOn, "license.expiresOn", errors);
        if (fields.Pending is null)
        {
            errors["license.pending"] = InputFields.Required;
            return null;
        }

        if (fields.Pending.Value)
        {
            return PendingLicense(type, fields, errors);
        }

        // Each date is checked on its own, so every invalid one is named at once.
        if (string.IsNullOrEmpty(fields.IssuedOn))
        {
            errors["license.issuedOn"] = InputFields.Required;
        }
        else if (issuedOn > today)
        {
            errors["license.issuedOn"] = Future;
        }

        if (issuedOn is not null && expiresOn is not null && expiresOn <= issuedOn)
        {
            errors["license.expiresOn"] = NotAfterIssued;
        }

        if (type is null || issuedOn is null || errors.ContainsKey("license.issuedOn") || errors.ContainsKey("license.expiresOn"))
        {
            return null;
        }

        return new License(type.Value, Pending: false, issuedOn, expiresOn ?? License.DefaultExpiry(type.Value, issuedOn.Value));
    }

    private static License? PendingLicense(LicenseType? type, LicenseFields fields, Dictionary<string, string> errors)
    {
        var hasDates = false;
        if (!string.IsNullOrEmpty(fields.IssuedOn))
        {
            errors["license.issuedOn"] = DatesWhilePending;
            hasDates = true;
        }

        if (!string.IsNullOrEmpty(fields.ExpiresOn))
        {
            errors["license.expiresOn"] = DatesWhilePending;
            hasDates = true;
        }

        return type is null || hasDates ? null : new License(type.Value, Pending: true, null, null);
    }

    /// <summary>Spec "Owned weapons (UC-04)": the model is required, both numbers 1 to 30 characters.</summary>
    public static (OwnedWeaponInput? Input, IReadOnlyDictionary<string, string> Errors) Read(OwnedWeaponFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (fields.WeaponModelId is null)
        {
            errors["weaponModelId"] = InputFields.Required;
        }

        var weaponNumber = InputFields.Text(fields.WeaponNumber, "weaponNumber", OwnedWeapons.OwnedWeapon.NumberMaxLength, errors);
        var guide = InputFields.Text(fields.OwnershipGuideNumber, "ownershipGuideNumber", OwnedWeapons.OwnedWeapon.NumberMaxLength, errors);
        return errors.Count > 0
            ? (null, errors)
            : (new OwnedWeaponInput(fields.WeaponModelId!.Value, weaponNumber!, guide!.ToUpperInvariant()), errors);
    }
}
