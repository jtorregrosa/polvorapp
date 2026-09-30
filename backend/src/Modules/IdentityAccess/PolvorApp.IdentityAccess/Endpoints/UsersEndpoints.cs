using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.IdentityAccess.Endpoints;

/// <summary>Spec "User management by Admins" and "Invitation-only accounts" (UC-24 users). Admins only.</summary>
internal static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapGroup("/users").WithTags("Users").RequireAuthorization(AuthorizationPolicies.Admin)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        users.MapGet("/", ListAsync).WithName("ListUsers").WithSummary("Users, optionally filtered by role and status.")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        users.MapGet("/{id:guid}", GetAsync).WithName("GetUser").WithSummary("One user.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        users.MapPost("/", InviteAsync).WithName("InviteUser").WithSummary("Invites a user by email.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status502BadGateway);
        users.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateUser").WithSummary("Changes name, role and language.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        users.MapPost("/{id:guid}/deactivate", (Guid id, UserAdministration admin, CancellationToken ct) => SetActiveAsync(id, false, admin, ct))
            .WithName("DeactivateUser").WithSummary("Deactivates a user and ends their sessions.")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        users.MapPost("/{id:guid}/reactivate", (Guid id, UserAdministration admin, CancellationToken ct) => SetActiveAsync(id, true, admin, ct))
            .WithName("ReactivateUser").WithSummary("Reactivates a deactivated user.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        users.MapPost("/{id:guid}/invitation", ResendInvitationAsync).WithName("ResendInvitation").WithSummary("Sends a new invitation link; the old one stops working.")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status502BadGateway);
        users.MapPost("/{id:guid}/two-factor/reset", ResetTwoFactorAsync).WithName("ResetTwoFactor").WithSummary("Removes the authenticator; the user enrols again at the next sign-in.")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        return endpoints;
    }

    private static async Task<Results<Ok<List<UserResponse>>, ProblemHttpResult>> ListAsync(
        string? role, string? status, IdentityAccessDbContext db, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var roleFilter = role is null ? null : UserInput.Role(role, errors);
        UserStatus? statusFilter = status switch
        {
            null => null,
            "INVITED" => UserStatus.Invited,
            "ACTIVE" => UserStatus.Active,
            "DEACTIVATED" => UserStatus.Deactivated,
            _ => null,
        };
        if (status is not null && statusFilter is null)
        {
            errors["status"] = UserInput.Invalid;
        }

        if (errors.Count > 0)
        {
            return Problems.Invalid(errors);
        }

        var query = db.Users.AsNoTracking();
        if (roleFilter is { } r)
        {
            query = query.Where(u => u.Role == r);
        }

        query = statusFilter switch
        {
            UserStatus.Invited => query.Where(u => u.Active && u.PasswordHash == null),
            UserStatus.Active => query.Where(u => u.Active && u.PasswordHash != null),
            UserStatus.Deactivated => query.Where(u => !u.Active),
            _ => query,
        };

        var users = await query.OrderBy(u => u.Name).ThenBy(u => u.Email).ToListAsync(cancellationToken);
        return TypedResults.Ok(users.Select(UserResponse.From).ToList());
    }

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> GetAsync(
        Guid id, IdentityAccessDbContext db, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken) is { } user
            ? TypedResults.Ok(UserResponse.From(user))
            : NotFound();

    private static async Task<Results<Created<UserResponse>, ProblemHttpResult>> InviteAsync(
        InviteUserRequest request, UserAdministration admin, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        UserInput.Email(request.Email, errors);
        UserInput.Name(request.Name, errors);
        var role = UserInput.Role(request.Role, errors);
        UserInput.Locale(request.Locale, errors);
        if (errors.Count > 0 || role is null)
        {
            return Problems.Invalid(errors);
        }

        var (outcome, user) = await admin.InviteAsync(request.Email!.Trim(), request.Name!.Trim(), role.Value, request.Locale!, cancellationToken);
        return outcome switch
        {
            AdminOutcome.Done => TypedResults.Created($"/api/users/{user!.Id}", UserResponse.From(user)),
            _ => Problem(outcome, user),
        };
    }

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateUserRequest request, UserAdministration admin, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        UserInput.Name(request.Name, errors);
        var role = UserInput.Role(request.Role, errors);
        UserInput.Locale(request.Locale, errors);
        if (errors.Count > 0 || role is null)
        {
            return Problems.Invalid(errors);
        }

        return Respond(await admin.UpdateAsync(id, request.Name!.Trim(), role.Value, request.Locale!, cancellationToken));
    }

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> SetActiveAsync(
        Guid id, bool active, UserAdministration admin, CancellationToken cancellationToken) =>
        Respond(await admin.SetActiveAsync(id, active, cancellationToken));

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> ResendInvitationAsync(
        Guid id, UserAdministration admin, CancellationToken cancellationToken) =>
        Respond(await admin.ResendInvitationAsync(id, cancellationToken));

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> ResetTwoFactorAsync(
        Guid id, UserAdministration admin, CancellationToken cancellationToken) =>
        Respond(await admin.ResetTwoFactorAsync(id, cancellationToken));

    private static Results<Ok<UserResponse>, ProblemHttpResult> Respond((AdminOutcome Outcome, User? User) result) =>
        result.Outcome == AdminOutcome.Done ? TypedResults.Ok(UserResponse.From(result.User!)) : Problem(result.Outcome, result.User);

    private static ProblemHttpResult Problem(AdminOutcome outcome, User? user) => outcome switch
    {
        AdminOutcome.NotFound => NotFound(),
        AdminOutcome.EmailTaken => Problems.Conflict(Problems.EmailTaken),
        AdminOutcome.LastAdmin => Problems.Conflict(Problems.LastAdmin),
        AdminOutcome.NotInvited => Problems.Conflict(Problems.NotInvited),
        AdminOutcome.NotEnrolled => Problems.Conflict(Problems.NotEnrolled),
        AdminOutcome.EmailNotSent => Problems.Problem(StatusCodes.Status502BadGateway, Problems.EmailSendFailed, new Dictionary<string, object?> { ["userId"] = user?.Id }),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unexpected outcome."),
    };

    private static ProblemHttpResult NotFound() => ProblemResults.NotFound(Problems.UserNotFound);
}
