using System.Runtime.CompilerServices;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>
/// Golden files (ADR-0008): the expected text of a generated document, committed next to the tests
/// in <c>Exports/Golden/</c>. Set <c>POLVORAPP_UPDATE_GOLDEN=1</c> to rewrite them, then review the
/// diff before committing.
/// </summary>
internal static class Golden
{
    public static void Matches(string name, string actual, [CallerFilePath] string caller = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(caller)!, "Golden", name + ".golden.txt");
        var normalised = actual.ReplaceLineEndings("\n");
        if (Environment.GetEnvironmentVariable("POLVORAPP_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, normalised);
            return;
        }

        Assert.True(File.Exists(path), $"Missing golden file {name}; run with POLVORAPP_UPDATE_GOLDEN=1 and review it.");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), normalised);
    }
}
