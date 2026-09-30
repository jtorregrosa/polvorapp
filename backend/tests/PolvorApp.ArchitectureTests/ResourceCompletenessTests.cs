using System.Xml.Linq;

namespace PolvorApp.ArchitectureTests;

/// <summary>
/// Spec platform "Translation completeness" for the API's texts (problem titles, emails): every key
/// of a neutral (Spanish) <c>.resx</c> exists, non-empty, in the Valencian (<c>.ca</c>) and English
/// (<c>.en</c>) files beside it.
/// </summary>
public sealed class ResourceCompletenessTests
{
    private static readonly string[] Cultures = ["ca", "en"];

    public static TheoryData<string> NeutralResources()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(SourceRoot(), "*.resx", SearchOption.AllDirectories)
            .Where(f => Path.GetFileNameWithoutExtension(f).Split('.').Length == 1))
        {
            data.Add(Path.GetRelativePath(SourceRoot(), file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NeutralResources))]
    public void Every_key_exists_in_every_culture(string neutral)
    {
        var path = Path.Combine(SourceRoot(), neutral);
        var keys = Keys(path);
        Assert.NotEmpty(keys);

        foreach (var culture in Cultures)
        {
            var translated = Path.ChangeExtension(path, $".{culture}.resx");
            Assert.True(File.Exists(translated), $"{neutral}: missing {culture} file");
            var translatedKeys = Keys(translated);
            var missing = keys.Keys.Where(k => !translatedKeys.TryGetValue(k, out var value) || string.IsNullOrWhiteSpace(value)).ToList();
            Assert.True(missing.Count == 0, $"{neutral} [{culture}] missing or empty: {string.Join(", ", missing)}");
            Assert.Empty(translatedKeys.Keys.Except(keys.Keys));
        }
    }

    private static Dictionary<string, string> Keys(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(e => e.Attribute("name")!.Value, e => e.Element("value")?.Value ?? string.Empty);

    private static string SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PolvorApp.slnx")))
            {
                return Path.Combine(directory.FullName, "src");
            }
        }

        throw new InvalidOperationException("PolvorApp.slnx not found above the test output directory.");
    }
}
