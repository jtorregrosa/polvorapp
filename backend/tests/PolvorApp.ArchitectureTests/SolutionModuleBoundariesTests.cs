using System.Xml.Linq;

namespace PolvorApp.ArchitectureTests;

public sealed class SolutionModuleBoundariesTests
{
    private const string ContractsSuffix = ".Contracts";

    [Fact]
    public void Every_source_project_respects_module_boundaries()
    {
        var graph = SourceProjects().ToDictionary(p => p.Name, p => p.References);

        Assert.Contains(ModuleDependencyRules.Host, graph.Keys);
        Assert.Contains(ModuleDependencyRules.SharedKernel, graph.Keys);
        var violations = ModuleDependencyRules.FindViolations(graph);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Api_host_references_every_module_so_each_one_is_registered_and_deployed()
    {
        var projects = SourceProjects();
        var host = projects.Single(p => p.Name == ModuleDependencyRules.Host);
        var modules = projects
            .Select(p => p.Name)
            .Where(name => name is not ModuleDependencyRules.Host and not ModuleDependencyRules.SharedKernel)
            .Where(name => !name.EndsWith(ContractsSuffix, StringComparison.Ordinal));

        Assert.All(modules, module => Assert.Contains(module, host.References));
    }

    private sealed record SourceProject(string Name, string[] References);

    /// <summary>
    /// Every project under <c>backend/src</c> with its <c>ProjectReference</c> items. Reading project
    /// files instead of compiled assemblies also catches references whose types are not used yet.
    /// </summary>
    private static List<SourceProject> SourceProjects()
    {
        var source = Path.Combine(FindBackendRoot(), "src");

        return Directory
            .EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories)
            .Select(path => new SourceProject(
                Path.GetFileNameWithoutExtension(path),
                XDocument.Load(path)
                    .Descendants("ProjectReference")
                    .Select(reference => Path.GetFileNameWithoutExtension(
                        reference.Attribute("Include")!.Value.Replace('\\', '/')))
                    .ToArray()))
            .ToList();
    }

    private static string FindBackendRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PolvorApp.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("PolvorApp.slnx not found above the test output directory.");
    }
}
