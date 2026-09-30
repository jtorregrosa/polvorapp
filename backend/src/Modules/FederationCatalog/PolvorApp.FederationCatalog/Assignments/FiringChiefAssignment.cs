namespace PolvorApp.FederationCatalog.Assignments;

/// <summary>
/// A FiringChief assigned to a comparsa (spec: FiringChief assignments). The user lives in the
/// identity module: no cross-schema foreign key, validated through <c>IUserDirectory</c> on write.
/// </summary>
internal sealed class FiringChiefAssignment
{
    public required Guid ComparsaId { get; init; }

    public required Guid UserId { get; init; }

    public required DateTimeOffset AssignedAt { get; init; }
}
