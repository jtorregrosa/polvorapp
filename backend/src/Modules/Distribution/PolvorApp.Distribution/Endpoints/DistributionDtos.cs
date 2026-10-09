using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.FestivalEditions.Contracts;

namespace PolvorApp.Distribution.Endpoints;

/// <summary>Plan a day. The type and date arrive as text so an invalid value is reported by field name.</summary>
/// <param name="Type"><c>POWDER</c> or <c>WEAPONS</c>.</param>
/// <param name="Date">ISO date, <c>yyyy-MM-dd</c>, within the edition's year and not after the festival.</param>
/// <param name="Location">1 to 200 characters after trimming, on one line.</param>
internal sealed record PlanDayRequest(string? Type, string? Date, string? Location);

/// <summary>Edit a day, with the version it was read at (required).</summary>
internal sealed record EditDayRequest(string? Date, string? Location, uint? Version);

/// <summary>A slot to save: a comparsa and its start time, <c>HH:mm</c>.</summary>
internal sealed record SlotRequest(Guid? ComparsaId, string? StartsAt);

/// <summary>
/// The whole set of a day's slots, which replaces the previous set, with the day's version (both
/// required: an empty list clears the slots, a missing one is refused rather than taken as empty).
/// </summary>
internal sealed record SaveSlotsRequest(uint? Version, IReadOnlyList<SlotRequest>? Slots);

/// <summary>A comparsa as the distribution page names it.</summary>
internal sealed record ComparsaRef(Guid Id, string Name);

/// <summary>A comparsa's slot.</summary>
/// <param name="ComparsaId">The comparsa.</param>
/// <param name="ComparsaName">Its name.</param>
/// <param name="StartsAt">Start time, <c>HH:mm</c>.</param>
internal sealed record SlotResponse(Guid ComparsaId, string ComparsaName, string StartsAt);

/// <summary>A distribution day as the API returns it.</summary>
/// <param name="Id">Day identifier.</param>
/// <param name="EditionId">Its edition.</param>
/// <param name="Type">What it hands out.</param>
/// <param name="Date">Its date.</param>
/// <param name="Location">Where.</param>
/// <param name="Version">The version an edit, deletion or slot save must carry.</param>
/// <param name="Slots">Slots in time order, then by comparsa name: every one for Admins, their own for FiringChiefs.</param>
/// <param name="WithoutSlot">For Admins, the active comparsas with no slot that day; null for FiringChiefs.</param>
internal sealed record DistributionDayResponse(
    Guid Id,
    Guid EditionId,
    DistributionType Type,
    DateOnly Date,
    string Location,
    uint Version,
    IReadOnlyList<SlotResponse> Slots,
    IReadOnlyList<ComparsaRef>? WithoutSlot,
    HandoverCountResponse? Handovers = null);

/// <summary>The powder day's handovers recorded out of its holders (spec: Handover screens); Admins only.</summary>
internal sealed record HandoverCountResponse(int Recorded, int Holders);

/// <summary>A comparsa whose order is not validated, and its status (null when not prepared).</summary>
internal sealed record NotValidatedResponse(Guid ComparsaId, string ComparsaName, OrderStatus? Status);

/// <summary>An edition's distribution plan (spec: Distribution screens).</summary>
/// <param name="EditionId">The edition.</param>
/// <param name="EditionYear">Its year.</param>
/// <param name="EditionStatus">Its status.</param>
/// <param name="Days">Its planned days: at most one of each type.</param>
/// <param name="CanPlan">Whether the user may plan, edit and delete days and slots now (Admins, edition in progress).</param>
/// <param name="CanManageProxies">Whether the user may register and remove proxies now.</param>
/// <param name="NotValidated">For Admins, the active comparsas whose orders are not validated, which the lists leave out; null for FiringChiefs.</param>
internal sealed record DistributionPlanResponse(
    Guid EditionId,
    int EditionYear,
    EditionStatus EditionStatus,
    IReadOnlyList<DistributionDayResponse> Days,
    bool CanPlan,
    bool CanManageProxies,
    IReadOnlyList<NotValidatedResponse>? NotValidated);

/// <summary>Register a pickup proxy: the holder's and the proxy's entries and the type, as text so an invalid value is named.</summary>
internal sealed record ProxyRequest(Guid? HolderEntryId, Guid? ProxyEntryId, string? Type);
