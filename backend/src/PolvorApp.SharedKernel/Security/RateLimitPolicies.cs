namespace PolvorApp.SharedKernel.Security;

/// <summary>Rate-limit policy names defined by the host; modules put them on their endpoints.</summary>
public static class RateLimitPolicies
{
    /// <summary>Sign-in, second factor, enrolment and invitation acceptance: 10 per minute per client.</summary>
    public const string Auth = "auth";

    /// <summary>Requests that send an email to an address typed by an anonymous user: 5 per 15 minutes per client.</summary>
    public const string AuthEmail = "auth-email";
}
