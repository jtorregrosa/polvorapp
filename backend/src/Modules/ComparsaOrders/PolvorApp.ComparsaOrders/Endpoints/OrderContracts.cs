using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComplianceInsights.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Text;

namespace PolvorApp.ComparsaOrders.Endpoints;

/// <summary>Prepare the order of a comparsa for an edition (spec: Preparing an order (UC-12)).</summary>
/// <param name="EditionId">The edition; it must have left preparation.</param>
/// <param name="ComparsaId">An active comparsa of the caller's scope.</param>
internal sealed record PrepareOrderRequest(Guid? EditionId, Guid? ComparsaId);

/// <summary>Add an arquebusier of the order's comparsa who has no entry in the edition (spec: Adding arquebusiers to an order (UC-12)).</summary>
/// <param name="ArquebusierId">The arquebusier to add.</param>
internal sealed record AddEntryRequest(Guid? ArquebusierId);

/// <summary>Replace the values of an entry (spec: Edition entries (BR-05, BR-07)); codes are case-sensitive.</summary>
/// <param name="Version">The entry version the edit is based on.</param>
/// <param name="Status"><c>ACTIVE</c> or <c>RESERVE</c>, for this edition only.</param>
/// <param name="PowderKg">0, 1 or 2.</param>
/// <param name="CapsBoxes">0 to 99.</param>
/// <param name="CapsType"><c>NORMAL</c> or <c>SMALL</c>, exactly when there are caps boxes.</param>
/// <param name="WeaponSource"><c>OWNED</c>, <c>RENTAL</c>, <c>LOAN</c> or <c>NONE</c>.</param>
/// <param name="OwnedWeaponId">For <c>OWNED</c>: one of the arquebusier's owned weapons.</param>
/// <param name="RentalWeaponModelId">For <c>RENTAL</c>: a model offered for rental in the edition.</param>
/// <param name="Loan">For <c>LOAN</c>: the lender.</param>
/// <param name="Flask"><c>OWNED</c>, <c>RENTAL_1KG</c>, <c>RENTAL_2KG</c> or <c>NONE</c>.</param>
internal sealed record EditEntryRequest(
    uint? Version,
    string? Status,
    int? PowderKg,
    int? CapsBoxes,
    string? CapsType,
    string? WeaponSource,
    Guid? OwnedWeaponId,
    Guid? RentalWeaponModelId,
    LoanRequest? Loan,
    string? Flask)
{
    public EntryFields ToFields() => new(
        Status,
        PowderKg,
        CapsBoxes,
        CapsType,
        WeaponSource,
        OwnedWeaponId,
        RentalWeaponModelId,
        Loan is null ? null : new LoanFields(Loan.OwnedWeaponId, Loan.External?.ToFields()),
        Flask);
}

/// <summary>The lender of a loan: exactly one of a registered owner's weapon or an external owner (spec: Weapon loans (UC-13, BR-09)).</summary>
/// <param name="OwnedWeaponId">An owned weapon of a registered arquebusier of any comparsa, found with the lender lookup.</param>
/// <param name="External">An owner who is not in PolvorApp.</param>
internal sealed record LoanRequest(Guid? OwnedWeaponId, ExternalLenderRequest? External);

/// <summary>An external owner and their weapon.</summary>
/// <param name="FirstName">1 to 100 characters, on one line.</param>
/// <param name="LastName">1 to 100 characters, on one line.</param>
/// <param name="NationalId">A valid DNI or NIE (BR-01) that no arquebusier has.</param>
/// <param name="WeaponModelId">An active catalogue model, of any kind.</param>
/// <param name="WeaponNumber">1 to 30 characters.</param>
/// <param name="OwnershipGuideNumber">1 to 30 characters; stored upper-cased.</param>
internal sealed record ExternalLenderRequest(
    string? FirstName,
    string? LastName,
    string? NationalId,
    Guid? WeaponModelId,
    string? WeaponNumber,
    string? OwnershipGuideNumber)
{
    public ExternalLenderFields ToFields() => new(FirstName, LastName, NationalId, WeaponModelId, WeaponNumber, OwnershipGuideNumber);
}

/// <summary>The edition of an order.</summary>
/// <param name="Id">Edition identifier.</param>
/// <param name="Year">Edition year.</param>
/// <param name="Status">Edition lifecycle status.</param>
/// <param name="OrdersOpen">Whether FiringChiefs may edit orders now (BR-10).</param>
internal sealed record OrderEditionResponse(Guid Id, int Year, EditionStatus Status, bool OrdersOpen);

