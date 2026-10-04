namespace PolvorApp.SharedKernel.Security;

/// <summary>Rate-limit policy names defined by the host; modules put them on their endpoints.</summary>
public static class RateLimitPolicies
{
    /// <summary>Sign-in, second factor, enrolment and invitation acceptance: 10 per minute per client.</summary>
    public const string Auth = "auth";

    /// <summary>Requests that send an email to an address typed by an anonymous user: 5 per 15 minutes per client.</summary>
    public const string AuthEmail = "auth-email";

    /// <summary>
    /// Requests that reveal whether a personal identifier exists (registry registration and edits, and the
    /// orders' lender lookup): 60 per minute per signed-in user, so probing cannot run at full speed
    /// (add-arquebusier-registry D11, add-comparsa-orders D9).
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

    /// <summary>
    /// Writes to comparsa orders (preparation, entries, loans, submission, review): 120 per minute per
    /// signed-in user, enough to edit an order entry after entry, while a single account cannot keep the
    /// order writes, which read a whole roster, busy (add-comparsa-orders, group 4 review).
    /// </summary>
    public const string OrderWrites = "order-writes";

    /// <summary>
    /// Downloads of exports (Excel and PDF files built from an edition's orders): 30 per minute per
    /// signed-in user, so a single account cannot keep the document generation busy (add-exports D8).
    /// </summary>
    public const string Exports = "exports";

    /// <summary>
    /// GDPR requests (person lookup, personal data export and erasure): 10 per minute per signed-in
    /// Admin, so a lookup by DNI/NIE cannot be used to probe the registry at speed (add-audit-privacy D11).
    /// </summary>
    public const string Privacy = "privacy";
}
