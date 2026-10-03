using System.Text.Json.Serialization;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Exports.Contracts;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Exports.Definitions;

/// <summary>Who an export is for, which decides who may download it (spec: Who may export (BR-12)).</summary>
[JsonConverter(typeof(CodeEnumConverter<ExportAudience>))]
internal enum ExportAudience
{
    /// <summary>A recipient of the Federation (supplier, rental company, Arms Authority): Admins only, validated orders only.</summary>
    [JsonStringEnumMemberName("RECIPIENT")]
    Recipient,

    /// <summary>One comparsa's list: Admins and the comparsa's FiringChiefs, an order in any status.</summary>
    [JsonStringEnumMemberName("COMPARSA")]
    Comparsa,
}

/// <summary>
/// What a definition is built from, read once per request (design D3): the orders, the names of
/// their comparsas and weapon models, and the arquebusiers still in the registry, by id.
/// </summary>
internal sealed record ExportData(
    int EditionYear,
    IReadOnlyList<ExportedOrder> Orders,
    IReadOnlyDictionary<Guid, string> ComparsaNames,
    IReadOnlyDictionary<Guid, string> ModelLabels,
    IReadOnlyDictionary<Guid, RosterArquebusier> Arquebusiers)
{
    /// <summary>The type name only.</summary>
    public override string ToString() => nameof(ExportData);
}

/// <summary>A named, versioned export definition (ADR-0008; spec: Export definitions).</summary>
/// <remarks>The table's notices come first: a draft notice, then the provisional notice.</remarks>
internal interface IExportDefinition
{
    /// <summary>The name in routes, files and the audit trail, e.g. <c>arms-authority</c>.</summary>
    string Name { get; }

    /// <summary>The version written in the document; <see cref="ExportRows.ProvisionalVersion"/> until the real template.</summary>
    string Version { get; }

    /// <summary>True until the recipient's template arrives (Q-44).</summary>
    bool Provisional { get; }

    ExportAudience Audience { get; }

    /// <summary>The table, in the texts' language, from data already scoped and filtered by the caller.</summary>
    DocumentTable Build(ExportData data, ExportTexts texts);
}