/// <summary>The comparsa of an order.</summary>
/// <param name="Id">Comparsa identifier.</param>
/// <param name="Name">Comparsa name.</param>
/// <param name="Side">Its side.</param>
internal sealed record OrderComparsaResponse(Guid Id, string Name, Side Side);

/// <summary>The latest submission of an order.</summary>
/// <param name="At">When it was submitted.</param>
/// <param name="ByName">Who submitted it.</param>
/// <param name="Attested">A FiringChief confirmed the attestation.</param>
/// <param name="ByAdmin">An Admin submitted it on the comparsa's behalf, without the attestation.</param>
internal sealed record OrderSubmissionResponse(DateTimeOffset At, string? ByName, bool Attested, bool ByAdmin);

/// <summary>The latest review of an order.</summary>
/// <param name="At">When it was reviewed.</param>
/// <param name="ByName">Which Admin reviewed it.</param>
/// <param name="ReturnReason">The reason, while the order is returned.</param>
internal sealed record OrderReviewResponse(DateTimeOffset At, string? ByName, string? ReturnReason);

/// <summary>Why the caller may not edit an order: <c>ordersClosed</c> or <c>validated</c>.</summary>
internal static class ReadOnlyReasons
{
    public const string OrdersClosed = "ordersClosed";
    public const string Validated = "validated";
}

/// <summary>A comparsa order with its entries (spec: Orders screens).</summary>
/// <param name="Id">Order identifier.</param>
/// <param name="Version">Order version, for submissions and reviews.</param>
/// <param name="Status">Order status.</param>
/// <param name="Edition">Its edition.</param>
/// <param name="Comparsa">Its comparsa.</param>
/// <param name="PreparedAt">When it was prepared.</param>
/// <param name="Submission">The latest submission, if any.</param>
/// <param name="Review">The latest review, if any.</param>
/// <param name="CanEdit">Whether the caller may edit it now (BR-10).</param>
/// <param name="ReadOnlyReason">Why not, when <paramref name="CanEdit"/> is false.</param>
/// <param name="Entries">The entries, by last and first name in Spanish order.</param>
/// <param name="NotInOrder">Arquebusiers of the comparsa with no entry in the edition.</param>
/// <param name="LentOut">Owned weapons of the comparsa's arquebusiers lent in the edition (BR-12 exception).</param>
/// <param name="Totals">The order's totals.</param>
/// <param name="OfferedModels">The models offered for rental in the edition now (BR-07), by label: the entry panel's rental choices.</param>
internal sealed record OrderResponse(
    Guid Id,
    uint Version,
    OrderStatus Status,
    OrderEditionResponse Edition,
    OrderComparsaResponse Comparsa,
    DateTimeOffset PreparedAt,
    OrderSubmissionResponse? Submission,
    OrderReviewResponse? Review,
    bool CanEdit,
    string? ReadOnlyReason,
    IReadOnlyList<EntryResponse> Entries,
    IReadOnlyList<NotInOrderResponse> NotInOrder,
    IReadOnlyList<LentOutResponse> LentOut,
    OrderTotalsResponse Totals,
    IReadOnlyList<WeaponModelReference> OfferedModels);

/// <summary>
/// An owned weapon of the comparsa's arquebusiers lent in the edition, as the lender's comparsa sees it
/// (spec: Order visibility (BR-12)): the weapon, its owner, and the borrower's name and comparsa only.
/// </summary>
/// <param name="LoanId">The loan.</param>
/// <param name="WeaponModel">The weapon's model.</param>
/// <param name="WeaponNumber">The weapon's number.</param>
/// <param name="LenderFirstName">The owner's first name.</param>
/// <param name="LenderLastName">The owner's last name.</param>
/// <param name="BorrowerFirstName">The borrower's first name.</param>
/// <param name="BorrowerLastName">The borrower's last name.</param>
/// <param name="BorrowerComparsaName">The comparsa whose order has the borrower's entry.</param>
internal sealed record LentOutResponse(
    Guid LoanId,
    WeaponModelReference? WeaponModel,
    string? WeaponNumber,
    string? LenderFirstName,
    string? LenderLastName,
    string? BorrowerFirstName,
    string? BorrowerLastName,
    string BorrowerComparsaName);

