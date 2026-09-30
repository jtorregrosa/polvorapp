using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json.Serialization;

namespace PolvorApp.SharedKernel.Codes;

/// <summary>
/// Culture-independent codes of an enum (e.g. <c>MOORISH</c>), taken from each member's
/// <see cref="JsonStringEnumMemberNameAttribute"/> so the API, the database and the UI share one
/// source of truth. Every member must declare a code; codes are compared ordinally.
/// </summary>
public static class EnumCodes
{
    /// <summary>Every code of <typeparamref name="TEnum"/>, in declaration order.</summary>
    public static IReadOnlyList<string> All<TEnum>()
        where TEnum : struct, Enum => Cache<TEnum>.All;

    public static string ToCode<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        Cache<TEnum>.CodeByValue.TryGetValue(value, out var code)
            ? code
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unknown {typeof(TEnum).Name}.");

    /// <summary>The value of <paramref name="code"/>, or null for an unknown code (invalid input).</summary>
    public static TEnum? FromCode<TEnum>(string? code)
        where TEnum : struct, Enum =>
        code is not null && Cache<TEnum>.ValueByCode.TryGetValue(code, out var value) ? value : null;

    /// <summary>Parses a stored code; an unknown code is corrupt data, never a default value.</summary>
    public static TEnum Parse<TEnum>(string code)
        where TEnum : struct, Enum =>
        FromCode<TEnum>(code) ?? throw new FormatException($"Unknown {typeof(TEnum).Name} code '{Shorten(code)}'.");

    /// <summary>Codes are short; a corrupt value is cut so it cannot flood a log line.</summary>
    private static string? Shorten(string? code) => code is { Length: > 32 } ? code[..32] + "…" : code;

    private static class Cache<TEnum>
        where TEnum : struct, Enum
    {
#pragma warning disable CA1000 // Private cache: one set of maps per enum type, built once.
        /// <summary>Members in declaration (metadata) order, each with its code.</summary>
        private static readonly (TEnum Value, string Code)[] Members =
            [.. typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => (
                (TEnum)field.GetValue(null)!,
                field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name
                    ?? throw new InvalidOperationException($"{typeof(TEnum).Name}.{field.Name} has no code.")))];

        public static readonly FrozenDictionary<TEnum, string> CodeByValue =
            Members.ToFrozenDictionary(member => member.Value, member => member.Code);

        public static readonly FrozenDictionary<string, TEnum> ValueByCode =
            Members.ToFrozenDictionary(member => member.Code, member => member.Value, StringComparer.Ordinal);

        public static readonly IReadOnlyList<string> All = Array.AsReadOnly(Members.Select(member => member.Code).ToArray());
#pragma warning restore CA1000
    }
}
