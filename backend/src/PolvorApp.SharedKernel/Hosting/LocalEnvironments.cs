using Microsoft.Extensions.Hosting;

namespace PolvorApp.SharedKernel.Hosting;

/// <summary>
/// Environments that run only on a developer machine or in tests (NFR-13): plain SMTP to the mail
/// catcher, http links and non-Secure cookies are allowed only here.
/// </summary>
public static class LocalEnvironments
{
    public const string Testing = "Testing";

    public static bool IsLocal(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.IsDevelopment() || environment.IsEnvironment(Testing);
    }
}