/// <summary>The arquebusier of an entry: their current registry data, or the entry's copy once they left it.</summary>
/// <param name="Id">The arquebusier, while they are in the registry.</param>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name.</param>
/// <param name="NationalId">Normalised DNI or NIE.</param>
/// <param name="FederationId">ID in the Federation's external app.</param>
/// <param name="InRegistry">False once the arquebusier was deleted from the registry.</param>
internal sealed record EntryArquebusierResponse(Guid? Id, string? FirstName, string? LastName, string? NationalId, int? FederationId, bool InRegistry);

/// <summary>An owned weapon of the entry's arquebusier, without its ownership guide: an owned-weapon choice.</summary>
/// <param name="Id">The owned weapon, while it is in the registry.</param>
/// <param name="WeaponModelId">Its catalogue model.</param>
/// <param name="ModelLabel">The model's Federation label.</param>
/// <param name="WeaponNumber">Number engraved on the stock.</param>
/// <param name="Removed">True once the weapon was removed from the registry.</param>
internal sealed record EntryWeaponResponse(Guid? Id, Guid? WeaponModelId, string? ModelLabel, string? WeaponNumber, bool Removed);

/// <summary>The owned weapon of an <c>OWNED</c> entry.</summary>
/// <param name="Id">The owned weapon, while it is in the registry.</param>
/// <param name="WeaponModelId">Its catalogue model.</param>
/// <param name="ModelLabel">The model's Federation label.</param>
/// <param name="WeaponNumber">Number engraved on the stock.</param>
/// <param name="OwnershipGuideNumber">
/// The ownership guide from the entry's copy, once the weapon was removed from the registry (spec:
/// Owned weapon kept in the history); null while it is there.
/// </param>
/// <param name="Removed">True once the weapon was removed from the registry.</param>
internal sealed record EntryOwnedWeaponResponse(
    Guid? Id, Guid? WeaponModelId, string? ModelLabel, string? WeaponNumber, string? OwnershipGuideNumber, bool Removed);

/// <summary>A catalogue weapon model, by id and label.</summary>
/// <param name="Id">Model identifier.</param>
/// <param name="Label">Federation label.</param>
internal sealed record WeaponModelReference(Guid Id, string Label);

/// <summary>One entry of an order.</summary>
/// <param name="Id">Entry identifier.</param>
/// <param name="Version">Entry version, for edits.</param>
/// <param name="Arquebusier">The arquebusier.</param>
/// <param name="Status">Status for this edition.</param>
/// <param name="PowderKg">Powder in kilograms.</param>
/// <param name="CapsBoxes">Caps boxes.</param>
/// <param name="CapsType">Caps type, when there are boxes.</param>
/// <param name="WeaponSource">Where the weapon comes from.</param>
/// <param name="OwnedWeapon">The owned weapon, for <c>OWNED</c>.</param>
/// <param name="RentalWeaponModel">The rental model, for <c>RENTAL</c>.</param>
/// <param name="Loan">The loan, for <c>LOAN</c>.</param>
/// <param name="Flask">The powder flask.</param>
/// <param name="Warnings">Compliance warnings on the festival dates (BR-04), for <c>ACTIVE</c> entries of the edition in progress.</param>
/// <param name="Issues">
/// Data problems that block the submission and validation: <c>ownedWeaponMissing</c>,
/// <c>loanWeaponMissing</c> or <c>rentalModelNotOffered</c>.
/// </param>
/// <param name="FirstYear">
/// Whether the arquebusier is in their first year in the order's edition (UC-07); null once they left
/// the registry, or while no earlier edition has orders.
/// </param>
/// <param name="OwnedWeapons">
/// The arquebusier's owned weapons now, oldest first, without their ownership guides: the entry
/// panel's owned-weapon choices. Empty once they left the registry.
/// </param>
internal sealed record EntryResponse(
    Guid Id,
    uint Version,
    EntryArquebusierResponse Arquebusier,
    ArquebusierStatus Status,
    int PowderKg,
    int CapsBoxes,
    CapsType? CapsType,
    WeaponSource WeaponSource,
    EntryOwnedWeaponResponse? OwnedWeapon,
    WeaponModelReference? RentalWeaponModel,
    EntryLoanResponse? Loan,
    FlaskOption Flask,
    IReadOnlyList<ComplianceWarning> Warnings,
    IReadOnlyList<string> Issues,
    bool? FirstYear,
    IReadOnlyList<EntryWeaponResponse> OwnedWeapons);

