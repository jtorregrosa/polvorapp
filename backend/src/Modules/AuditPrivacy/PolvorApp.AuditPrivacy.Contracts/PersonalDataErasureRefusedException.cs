namespace PolvorApp.AuditPrivacy.Contracts;

/// <summary>
/// A participant refuses an erasure for a blocking rule (spec: Erasing a user's data), e.g. an Admin
/// erasing themselves. The whole erasure is rolled back and answered <c>409 Conflict</c> with
/// <see cref="Code"/>.
/// </summary>
public sealed class PersonalDataErasureRefusedException : Exception
{
    /// <summary>Erasing your own account.</summary>
    public const string SelfErasure = "privacy.selfErasure";

    /// <summary>The user is already erased.</summary>
    public const string AlreadyErased = "privacy.alreadyErased";

    /// <summary>The user is the last active Admin.</summary>
    public const string LastAdmin = "privacy.lastAdmin";

    public PersonalDataErasureRefusedException()
        : this(string.Empty)
    {
    }

    public PersonalDataErasureRefusedException(string code)
        : base($"The erasure is refused: {code}.") => Code = code;

    public PersonalDataErasureRefusedException(string message, Exception innerException)
        : base(message, innerException) => Code = string.Empty;

    /// <summary>The problem code the API returns.</summary>
    public string Code { get; }
}
