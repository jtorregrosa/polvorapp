namespace PolvorApp.ArchitectureTests;

/// <summary>A forbidden reference between two PolvorApp assemblies.</summary>
public sealed record ModuleDependencyViolation(string Source, string Target, string Reason)
{
    public override string ToString() => $"{Source} -> {Target}: {Reason}";
}

/// <summary>
/// Module boundary rules of ADR-0001 (change bootstrap-platform, design D2), evaluated over an
/// assembly-reference graph so they can be tested with synthetic graphs.
/// </summary>
public static class ModuleDependencyRules
{
    /// <summary>Prefix shared by every PolvorApp project; other references are ignored.</summary>
    public const string Prefix = "PolvorApp.";
    public const string SharedKernel = "PolvorApp.SharedKernel";
    public const string Host = "PolvorApp.Api";
    private const string ContractsSuffix = ".Contracts";

    private enum Kind { SharedKernel, Host, Module, Contracts, Other }

    /// <summary>Returns every reference in the graph (project → referenced projects) that breaks a rule.</summary>
    public static IReadOnlyList<ModuleDependencyViolation> FindViolations(
        IReadOnlyDictionary<string, string[]> graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        return graph
            .SelectMany(node => node.Value
                .Where(reference => reference.StartsWith(Prefix, StringComparison.Ordinal))
                .Select(reference => Check(node.Key, reference)))
            .OfType<ModuleDependencyViolation>()
            .ToList();
    }

    private static ModuleDependencyViolation? Check(string source, string target)
    {
        var targetKind = Classify(target);

        return Classify(source) switch
        {
            Kind.SharedKernel => new(source, target, "the shared kernel must not depend on modules or the host"),
            Kind.Contracts when targetKind != Kind.SharedKernel =>
                new(source, target, $"contracts of module '{ModuleName(source)}' may only reference the shared kernel"),
            Kind.Module when targetKind == Kind.Module =>
                new(source, target, $"module '{ModuleName(source)}' must use the contract of module '{ModuleName(target)}', not its implementation"),
            Kind.Module when targetKind == Kind.Host =>
                new(source, target, $"module '{ModuleName(source)}' must not depend on the API host"),
            _ => null,
        };
    }

    private static Kind Classify(string assembly) => assembly switch
    {
        SharedKernel => Kind.SharedKernel,
        Host => Kind.Host,
        _ when assembly.StartsWith(Host + ".", StringComparison.Ordinal) => Kind.Host,
        _ when !assembly.StartsWith(Prefix, StringComparison.Ordinal) => Kind.Other,
        _ when assembly.EndsWith(ContractsSuffix, StringComparison.Ordinal) => Kind.Contracts,
        _ => Kind.Module,
    };

    private static string ModuleName(string assembly)
    {
        var name = assembly[Prefix.Length..];
        return name.EndsWith(ContractsSuffix, StringComparison.Ordinal) ? name[..^ContractsSuffix.Length] : name;
    }
}
