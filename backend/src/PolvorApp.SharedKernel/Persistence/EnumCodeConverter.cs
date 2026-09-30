using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.SharedKernel.Persistence;

/// <summary>
/// Stores an enum as its <see cref="EnumCodes"/> code (e.g. <c>MOORISH</c>), so rows stay readable
/// and survive C# renames. An unknown stored code fails loudly instead of becoming a default.
/// </summary>
/// <typeparam name="TEnum">An enum whose members all declare a code.</typeparam>
public sealed class EnumCodeConverter<TEnum>() : ValueConverter<TEnum, string>(
    value => EnumCodes.ToCode(value),
    code => EnumCodes.Parse<TEnum>(code))
    where TEnum : struct, Enum;