/// <summary>
/// The loan of a <c>LOAN</c> entry, as the borrower's comparsa sees it (BR-12 exception): the lender's
/// name and comparsa, and the weapon. The ownership guide and DNI are shown only for an external owner,
/// whose data the borrower's comparsa typed; a registered owner's never leave the server.
/// </summary>
/// <param name="LenderKind"><c>ARQUEBUSIER</c> or <c>EXTERNAL</c>.</param>
/// <param name="LenderFirstName">The lender's first name.</param>
/// <param name="LenderLastName">The lender's last name.</param>
/// <param name="LenderComparsaName">A registered lender's comparsa.</param>
/// <param name="OwnedWeaponId">A registered lender's weapon, while it is in the registry.</param>
/// <param name="WeaponModel">The weapon's model.</param>
/// <param name="WeaponNumber">The weapon's number.</param>
/// <param name="OwnershipGuideNumber">An external owner's ownership guide.</param>
/// <param name="ExternalNationalId">An external owner's DNI or NIE, as typed by the borrower's comparsa.</param>
/// <param name="WeaponRemoved">True once a registered lender's weapon left the registry.</param>
internal sealed record EntryLoanResponse(
    LenderKind LenderKind,
    string? LenderFirstName,
    string? LenderLastName,
    string? LenderComparsaName,
    Guid? OwnedWeaponId,
    WeaponModelReference? WeaponModel,
    string? WeaponNumber,
    string? OwnershipGuideNumber,
    string? ExternalNationalId,
    bool WeaponRemoved);

/// <summary>An arquebusier of the comparsa who has no entry in the edition.</summary>
/// <param name="ArquebusierId">The arquebusier.</param>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name.</param>
/// <param name="Status">Registry status, the status of the new entry.</param>
internal sealed record NotInOrderResponse(Guid ArquebusierId, string FirstName, string LastName, ArquebusierStatus Status);

/// <summary>Look up a lender by an exact DNI or NIE (spec: Lender lookup (UC-13, BR-12)); sent in the body, never in the address.</summary>
/// <param name="NationalId">A valid DNI or NIE.</param>
internal sealed record LenderLookupRequest(string? NationalId);

/// <summary>The lookup's answer: a registered lender, or that the owner is not registered (an external owner).</summary>
/// <param name="Registered">Whether an arquebusier has that national ID.</param>
/// <param name="Lender">The registered lender, when there is one.</param>
internal sealed record LenderLookupResponse(bool Registered, LenderResponse? Lender);

/// <summary>A registered lender: names, comparsa and weapons only (no ownership guide, birth date, contact or license).</summary>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name.</param>
/// <param name="ComparsaName">The lender's comparsa.</param>
/// <param name="Weapons">The lender's owned weapons.</param>
internal sealed record LenderResponse(string FirstName, string LastName, string ComparsaName, IReadOnlyList<LenderWeaponResponse> Weapons);

/// <summary>One of a lender's owned weapons, without its ownership guide.</summary>
/// <param name="Id">Owned weapon identifier, to name in the loan.</param>
/// <param name="WeaponModel">Its model.</param>
/// <param name="WeaponNumber">Number engraved on the stock.</param>
internal sealed record LenderWeaponResponse(Guid Id, WeaponModelReference WeaponModel, string WeaponNumber);

/// <summary>Submit an order (spec: Submitting an order (UC-14)).</summary>
/// <param name="Version">The order version the submission is based on.</param>
/// <param name="Attestation">
/// A FiringChief confirms that the arquebusiers of the order meet the requirements (license, course,
/// legal age); required for a FiringChief, ignored for an Admin, who submits on the comparsa's behalf.
/// </param>
internal sealed record SubmitOrderRequest(uint? Version, bool? Attestation);

/// <summary>Validate an order (spec: Reviewing orders (UC-15)).</summary>
/// <param name="Version">The order version the review is based on.</param>
internal sealed record ValidateOrderRequest(uint? Version);

/// <summary>Return an order with a reason (spec: Reviewing orders (UC-15)).</summary>
/// <param name="Version">The order version the review is based on.</param>
/// <param name="Reason">1 to 500 characters after trimming; it may span several lines.</param>
internal sealed record ReturnOrderRequest(uint? Version, string? Reason);

