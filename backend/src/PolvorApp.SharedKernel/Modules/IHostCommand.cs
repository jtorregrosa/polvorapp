namespace PolvorApp.SharedKernel.Modules;

/// <summary>
/// A command-line verb a module contributes to the API host (<c>dotnet PolvorApp.Api.dll &lt;verb&gt; ...</c>).
/// It runs instead of the web server, with the host's services, and returns the process exit code.
/// </summary>
public interface IHostCommand
{
    string Verb { get; }

    /// <param name="arguments">The arguments after the verb.</param>
    /// <param name="cancellationToken">Cancelled by Ctrl+C.</param>
    Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
