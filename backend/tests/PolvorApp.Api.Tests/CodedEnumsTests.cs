using System.Reflection;
using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Api.Tests;

/// <summary>
/// Every enum the API serialises as a string code declares a code on every member (Modules
/// README, "Enum codes"), so a missing code fails here instead of on the first request.
/// </summary>
public sealed class CodedEnumsTests
{
    [Fact]
    public void Every_string_coded_enum_declares_a_code_on_every_member()
    {
        var enums = PolvorAppAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsEnum && IsStringCoded(type))
            .ToList();

        Assert.Contains(enums, type => type.Name == "Side");
        Assert.Contains(enums, type => type.Name == "UserRole");
        Assert.Contains(enums, type => type.Name == "ArquebusierStatus");
        Assert.Contains(enums, type => type.Name == "Gender");
        Assert.Contains(enums, type => type.Name == "LicenseType");
        Assert.Contains(enums, type => type.Name == "LicenseStatus");
        Assert.Contains(enums, type => type.Name == "ComplianceWarning");
        Assert.Contains(enums, type => type.Name == "BillingConcept");
        Assert.Contains(enums, type => type.Name == "BillingState");
        Assert.Contains(enums, type => type.Name == "ExportFormat");
        Assert.Contains(enums, type => type.Name == "ExportAudience");
        foreach (var type in enums)
        {
            var all = typeof(EnumCodes).GetMethod(nameof(EnumCodes.All))!.MakeGenericMethod(type);
            var codes = (IReadOnlyList<string>)all.Invoke(null, null)!;
            Assert.Equal(Enum.GetValues(type).Length, codes.Distinct(StringComparer.Ordinal).Count());
        }
    }

    private static bool IsStringCoded(Type type) =>
        type.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType is { IsGenericType: true } converter
        && converter.GetGenericTypeDefinition() is var definition
        && (definition == typeof(CodeEnumConverter<>) || definition == typeof(JsonStringEnumConverter<>));

    private static List<Assembly> PolvorAppAssemblies()
    {
        var found = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Stack<Assembly>([typeof(Program).Assembly]);
        while (pending.TryPop(out var assembly))
        {
            if (!found.TryAdd(assembly.GetName().Name!, assembly))
            {
                continue;
            }

            foreach (var reference in assembly.GetReferencedAssemblies().Where(r => r.Name!.StartsWith("PolvorApp.", StringComparison.Ordinal)))
            {
                pending.Push(Assembly.Load(reference));
            }
        }

        return [.. found.Values];
    }
}
