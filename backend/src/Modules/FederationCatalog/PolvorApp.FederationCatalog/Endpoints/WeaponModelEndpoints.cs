using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.FederationCatalog.Endpoints;

/// <summary>
/// Specs "Weapon models" (BR-07), "Weapon catalogue access" and "Deleting comparsas and weapon
/// models". Every signed-in user reads the catalogue; every write is Admin-only.
/// </summary>
internal static class WeaponModelEndpoints
{
    /// <summary>Validation reason for a rentable pistol (BR-07).</summary>
    public const string PistolNotRentable = "pistolNotRentable";

    public static IEndpointRouteBuilder MapWeaponModelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/weapon-models").WithTags("WeaponModels").ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapGet("/", ListAsync).WithName("ListWeaponModels")
            .WithSummary("The weapon catalogue sorted by label; inactive models only with includeInactive.")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetWeaponModel").WithSummary("One weapon model.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var adminOnly = group.MapGroup(string.Empty).RequireAuthorization(AuthorizationPolicies.Admin).ProducesProblem(StatusCodes.Status403Forbidden);
        adminOnly.MapPost("/", CreateAsync).WithName("CreateWeaponModel").WithSummary("Creates a weapon model.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
        adminOnly.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateWeaponModel").WithSummary("Changes a weapon model.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        adminOnly.MapPost("/{id:guid}/deactivate", (Guid id, WeaponModelAdministration administration, CancellationToken ct) => SetActiveAsync(id, false, administration, ct))
            .WithName("DeactivateWeaponModel").WithSummary("Deactivates a weapon model.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        adminOnly.MapPost("/{id:guid}/reactivate", (Guid id, WeaponModelAdministration administration, CancellationToken ct) => SetActiveAsync(id, true, administration, ct))
            .WithName("ReactivateWeaponModel").WithSummary("Reactivates a deactivated weapon model.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        adminOnly.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteWeaponModel")
            .WithSummary("Deletes a weapon model that no other record uses.")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        return endpoints;
    }

    private static async Task<Results<Ok<List<WeaponModelResponse>>, ProblemHttpResult>> ListAsync(
        string? kind, bool? includeInactive, FederationCatalogDbContext db, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var kindFilter = CatalogInput.OptionalCode<WeaponKind>(kind, "kind", errors);
        if (errors.Count > 0)
        {
            return ProblemResults.Invalid(errors);
        }

        var query = db.WeaponModels.AsNoTracking();
        if (includeInactive != true)
        {
            query = query.Where(m => m.Active);
        }

        if (kindFilter is { } wanted)
        {
            query = query.Where(m => m.Kind == wanted);
        }

        var models = await query.ToListAsync(cancellationToken);
        return TypedResults.Ok(models.OrderBy(m => m.Label, CatalogOrder.Names).Select(WeaponModelResponse.From).ToList());
    }

    private static async Task<Results<Ok<WeaponModelResponse>, ProblemHttpResult>> GetAsync(
        Guid id, FederationCatalogDbContext db, CancellationToken cancellationToken) =>
        await db.WeaponModels.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, cancellationToken) is { } model
            ? TypedResults.Ok(WeaponModelResponse.From(model))
            : CatalogProblems.From(CatalogOutcome.WeaponModelNotFound);

    private static async Task<Results<Created<WeaponModelResponse>, ProblemHttpResult>> CreateAsync(
        WeaponModelRequest request, WeaponModelAdministration administration, CancellationToken cancellationToken)
    {
        if (!TryRead(request, out var input, out var invalid))
        {
            return invalid;
        }

        return await administration.CreateAsync(input, cancellationToken) switch
        {
            (CatalogOutcome.Done, { } model) => TypedResults.Created($"/api/weapon-models/{model.Id}", WeaponModelResponse.From(model)),
            var (outcome, _) => CatalogProblems.From(outcome),
        };
    }

    private static async Task<Results<Ok<WeaponModelResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, WeaponModelRequest request, WeaponModelAdministration administration, CancellationToken cancellationToken)
    {
        if (!TryRead(request, out var input, out var invalid))
        {
            return invalid;
        }

        return Respond(await administration.UpdateAsync(id, input, cancellationToken));
    }

    private static async Task<Results<Ok<WeaponModelResponse>, ProblemHttpResult>> SetActiveAsync(
        Guid id, bool active, WeaponModelAdministration administration, CancellationToken cancellationToken) =>
        Respond(await administration.SetActiveAsync(id, active, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, WeaponModelAdministration administration, CancellationToken cancellationToken)
    {
        var outcome = await administration.DeleteAsync(id, cancellationToken);
        return outcome == CatalogOutcome.Done ? TypedResults.NoContent() : CatalogProblems.From(outcome);
    }

    private static Results<Ok<WeaponModelResponse>, ProblemHttpResult> Respond((CatalogOutcome Outcome, WeaponModel? Model) result) => result switch
    {
        (CatalogOutcome.Done, { } model) => TypedResults.Ok(WeaponModelResponse.From(model)),
        var (outcome, _) => CatalogProblems.From(outcome),
    };

    /// <summary>
    /// Reads a create or edit request, or names every invalid field. Every kind but a pistol needs
    /// side, handedness and size (the database check says the same); a pistol needs only kind and
    /// label, treats an empty attribute as absent and is never rentable (BR-07). <c>rentable</c> is
    /// always required: an edit replaces the whole model, so a forgotten flag must not flip it.
    /// </summary>
    private static bool TryRead(
        WeaponModelRequest request, [NotNullWhen(true)] out WeaponModelInput? input, [NotNullWhen(false)] out ProblemHttpResult? invalid)
    {
        var errors = new Dictionary<string, string>();
        var kind = CatalogInput.RequiredCode<WeaponKind>(request.Kind, "kind", errors);
        var label = CatalogInput.Text(request.Label, "label", WeaponModel.LabelMaxLength, errors);
        var attributesRequired = kind is { } known && known != WeaponKind.Pistol;
        var side = Attribute<Side>(request.Side, "side", attributesRequired, errors);
        var handedness = Attribute<Handedness>(request.Handedness, "handedness", attributesRequired, errors);
        var size = Attribute<WeaponSize>(request.Size, "size", attributesRequired, errors);

        if (request.Rentable is null)
        {
            errors["rentable"] = CatalogInput.Required;
        }
        else if (request.Rentable.Value && kind == WeaponKind.Pistol)
        {
            errors["rentable"] = PistolNotRentable;
        }

        if (kind is { } validKind && label is not null && request.Rentable is { } rentable && errors.Count == 0)
        {
            (input, invalid) = (new WeaponModelInput(validKind, side, handedness, size, rentable, label), null);
            return true;
        }

        (input, invalid) = (null, ProblemResults.Invalid(errors));
        return false;
    }

    private static TEnum? Attribute<TEnum>(string? code, string field, bool required, Dictionary<string, string> errors)
        where TEnum : struct, Enum =>
        required
            ? CatalogInput.RequiredCode<TEnum>(code, field, errors)
            : CatalogInput.OptionalCode<TEnum>(string.IsNullOrEmpty(code) ? null : code, field, errors);
}
