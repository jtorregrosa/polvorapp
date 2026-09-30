using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Emails;
using PolvorApp.IdentityAccess.Endpoints;
using PolvorApp.IdentityAccess.Persistence;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Modules;
using PolvorApp.SharedKernel.Persistence;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.IdentityAccess;

/// <summary>
/// Capability identity-access (UC-24 users, BR-12, SEC-03, SEC-04; ADR-0004): users and roles,
/// invitations, sign-in with TOTP, sessions, account self-service and comparsa scoping.
/// </summary>
public sealed class IdentityAccessModule : IModule
{
    /// <summary>After the audit trail (the identity context writes audit entries).</summary>
    public const int MigrationOrder = 10;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddModuleDbContext<IdentityAccessDbContext>(IdentityAccessDbContext.Schema, MigrationOrder);
        services.AddPolvorAppIdentity();

        services.AddLocalization();
        services.AddScoped<IdentityEmails>();
        services.AddScoped<SignInFlow>();
        services.AddScoped<UserAdministration>();
        services.AddSingleton<IHostCommand, CreateAdminCommand>();
        services.AddScoped<IDataSeeder, IdentitySeeder>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IComparsaScope, ComparsaScope>();
        services.TryAddScoped<IFiringChiefAssignmentSource, NoFiringChiefAssignments>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/auth").WithTags("Auth");
        auth.MapSignInEndpoints();
        auth.MapEnrolmentEndpoints();
        auth.MapPasswordEndpoints();
        auth.MapInvitationEndpoints();
        endpoints.MapAccountEndpoints();
        endpoints.MapUsersEndpoints();
    }
}
