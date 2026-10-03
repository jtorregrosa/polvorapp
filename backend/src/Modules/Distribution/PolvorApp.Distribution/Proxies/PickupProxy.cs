using PolvorApp.Distribution.Contracts;

namespace PolvorApp.Distribution.Proxies;

/// <summary>
/// An exceptional pickup proxy (glossary: <c>PickupProxy</c>; spec: Pickup proxies (UC-19, BR-06)): the
/// holder's entry authorises another entry of the same order to collect for them. No reason is stored
/// (maintainer decision): it is written by hand on the signed form. Never edited, only removed. The
/// edition and comparsa are copied from the holder's entry, which never changes order (BR-13).
/// </summary>
internal sealed class PickupProxy
{
    public required Guid Id { get; init; }

    public required Guid EditionId { get; init; }

    public required Guid ComparsaId { get; init; }

    public required DistributionType Type { get; init; }

    public required Guid HolderEntryId { get; init; }

    public required Guid ProxyEntryId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
