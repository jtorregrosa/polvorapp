namespace PolvorApp.Badges;

/// <summary>Problem codes of the badges module, translated by the UI as <c>badges:errors.&lt;code&gt;</c>.</summary>
internal static class BadgeProblems
{
    public const string NotFound = "badges.notFound";
    public const string NothingToPrint = "badges.nothingToPrint";
    public const string TooMany = "badges.tooMany";
    public const string Busy = "badges.busy";
    public const string AuditUnavailable = "badges.auditUnavailable";

    /// <summary>A photo the registry holds cannot be read or scaled; the problem lists the arquebusiers' ids.</summary>
    public const string PhotoUnreadable = "badges.photoUnreadable";

    /// <summary>Shared with the other document routes: the storage could not serve the logo or a photo.</summary>
    public const string StorageUnavailable = "storage.unavailable";

    /// <summary>Reason of an <c>arquebusierIds[i]</c> that is not in the registry.</summary>
    public const string ArquebusierNotFound = "notFound";
}
