using Microsoft.AspNetCore.Http;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.Api.Tests;

/// <summary>The shared problem helpers: status, the reserved <c>code</c> extension and field errors.</summary>
public sealed class ProblemResultsTests
{
    [Fact]
    public void Problem_carries_the_status_the_code_and_extra_extensions()
    {
        var result = ProblemResults.Problem(StatusCodes.Status502BadGateway, "email.sendFailed", new Dictionary<string, object?> { ["userId"] = "u1" });

        Assert.Equal(StatusCodes.Status502BadGateway, result.ProblemDetails.Status);
        Assert.Equal("email.sendFailed", result.ProblemDetails.Extensions["code"]);
        Assert.Equal("u1", result.ProblemDetails.Extensions["userId"]);
    }

    [Fact]
    public void The_code_cannot_be_overridden_by_an_extra_extension()
    {
        var result = ProblemResults.Problem(StatusCodes.Status400BadRequest, "real", new Dictionary<string, object?> { ["code"] = "forged" });

        Assert.Equal("real", result.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public void NotFound_and_Conflict_use_their_status()
    {
        Assert.Equal(StatusCodes.Status404NotFound, ProblemResults.NotFound("comparsas.notFound").ProblemDetails.Status);
        Assert.Equal(StatusCodes.Status409Conflict, ProblemResults.Conflict("comparsas.nameTaken").ProblemDetails.Status);
    }

    [Fact]
    public void Invalid_names_each_field_with_its_reason()
    {
        var fields = new Dictionary<string, string> { ["side"] = "invalid" };

        var result = ProblemResults.Invalid(fields);

        Assert.Equal(StatusCodes.Status400BadRequest, result.ProblemDetails.Status);
        Assert.Equal(ProblemResults.Validation, result.ProblemDetails.Extensions["code"]);
        Assert.Same(fields, result.ProblemDetails.Extensions["errors"]);
    }
}
