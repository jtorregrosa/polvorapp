namespace PolvorApp.ArquebusierRegistry.Lock;

/// <summary>
/// The registry's single settings row (add-festival-editions, design D8): the registry lock (BR-10,
/// UC-11). While <see cref="Locked"/>, FiringChiefs cannot change the registry; Admins always can.
/// </summary>
internal sealed class RegistrySettings
{
    /// <summary>The only row's id (a check constraint allows no other).</summary>
    public const short SingletonId = 1;

    public required short Id { get; init; }

    public bool Locked { get; set; }

    /// <summary>When the lock was last turned on or off; null until then.</summary>
    public DateTimeOffset? LockedChangedAt { get; set; }
}
