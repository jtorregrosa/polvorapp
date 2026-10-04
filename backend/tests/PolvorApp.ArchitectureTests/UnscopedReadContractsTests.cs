namespace PolvorApp.ArchitectureTests;

/// <summary>
/// Unscoped read contracts that return a person's photo may only be used where the caller's scope is
/// the whole Federation (add-badges, design D3; group 2 review): <c>IIdPhotoReader</c> is named only by
/// the registry, which implements it, and the badges module, whose routes are Admin-only (BR-12).
/// </summary>
public sealed class UnscopedReadContractsTests
{
    private const string Contract = "IIdPhotoReader";

    private static readonly string[] AllowedFolders = ["Modules/ArquebusierRegistry/", "Modules/Badges/"];

    [Fact]
    public void Only_the_registry_and_the_badges_module_name_the_id_photo_reader()
    {
        var root = SourceRoot();
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(file => !file.Contains("/obj/", StringComparison.Ordinal) && !file.Contains("/bin/", StringComparison.Ordinal))
            .Where(file => !AllowedFolders.Any(folder => file.StartsWith(folder, StringComparison.Ordinal)))
            .Where(file => File.ReadAllText(Path.Combine(root, file)).Contains(Contract, StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0, $"{Contract} named outside the registry and the badges module: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_badges_module_is_where_the_rule_allows_it() =>
        Assert.True(Directory.Exists(Path.Combine(SourceRoot(), "Modules/Badges")), "Modules/Badges");

    private static string SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PolvorApp.slnx")))
            {
                return Path.Combine(directory.FullName, "src");
            }
        }

        throw new InvalidOperationException("PolvorApp.slnx not found above the test output.");
    }
}
