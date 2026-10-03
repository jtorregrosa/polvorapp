using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ComparsaOrders.Entries;

/// <summary>The entry fields as received: codes arrive as text, so errors name the field.</summary>
internal sealed record EntryFields(
    string? Status,
    int? PowderKg,
    int? CapsBoxes,
    string? CapsType,
    string? WeaponSource,
    Guid? OwnedWeaponId,
    Guid? RentalWeaponModelId,
    LoanFields? Loan,
    string? Flask);

/// <summary>The loan as received: a registered owner's weapon, or an external owner.</summary>
internal sealed record LoanFields(Guid? OwnedWeaponId, ExternalLenderFields? External);

/// <summary>An external owner and their weapon as received.</summary>
internal sealed record ExternalLenderFields(
    string? FirstName,
    string? LastName,
    string? NationalId,
    Guid? WeaponModelId,
    string? WeaponNumber,
    string? OwnershipGuideNumber);

/// <summary>A validated loan (spec: Weapon loans (UC-13, BR-09)).</summary>
internal abstract record LoanInput
{
    private LoanInput()
    {
    }

    /// <summary>A registered arquebusier's owned weapon; the owner and the weapon are checked against the registry.</summary>
    public sealed record Registered(Guid OwnedWeaponId) : LoanInput;

    /// <summary>The entry's current loan, unchanged: the request sent no lender for an entry that has one.</summary>
    public sealed record Keep : LoanInput;

    /// <summary>An owner who is not in PolvorApp, with the national ID normalised and the guide upper-cased.</summary>
    public sealed record External(
        string FirstName,
        string LastName,
        string NationalId,
        Guid WeaponModelId,
        string WeaponNumber,
        string OwnershipGuideNumber) : LoanInput
    {
        /// <summary>The type name only: the members are personal data.</summary>
        public override string ToString() => nameof(External);
    }
}

/// <summary>What the field rules need to know about the entry being edited.</summary>
/// <param name="OwnedWeaponIds">The arquebusier's owned weapons; null when they are no longer in the registry.</param>
/// <param name="OfferedModelIds">The models offered for rental in the edition (BR-07).</param>
/// <param name="KeepsRemovedWeapon">
/// The entry is <c>OWNED</c> and its weapon left the registry: it may stay so, with its history copy,
/// when the request sends no weapon (spec: Entries after registry changes).
/// </param>
/// <param name="HasLoan">The entry has a loan, which a request without a lender keeps.</param>
internal sealed record EntryContext(
    IReadOnlySet<Guid>? OwnedWeaponIds,
    IReadOnlySet<Guid> OfferedModelIds,
    bool KeepsRemovedWeapon = false,
    bool HasLoan = false);

/// <summary>
/// Blocking field rules of an entry (spec: Edition entries (BR-05, BR-07); design D8). Every invalid
/// field is reported at once by name with a reason code; loan fields as <c>loan.&lt;field&gt;</c>.
/// Values of another weapon source are ignored. The checks that need other records (the lender's
/// weapon, an external owner's model, a registered national ID) are made by the administration.
/// </summary>
internal sealed record EntryInput(EntryValues Values, LoanInput? Loan)
{
    public const string OutOfRange = "outOfRange";
    public const string Reserve = "reserve";
    public const string NotOwned = "notOwned";
    public const string NotOffered = "notOffered";
    public const string OwnWeapon = "ownWeapon";

    public static (EntryInput? Input, Dictionary<string, string> Errors) Read(EntryFields fields, EntryContext context)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(context);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        var status = InputFields.RequiredCode<ArquebusierStatus>(fields.Status, "status", errors);
        var powder = Range(fields.PowderKg, "powderKg", EditionEntry.MaxPowderKg, errors);
        var caps = Range(fields.CapsBoxes, "capsBoxes", EditionEntry.MaxCapsBoxes, errors);
        var capsType = InputFields.OptionalCode<CapsType>(fields.CapsType, "capsType", errors);
        var source = InputFields.RequiredCode<WeaponSource>(fields.WeaponSource, "weaponSource", errors);
        var flask = InputFields.RequiredCode<FlaskOption>(fields.Flask, "flask", errors);

        if (caps > 0 && capsType is null && !errors.ContainsKey("capsType"))
        {
            errors["capsType"] = InputFields.Required;
        }
        else if (caps == 0 && capsType is not null)
        {
            errors["capsType"] = InputFields.Invalid;
        }

        if (status == ArquebusierStatus.Reserve)
        {
            ReserveRules(powder, caps, source, flask, errors);
        }

