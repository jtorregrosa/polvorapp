namespace PolvorApp.ComplianceInsights.Contracts;

/// <summary>
/// What the compliance rules need to know about one arquebusier: no name, identifier or contact data
/// (design D2). Each caller builds it from its own data. It is never logged: its text form prints no
/// value, because the birth date is personal data.
/// </summary>
/// <param name="BirthDate">The birth date; the age is derived from it on the reference date.</param>
/// <param name="License">The current license, or null when the arquebusier has none.</param>
/// <param name="TrainingCompletedOn">The course date, or null when the course is not done.</param>
/// <param name="HasIdPhoto">Whether the arquebusier has an ID photo.</param>
public sealed record ComplianceFacts(
    DateOnly BirthDate,
    ComplianceLicense? License,
    DateOnly? TrainingCompletedOn,
    bool HasIdPhoto)
{
    /// <summary>The type name only: the members are personal data.</summary>
    public override string ToString() => nameof(ComplianceFacts);
}

/// <summary>
/// The current license as the rules see it: either <see cref="Pending"/>, which has no dates (spec:
/// Current license), or <see cref="Issued"/>, which has an expiry date and may have its photos.
/// Closed to these two cases, so every value is a valid license.
/// </summary>
public abstract record ComplianceLicense
{
    private ComplianceLicense()
    {
    }

    /// <summary>Applied for but not issued yet: no dates, and its photos do not matter to the rules.</summary>
    public sealed record Pending : ComplianceLicense;

    /// <summary>An issued license, valid through <paramref name="ExpiresOn"/> in Europe/Madrid.</summary>
    public sealed record Issued(DateOnly ExpiresOn, bool HasFrontPhoto, bool HasBackPhoto) : ComplianceLicense
    {
        /// <summary>The type name only: the expiry date is personal data.</summary>
        public override string ToString() => nameof(Issued);
    }
}
