using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.OwnedWeapons;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Security;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ArquebusierRegistry.Endpoints;

/// <summary>
/// Spec "Owned weapons (UC-04)" (design D6): owned weapons are a sub-resource of an arquebusier in the
/// caller's scope; an arquebusier outside it does not exist for the caller (BR-12).
/// </summary>
internal static class OwnedWeaponEndpoints
{
    public static IEndpointRouteBuilder MapOwnedWeaponEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/arquebusiers/{id:guid}/owned-weapons").WithTags("Arquebusiers")
            .WithMetadata(new RequestSizeLimitAttribute(ArquebusierEndpoints.MaxBodyBytes))
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapPost("/", AddAsync).WithName("AddOwnedWeapon")
            .WithSummary("Adds an owned weapon to an arquebusier of the caller's comparsas.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(RateLimitPolicies.PersonalDataWrites).ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapPut("/{weaponId:guid}", UpdateAsync).WithName("UpdateOwnedWeapon")
            .WithSummary("Replaces an owned weapon's model and numbers, if the version is still current.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(RateLimitPolicies.PersonalDataWrites).ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapDelete("/{weaponId:guid}", RemoveAsync).WithName("RemoveOwnedWeapon")
            .WithSummary("Removes an owned weapon.")
            .RequireRateLimiting(RateLimitPolicies.PersonalDataWrites).ProducesProblem(StatusCodes.Status429TooManyRequests);
        return endpoints;
    }

    private static async Task<Results<Created<OwnedWeaponResponse>, ProblemHttpResult>> AddAsync(
        Guid id, OwnedWeaponRequest request, OwnedWeaponAdministration administration, ICatalogDirectory catalog, CancellationToken cancellationToken)
    {
        var (input, errors) = RegistryInput.Read(request.Fields());
        if (input is null)
        {
            return ProblemResults.Invalid(errors);
        }

        return await administration.AddAsync(id, input, cancellationToken) switch
        {
            // Owned weapons are read through their arquebusier's detail.
            (RegistryOutcome.Done, { } weapon) => TypedResults.Created($"/api/arquebusiers/{id}", await ResponseAsync(weapon, catalog, cancellationToken)),
            var (outcome, _) => RegistryProblems.From(outcome),
        };
    }

    private static async Task<Results<Ok<OwnedWeaponResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, Guid weaponId, UpdateOwnedWeaponRequest request, OwnedWeaponAdministration administration, ICatalogDirectory catalog,
        CancellationToken cancellationToken)
    {
        var (input, found) = RegistryInput.Read(request.Fields());
        var errors = new Dictionary<string, string>(found, StringComparer.Ordinal);
        if (request.Version is null)
        {
            errors["version"] = InputFields.Required;
        }

        if (input is null || request.Version is not { } version)
        {
            return ProblemResults.Invalid(errors);
        }

        return await administration.UpdateAsync(id, weaponId, input, version, cancellationToken) switch
        {
            (RegistryOutcome.Done, { } weapon) => TypedResults.Ok(await ResponseAsync(weapon, catalog, cancellationToken)),
            var (outcome, _) => RegistryProblems.From(outcome),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAsync(
        Guid id, Guid weaponId, OwnedWeaponAdministration administration, CancellationToken cancellationToken)
    {
        var (outcome, _) = await administration.RemoveAsync(id, weaponId, cancellationToken);
        return outcome == RegistryOutcome.Done ? TypedResults.NoContent() : RegistryProblems.From(outcome);
    }

    private static async Task<OwnedWeaponResponse> ResponseAsync(OwnedWeapon weapon, ICatalogDirectory catalog, CancellationToken cancellationToken) =>
        new(
            weapon.Id,
            await catalog.FindWeaponModelAsync(weapon.WeaponModelId, cancellationToken)
                ?? throw new InvalidOperationException($"Weapon model {weapon.WeaponModelId} of owned weapon {weapon.Id} is missing from the catalog."),
            weapon.WeaponNumber,
            weapon.OwnershipGuideNumber,
            weapon.Version);
}
