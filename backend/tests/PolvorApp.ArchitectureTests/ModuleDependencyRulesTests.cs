namespace PolvorApp.ArchitectureTests;

public sealed class ModuleDependencyRulesTests
{
    private static Dictionary<string, string[]> Graph(params (string Assembly, string[] References)[] nodes) =>
        nodes.ToDictionary(n => n.Assembly, n => n.References);

    [Fact]
    public void Module_referencing_another_modules_implementation_is_a_violation_naming_both_modules()
    {
        var graph = Graph(
            ("PolvorApp.Registry", ["PolvorApp.SharedKernel", "PolvorApp.Orders"]),
            ("PolvorApp.Orders", ["PolvorApp.SharedKernel"]));

        var violations = ModuleDependencyRules.FindViolations(graph);

        var violation = Assert.Single(violations);
        Assert.Equal("PolvorApp.Registry", violation.Source);
        Assert.Equal("PolvorApp.Orders", violation.Target);
        Assert.Contains("module 'Registry'", violation.Reason, StringComparison.Ordinal);
        Assert.Contains("module 'Orders'", violation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Module_referencing_shared_kernel_and_other_modules_contracts_is_allowed()
    {
        var graph = Graph(
            ("PolvorApp.Registry", ["PolvorApp.SharedKernel", "PolvorApp.Registry.Contracts", "PolvorApp.Orders.Contracts", "System.Runtime"]),
            ("PolvorApp.Registry.Contracts", ["PolvorApp.SharedKernel"]),
            ("PolvorApp.Orders.Contracts", ["PolvorApp.SharedKernel"]));

        Assert.Empty(ModuleDependencyRules.FindViolations(graph));
    }

    [Fact]
    public void Contracts_referencing_a_module_implementation_is_a_violation()
    {
        var graph = Graph(("PolvorApp.Orders.Contracts", ["PolvorApp.SharedKernel", "PolvorApp.Registry"]));

        var violation = Assert.Single(ModuleDependencyRules.FindViolations(graph));
        Assert.Equal("PolvorApp.Orders.Contracts", violation.Source);
        Assert.Equal("PolvorApp.Registry", violation.Target);
    }

    [Fact]
    public void Contracts_referencing_other_contracts_is_a_violation()
    {
        var graph = Graph(("PolvorApp.Orders.Contracts", ["PolvorApp.Registry.Contracts"]));

        var violation = Assert.Single(ModuleDependencyRules.FindViolations(graph));
        Assert.Equal("PolvorApp.Orders.Contracts", violation.Source);
        Assert.Equal("PolvorApp.Registry.Contracts", violation.Target);
    }

    [Theory]
    [InlineData("PolvorApp.Registry")]
    [InlineData("PolvorApp.Registry.Contracts")]
    [InlineData("PolvorApp.Api")]
    public void Shared_kernel_referencing_a_module_or_the_host_is_a_violation(string target)
    {
        var graph = Graph(("PolvorApp.SharedKernel", [target]));

        var violation = Assert.Single(ModuleDependencyRules.FindViolations(graph));
        Assert.Equal("PolvorApp.SharedKernel", violation.Source);
        Assert.Equal(target, violation.Target);
    }

    [Theory]
    [InlineData("PolvorApp.Api")]
    [InlineData("PolvorApp.Api.Hosting")]
    public void Module_referencing_the_api_host_is_a_violation(string host)
    {
        var graph = Graph(("PolvorApp.Registry", [host]));

        var violation = Assert.Single(ModuleDependencyRules.FindViolations(graph));
        Assert.Equal("PolvorApp.Registry", violation.Source);
        Assert.Equal(host, violation.Target);
    }

    [Fact]
    public void Module_whose_name_ends_in_tests_is_still_checked()
    {
        var graph = Graph(("PolvorApp.Contests", ["PolvorApp.Registry"]));

        var violation = Assert.Single(ModuleDependencyRules.FindViolations(graph));
        Assert.Equal("PolvorApp.Contests", violation.Source);
    }

    [Fact]
    public void Api_host_may_reference_every_module()
    {
        var graph = Graph(("PolvorApp.Api", ["PolvorApp.SharedKernel", "PolvorApp.Registry", "PolvorApp.Registry.Contracts"]));

        Assert.Empty(ModuleDependencyRules.FindViolations(graph));
    }
}
