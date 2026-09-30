using System.Text.Json.Serialization;

namespace PolvorApp.SharedKernel.Codes;

/// <summary>
/// JSON converter for coded enums: writes and reads the <see cref="JsonStringEnumMemberNameAttribute"/>
/// codes and, unlike the default, rejects integers, so a response never carries an unnamed value.
/// Request DTOs still take codes as text and parse them with <see cref="EnumCodes"/>, which also
/// rejects case and whitespace variants.
/// </summary>
/// <typeparam name="TEnum">An enum whose members all declare a code.</typeparam>
public sealed class CodeEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(namingPolicy: null, allowIntegerValues: false)
    where TEnum : struct, Enum;
