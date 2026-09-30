using System.Text.RegularExpressions;

namespace PolvorApp.ArchitectureTests;

/// <summary>Repository-level rules of the platform spec that no other test exercises.</summary>
public sealed partial class RepositoryHygieneTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void License_is_mit()
    {
        var license = File.ReadAllText(Path.Combine(RepositoryRoot, "LICENSE"));

        Assert.StartsWith("MIT License", license, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2026 Jorge Torregrosa Lloret", license, StringComparison.Ordinal);
    }

    [Fact]
    public void Env_example_defines_every_variable_compose_requires()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot, "compose.yaml"));
        var example = ReadEnvFile(Path.Combine(RepositoryRoot, ".env.example"));

        // ${NAME} without a ":-default" must come from .env; $${NAME} is escaped for the container.
        var required = ComposeVariable().Matches(compose)
            .Where(match => !match.Groups["default"].Success)
            .Select(match => match.Groups["name"].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(required);
        Assert.All(required, name =>
        {
            Assert.True(example.TryGetValue(name, out var value), $"{name} is missing from .env.example");
            Assert.False(string.IsNullOrWhiteSpace(value), $"{name} is empty in .env.example");
        });
    }

    [Fact]
    public void Local_env_file_is_never_committed()
    {
        var gitignore = File.ReadAllLines(Path.Combine(RepositoryRoot, ".gitignore"));

        Assert.Contains(".env", gitignore);
        Assert.Contains("!.env.example", gitignore);
    }

    private static Dictionary<string, string> ReadEnvFile(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "compose.yaml")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("compose.yaml not found above the test output directory.");
    }

    [GeneratedRegex(@"(?<!\$)\$\{(?<name>[A-Z0-9_]+)(?<default>:-[^}]*)?\}")]
    private static partial Regex ComposeVariable();
}
