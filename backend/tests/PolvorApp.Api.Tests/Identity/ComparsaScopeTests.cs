using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Security;

namespace PolvorApp.Api.Tests.Identity;

/// <summary>Spec "Comparsa scoping (BR-12)": blocking, deny by default, enforced on the server.</summary>
public sealed class ComparsaScopeTests
{
    private static readonly Guid Assigned = Guid.CreateVersion7();
    private static readonly Guid Other = Guid.CreateVersion7();

    private sealed record FakeUser(Guid? UserId, UserRole? Role) : ICurrentUser
    {
        public bool IsAuthenticated => UserId is not null;
    }

    private sealed class FakeAssignments(params Guid[] comparsas) : IFiringChiefAssignmentSource
    {
        public Task<IReadOnlySet<Guid>> GetComparsaIdsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(comparsas.ToHashSet());
    }

    [Fact]
    public async Task An_admin_reaches_every_comparsa()
    {
        var access = await Scope(new FakeUser(Guid.CreateVersion7(), UserRole.Admin), new FakeAssignments()).GetAccessAsync(TestContext.Current.CancellationToken);

        Assert.True(access.IsAll);
        Assert.True(access.CanAccess(Other));
    }

    [Fact]
    public async Task A_firing_chief_without_assignments_reaches_no_comparsa()
    {
        var access = await Scope(new FakeUser(Guid.CreateVersion7(), UserRole.FiringChief), new NoFiringChiefAssignments()).GetAccessAsync(TestContext.Current.CancellationToken);

        Assert.False(access.IsAll);
        Assert.False(access.CanAccess(Assigned));
    }

    [Fact]
    public async Task A_firing_chief_reaches_only_the_assigned_comparsa()
    {
        var access = await Scope(new FakeUser(Guid.CreateVersion7(), UserRole.FiringChief), new FakeAssignments(Assigned)).GetAccessAsync(TestContext.Current.CancellationToken);

        Assert.True(access.CanAccess(Assigned));
        Assert.False(access.CanAccess(Other));
    }

    [Fact]
    public async Task An_anonymous_request_reaches_no_comparsa()
    {
        var access = await Scope(new FakeUser(null, null), new FakeAssignments(Assigned)).GetAccessAsync(TestContext.Current.CancellationToken);

        Assert.False(access.CanAccess(Assigned));
    }

    [Fact]
    public async Task Queries_are_filtered_to_the_accessible_comparsas()
    {
        var rows = new[] { (Id: 1, ComparsaId: Assigned), (Id: 2, ComparsaId: Other) }.AsQueryable();
        var access = await Scope(new FakeUser(Guid.CreateVersion7(), UserRole.FiringChief), new FakeAssignments(Assigned)).GetAccessAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1], access.Filter(rows, r => r.ComparsaId).Select(r => r.Id));
        Assert.Equal([1, 2], ComparsaAccess.All.Filter(rows, r => r.ComparsaId).Select(r => r.Id));
        Assert.Empty(ComparsaAccess.None.Filter(rows, r => r.ComparsaId));
    }

    private static ComparsaScope Scope(ICurrentUser user, IFiringChiefAssignmentSource assignments) => new(user, assignments);
}
