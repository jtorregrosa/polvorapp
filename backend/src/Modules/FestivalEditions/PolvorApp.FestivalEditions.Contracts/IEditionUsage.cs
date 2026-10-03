namespace PolvorApp.FestivalEditions.Contracts;

/// <summary>
/// Implemented by modules whose records reference festival editions, e.g. comparsa orders
/// (add-comparsa-orders, design D5). The editions module asks every implementation before deleting a
/// draft edition and refuses the deletion while any reports a use (<c>editions.inUse</c>).
/// </summary>
public interface IEditionUsage
{
    /// <summary>True when records of the implementing module reference the edition.</summary>
    Task<bool> IsEditionInUseAsync(Guid editionId, CancellationToken cancellationToken);
}
