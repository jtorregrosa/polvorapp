using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Audit;

/// <summary>Spec audit-privacy "Audit log screens" and design D10: which records an entry can link to.</summary>
public sealed class AuditRecordResolverTests
{
    [Fact]
    public async Task The_linkable_record_types_each_have_one_resolver()
    {
        await using var factory = new ApiFactory("Host=offline");
        await using var scope = factory.Services.CreateAsyncScope();

        var types = scope.ServiceProvider.GetServices<IAuditRecordResolver>().Select(r => r.EntityType).Order(StringComparer.Ordinal);

        Assert.Equal(["Arquebusier", "Comparsa", "ComparsaOrder", "FestivalEdition", "User", "WeaponModel"], types);
    }

    [Fact]
    public async Task An_id_that_is_not_a_guid_never_exists()
    {
        await using var factory = new ApiFactory("Host=offline");
        await using var scope = factory.Services.CreateAsyncScope();

        foreach (var resolver in scope.ServiceProvider.GetServices<IAuditRecordResolver>())
        {
            Assert.Empty(await resolver.ExistingAsync(["registry", "arms-authority"], TestContext.Current.CancellationToken));
        }
    }
}
