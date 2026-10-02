namespace PolvorApp.SharedKernel.Security;

/// <summary>Rate-limit policy names defined by the host; modules put them on their endpoints.</summary>
public static class RateLimitPolicies
{
    /// <summary>Sign-in, second factor, enrolment and invitation acceptance: 10 per minute per client.</summary>
    public const string Auth = "auth";

    /// <summary>Requests that send an email to an address typed by an anonymous user: 5 per 15 minutes per client.</summary>
    public const string AuthEmail = "auth-email";

    /// <summary>
    /// Writes that answer whether a personal identifier exists (registry registration and edits): 60 per
    /// minute per signed-in user, so duplicate probing cannot run at full speed (add-arquebusier-registry D11).
    /// </summary>
    public const string PersonalDataWrites = "personal-data-writes";

    /// <summary>
    /// Uploads that decode an image on the server (comparsa logos): 20 per minute per signed-in user,
    /// so a single account cannot keep the image pipeline busy (add-comparsa-logos D6).
    /// </summary>
    public const string ImageUploads = "image-uploads";

    /// <summary>
    /// Uploads that read a spreadsheet on the server (arquebusier import check and import): 10 per
    /// minute per signed-in user, so a single account cannot keep the workbook reader busy
    /// (add-registry-import D9).
    /// </summary>
    public const string SpreadsheetImports = "spreadsheet-imports";
}
