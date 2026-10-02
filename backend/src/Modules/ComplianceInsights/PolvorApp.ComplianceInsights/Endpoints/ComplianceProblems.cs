using Microsoft.AspNetCore.Http.HttpResults;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.ComplianceInsights.Endpoints;

/// <summary>Problem codes of the compliance insights; the UI translates them.</summary>
internal static class ComplianceProblems
{
    /// <summary>The comparsa does not exist or is outside the caller's scope: both look the same (BR-12).</summary>
    public const string ComparsaNotFound = "compliance.comparsaNotFound";

    public static ProblemHttpResult ComparsaMissing() => ProblemResults.NotFound(ComparsaNotFound);
}
