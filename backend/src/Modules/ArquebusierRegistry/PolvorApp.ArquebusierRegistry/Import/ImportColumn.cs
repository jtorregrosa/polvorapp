namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>The columns of the import template, in template order (spec: Import template).</summary>
internal enum ImportColumn
{
    FederationId,
    LastName,
    FirstName,
    NationalId,
    BirthDate,
    Gender,
    Email,
    Phone,
    Status,
    LicenseType,
    LicenseIssuedOn,
    LicenseExpiresOn,
    TrainingCompletedOn,
}

/// <summary>What each column holds: its field key in errors, whether it is required and whether it is a date.</summary>
internal static class ImportColumns
{
    public static readonly IReadOnlyList<ImportColumn> All = Enum.GetValues<ImportColumn>();

    /// <summary>The field name a registration error uses for the column, so the UI translates both alike.</summary>
    public static string Field(ImportColumn column) => column switch
    {
        ImportColumn.FederationId => "federationId",
        ImportColumn.LastName => "lastName",
        ImportColumn.FirstName => "firstName",
        ImportColumn.NationalId => "nationalId",
        ImportColumn.BirthDate => "birthDate",
        ImportColumn.Gender => "gender",
        ImportColumn.Email => "email",
        ImportColumn.Phone => "phone",
        ImportColumn.Status => "status",
        ImportColumn.LicenseType => "license.type",
        ImportColumn.LicenseIssuedOn => "license.issuedOn",
        ImportColumn.LicenseExpiresOn => "license.expiresOn",
        ImportColumn.TrainingCompletedOn => "trainingCompletedOn",
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Unknown import column."),
    };

    /// <summary>A file without a required column cannot be read at all (<c>missingColumns</c>).</summary>
    public static bool IsRequired(ImportColumn column) =>
        column is ImportColumn.FederationId or ImportColumn.LastName or ImportColumn.FirstName
            or ImportColumn.NationalId or ImportColumn.BirthDate or ImportColumn.Gender;

    public static bool IsDate(ImportColumn column) =>
        column is ImportColumn.BirthDate or ImportColumn.LicenseIssuedOn or ImportColumn.LicenseExpiresOn
            or ImportColumn.TrainingCompletedOn;
}
