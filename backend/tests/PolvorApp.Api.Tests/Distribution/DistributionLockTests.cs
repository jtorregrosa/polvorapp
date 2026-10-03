using PolvorApp.Distribution.Persistence;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>Design D4: the proxies' advisory lock key is the same on every process and host, so two API instances serialise.</summary>
public sealed class DistributionLockTests
{
    private static readonly Guid Edition = Guid.Parse("0192f0aa-0000-7000-8000-000000000001");
    private static readonly Guid Comparsa = Guid.Parse("0192f0aa-0000-7000-8000-000000000002");

    [Fact]
    public void The_pair_key_is_a_fixed_hash_of_both_ids() =>
        Assert.Equal(1169825196, DistributionLocks.PairKey(Edition, Comparsa));

    [Fact]
    public void The_pair_key_depends_on_the_order_of_the_ids() =>
        Assert.NotEqual(DistributionLocks.PairKey(Edition, Comparsa), DistributionLocks.PairKey(Comparsa, Edition));
}
