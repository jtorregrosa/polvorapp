using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Distribution.Handovers;

/// <summary>
/// Who collected a holder's powder (spec: Powder handovers (UC-21)): the holder, or their powder proxy.
/// Kept apart from the collector's entry, so a proxy whose entry is later removed still reads "proxy".
/// </summary>
[JsonConverter(typeof(CodeEnumConverter<HandoverCollector>))]
public enum HandoverCollector
{
    [JsonStringEnumMemberName("HOLDER")]
    Holder,

    [JsonStringEnumMemberName("PROXY")]
    Proxy,
}

/// <summary>
/// One holder's powder handed over on the powder day (glossary: <c>Handover</c>; spec: Powder handovers
/// (UC-21)). Captured on a device, often offline, and recorded when it syncs: the id comes from the
/// device and makes the sync idempotent (design D2). Never edited; undone by deleting it. It holds no
/// identity of its own, so it outlives an anonymised entry (UC-26) and goes with a removed one (BR-14).
/// </summary>
internal sealed class Handover
{
    public const int FlaskNumberMaxLength = 20;

    public const int TraceabilityMaxLength = 50;

    public required Guid Id { get; init; }

    public required Guid DistributionId { get; init; }

    public required Guid HolderEntryId { get; init; }

    /// <summary>The number the capture package showed: a record of the day, never the list's numbering.</summary>
    public required int DistributionNumber { get; init; }

    public required HandoverCollector CollectedBy { get; init; }

    /// <summary>The proxy's entry when <see cref="CollectedBy"/> is a proxy; null once that entry is removed.</summary>
    public Guid? CollectorEntryId { get; init; }

    /// <summary>The entry's kilograms when it was recorded (BR-05), not the device's copy.</summary>
    public required short PowderKg { get; init; }

    public string? RentalFlaskNumber { get; init; }

    public string? Traceability1 { get; init; }

    public string? Traceability2 { get; init; }

    /// <summary>The device's time: informational, never used to order anything.</summary>
    public required DateTimeOffset CollectedAt { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public uint Version { get; init; }
}
