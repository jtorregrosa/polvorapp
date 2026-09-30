namespace PolvorApp.SharedKernel.Seeding;

/// <summary>
/// Populates a module's tables with synthetic data for development, staging and demos (SEC-11).
/// Seeders must never read real data; randomness must derive from <see cref="SyntheticData.RandomSeed"/>
/// so that every run produces the same data.
/// </summary>
public interface IDataSeeder
{
    /// <summary>Execution order across modules; lower runs first.</summary>
    int Order { get; }

    Task SeedAsync(CancellationToken cancellationToken);
}