        var (owned, rental, loan) = source switch
        {
            WeaponSource.Owned => (OwnedWeapon(fields.OwnedWeaponId, context, errors), (Guid?)null, (LoanInput?)null),
            WeaponSource.Rental => (null, RentalModel(fields.RentalWeaponModelId, context, errors), null),
            WeaponSource.Loan => (null, null, ReadLoan(fields.Loan, context, errors)),
            _ => ((Guid?)null, (Guid?)null, (LoanInput?)null),
        };

        if (errors.Count > 0 || status is not { } s || powder is not { } kg || caps is not { } boxes || source is not { } src || flask is not { } f)
        {
            return (null, errors);
        }

        return (new EntryInput(new EntryValues(s, kg, boxes, capsType, src, owned, rental, f), loan), errors);
    }

    private static int? Range(int? value, string field, int max, Dictionary<string, string> errors)
    {
        if (value is null)
        {
            errors[field] = InputFields.Required;
            return null;
        }

        if (value < 0 || value > max)
        {
            errors[field] = OutOfRange;
            return null;
        }

        return value;
    }

    /// <summary>BR-05: a <c>RESERVE</c> entry has no powder, caps, weapon or flask.</summary>
    private static void ReserveRules(int? powder, int? caps, WeaponSource? source, FlaskOption? flask, Dictionary<string, string> errors)
    {
        if (powder > 0)
        {
            errors["powderKg"] = Reserve;
        }

        if (caps > 0)
        {
            errors["capsBoxes"] = Reserve;
        }

        if (source is { } s && s != WeaponSource.None)
        {
            errors["weaponSource"] = Reserve;
        }

        if (flask is { } f && f != FlaskOption.None)
        {
            errors["flask"] = Reserve;
        }
    }

    private static Guid? OwnedWeapon(Guid? id, EntryContext context, Dictionary<string, string> errors)
    {
        if (id is not { } weapon)
        {
            if (!context.KeepsRemovedWeapon)
            {
                errors["ownedWeaponId"] = InputFields.Required;
            }

            return null;
        }

        if (context.OwnedWeaponIds is not { } owned || !owned.Contains(weapon))
        {
            errors["ownedWeaponId"] = NotOwned;
            return null;
        }

        return weapon;
    }

    private static Guid? RentalModel(Guid? id, EntryContext context, Dictionary<string, string> errors)
    {
        if (id is not { } model)
        {
            errors["rentalWeaponModelId"] = InputFields.Required;
            return null;
        }

        if (!context.OfferedModelIds.Contains(model))
        {
            errors["rentalWeaponModelId"] = NotOffered;
            return null;
        }

        return model;
    }

    private static LoanInput? ReadLoan(LoanFields? fields, EntryContext context, Dictionary<string, string> errors)
    {
        switch (fields)
        {
            case null or { OwnedWeaponId: null, External: null } when context.HasLoan:
                return new LoanInput.Keep();
            case null or { OwnedWeaponId: null, External: null }:
                errors["loan"] = InputFields.Required;
                return null;
            case { OwnedWeaponId: not null, External: not null }:
                errors["loan"] = InputFields.Invalid;
                return null;
            case { OwnedWeaponId: { } weapon }:
                if (context.OwnedWeaponIds?.Contains(weapon) == true)
                {
                    errors["loan.ownedWeaponId"] = OwnWeapon;
                    return null;
                }

                return new LoanInput.Registered(weapon);
            default:
                return ReadExternal(fields.External!, errors);
        }
    }

    private static LoanInput.External? ReadExternal(ExternalLenderFields fields, Dictionary<string, string> errors)
    {
        var firstName = InputFields.Text(fields.FirstName, "loan.firstName", EditionEntry.NameMaxLength, errors);
        var lastName = InputFields.Text(fields.LastName, "loan.lastName", EditionEntry.NameMaxLength, errors);
        var nationalId = NationalId.Parse(fields.NationalId);
        if (nationalId.Error is { } nationalIdError)
        {
            errors["loan.nationalId"] = nationalIdError;
        }

        if (fields.WeaponModelId is null)
        {
            errors["loan.weaponModelId"] = InputFields.Required;
        }

        var number = InputFields.Text(fields.WeaponNumber, "loan.weaponNumber", EditionEntry.WeaponNumberMaxLength, errors);
        var guide = InputFields.Text(fields.OwnershipGuideNumber, "loan.ownershipGuideNumber", EditionEntry.WeaponNumberMaxLength, errors);
        return firstName is null || lastName is null || nationalId.Value is null || fields.WeaponModelId is not { } model || number is null || guide is null
            ? null
            : new LoanInput.External(firstName, lastName, nationalId.Value, model, number, guide.ToUpperInvariant());
    }
}
