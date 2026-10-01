namespace PolvorApp.SharedKernel.Storage;

/// <summary>
/// A module that keeps objects under <see cref="Prefix"/> and can tell which of them its records
/// still reference (spec: Stored file cleanup, design D2 of add-arquebusier-photos). The platform's
/// sweep deletes old objects under the prefix that no record references. Modules write the object
/// before committing its reference, and delete it only after committing the removal, so a failure
/// leaves an unreferenced object for the sweep, never a reference without an object.
/// </summary>
public interface IStoredObjectOwner
{
    /// <summary>The key prefix the module owns, e.g. <c>registry/photos/</c>; never shared with another owner.</summary>
    string Prefix { get; }

    /// <summary>Returns the subset of <paramref name="keys"/> that committed records reference.</summary>
    /// <exception cref="Exception">Any failure: the sweep then deletes nothing.</exception>
    Task<IReadOnlySet<string>> FilterReferencedAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}