/// <summary>A number of weapon rentals of one model.</summary>
/// <param name="WeaponModel">The model.</param>
/// <param name="Count">How many entries rent it.</param>
internal sealed record ModelCountResponse(WeaponModelReference WeaponModel, int Count);

/// <summary>The totals of one or several orders (spec: Order totals and dashboard (UC-16)).</summary>
/// <param name="Active">Entries with status <c>ACTIVE</c>.</param>
/// <param name="Reserve">Entries with status <c>RESERVE</c>.</param>
/// <param name="PowderKg">Powder in kilograms.</param>
/// <param name="NormalCapsBoxes">Caps boxes of type <c>NORMAL</c>.</param>
/// <param name="SmallCapsBoxes">Caps boxes of type <c>SMALL</c>.</param>
/// <param name="WeaponRentals">Weapon rentals by model, by label.</param>
/// <param name="FlaskRentals1Kg">Rented 1 kg flasks.</param>
/// <param name="FlaskRentals2Kg">Rented 2 kg flasks.</param>
/// <param name="Loans">Entries with a lent weapon.</param>
/// <param name="OwnedWeapons">Entries with an owned weapon.</param>
/// <param name="EntriesWithWarnings"><c>ACTIVE</c> entries with compliance warnings, for the edition in progress.</param>
internal sealed record OrderTotalsResponse(
    int Active,
    int Reserve,
    int PowderKg,
    int NormalCapsBoxes,
    int SmallCapsBoxes,
    IReadOnlyList<ModelCountResponse> WeaponRentals,
    int FlaskRentals1Kg,
    int FlaskRentals2Kg,
    int Loans,
    int OwnedWeapons,
    int EntriesWithWarnings)
{
    public static OrderTotalsResponse From(Totals.OrderTotals totals, IReadOnlyDictionary<Guid, string> labels) => new(
        totals.Active,
        totals.Reserve,
        totals.PowderKg,
        totals.NormalCapsBoxes,
        totals.SmallCapsBoxes,
        [.. totals.WeaponRentals
            .Select(r => new ModelCountResponse(
                new WeaponModelReference(r.Key, labels.TryGetValue(r.Key, out var label) ? label : throw new InvalidOperationException($"Weapon model {r.Key} is missing from the catalogue.")),
                r.Value))
            .OrderBy(r => r.WeaponModel.Label, SpanishOrder.Names)
            .ThenBy(r => r.WeaponModel.Id)],
        totals.FlaskRentals1Kg,
        totals.FlaskRentals2Kg,
        totals.Loans,
        totals.OwnedWeapons,
        totals.EntriesWithWarnings);
}

/// <summary>One comparsa in the orders overview.</summary>
/// <param name="Comparsa">The comparsa.</param>
/// <param name="OrderId">Its order in the edition; null when not prepared.</param>
/// <param name="Status">The order's status; null when not prepared.</param>
/// <param name="Totals">The order's totals; null when not prepared.</param>
/// <param name="CanPrepare">Whether the caller may prepare its order now.</param>
internal sealed record OverviewRowResponse(OrderComparsaResponse Comparsa, Guid? OrderId, OrderStatus? Status, OrderTotalsResponse? Totals, bool CanPrepare);

/// <summary>How many comparsas have their order in each status, "not prepared" included (Admins).</summary>
/// <param name="NotPrepared">Comparsas without an order.</param>
/// <param name="Draft">Orders in <c>DRAFT</c>.</param>
/// <param name="Submitted">Orders in <c>SUBMITTED</c>.</param>
/// <param name="Returned">Orders in <c>RETURNED</c>.</param>
/// <param name="Validated">Orders in <c>VALIDATED</c>.</param>
internal sealed record OverviewStatusCountsResponse(int NotPrepared, int Draft, int Submitted, int Returned, int Validated);

/// <summary>The orders overview of an edition (spec: Order totals and dashboard (UC-16)).</summary>
/// <param name="Edition">The edition; null when no edition is in progress and none was asked for.</param>
/// <param name="Rows">The comparsas, by name in Spanish order.</param>
/// <param name="StatusCounts">Admins only.</param>
/// <param name="EditionTotals">The totals of every order of the edition; Admins only.</param>
internal sealed record OverviewResponse(
    OrderEditionResponse? Edition,
    IReadOnlyList<OverviewRowResponse> Rows,
    OverviewStatusCountsResponse? StatusCounts,
    OrderTotalsResponse? EditionTotals);
