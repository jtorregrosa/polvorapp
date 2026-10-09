using PolvorApp.ComparsaOrders.Contracts;

namespace PolvorApp.Distribution.Handovers;

/// <summary>
/// The powder day for a device to capture handovers offline (spec: Offline capture package (UC-21)):
/// the holders as the printed list numbers them and the handovers already recorded. No license,
/// contact or birth data (SEC-06).
/// </summary>
internal sealed record CapturePackageResponse(
    Guid DistributionId,
    Guid EditionId,
    int EditionYear,
    string Date,
    string Location,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CaptureRowResponse> Rows,
    IReadOnlyList<HandoverResponse> Handovers)
{
    public override string ToString() => nameof(CapturePackageResponse);
}

/// <summary>One holder of the powder list: number, slot (HH:mm), comparsa, identity, kilograms, flask and proxy.</summary>
internal sealed record CaptureRowResponse(
    int Number,
    string? Slot,
    Guid ComparsaId,
    string ComparsaName,
    Guid EntryId,
    string LastName,
    string FirstName,
    string? NationalId,
    int PowderKg,
    FlaskOption Flask,
    CapturePersonResponse? Proxy)
{
    public override string ToString() => nameof(CaptureRowResponse);
}

/// <summary>The holder's powder proxy that holds on the day.</summary>
internal sealed record CapturePersonResponse(Guid EntryId, string LastName, string FirstName, string? NationalId)
{
    public override string ToString() => nameof(CapturePersonResponse);
}

/// <summary>A recorded handover (spec: Powder handovers (UC-21)), with its version for an undo.</summary>
internal sealed record HandoverResponse(
    Guid Id,
    Guid HolderEntryId,
    int DistributionNumber,
    HandoverCollector CollectedBy,
    Guid? CollectorEntryId,
    int PowderKg,
    string? RentalFlaskNumber,
    string? Traceability1,
    string? Traceability2,
    DateTimeOffset CollectedAt,
    DateTimeOffset RecordedAt,
    uint Version)
{
    public static HandoverResponse From(Handover handover)
    {
        ArgumentNullException.ThrowIfNull(handover);
        return new(
            handover.Id,
            handover.HolderEntryId,
            handover.DistributionNumber,
            handover.CollectedBy,
            handover.CollectorEntryId,
            handover.PowderKg,
            handover.RentalFlaskNumber,
            handover.Traceability1,
            handover.Traceability2,
            handover.CollectedAt,
            handover.RecordedAt,
            handover.Version);
    }
}
