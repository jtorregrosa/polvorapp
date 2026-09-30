namespace PolvorApp.IdentityAccess.Contracts;

/// <summary>Comparsa scoping of the current request (BR-12, SEC-03), enforced on the server.</summary>
public interface IComparsaScope
{
    Task<ComparsaAccess> GetAccessAsync(CancellationToken cancellationToken);
}
