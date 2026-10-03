using System.Text.Json.Serialization;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Distribution.Proxies;

/// <summary>Why a registered proxy no longer holds (spec: Proxies that no longer hold).</summary>
[JsonConverter(typeof(CodeEnumConverter<ProxyProblem>))]
internal enum ProxyProblem
{
    /// <summary>The holder has nothing left to collect of the proxy's type.</summary>
    [JsonStringEnumMemberName("NOT_APPLICABLE")]
    NotApplicable,

    /// <summary>The proxy's license no longer holds on the reference date.</summary>
    [JsonStringEnumMemberName("LICENSE_INVALID")]
    LicenseInvalid,
}

/// <summary>
/// The rules of a pickup proxy (spec: Pickup proxies (UC-19, BR-06); design D5), pure so they are
/// unit-tested without a database: what a holder collects, the reference date, the proxy license rule
/// (blocking, a maintainer decision: an exception to compliance checks being warnings), the checks of a
/// registration in order, and the problem a stored proxy shows once its data changed.
/// </summary>
internal static class ProxyRules
{
    public const string NotInOrder = "notInOrder";
    public const string SameAsHolder = "sameAsHolder";
    public const string NothingToCollect = "nothingToCollect";
    public const string LicenseInvalid = "licenseInvalid";
    public const string ProxyAbsent = "proxyAbsent";

    /// <summary>For <c>POWDER</c>, an <c>ACTIVE</c> entry with powder; for <c>WEAPONS</c>, an <c>ACTIVE</c> entry renting a weapon.</summary>
    public static bool HasSomethingToCollect(EditionEntryFacts holder, DistributionType type)
    {
        ArgumentNullException.ThrowIfNull(holder);
        return holder.IsActive && type switch
        {
            DistributionType.Powder => holder.PowderKg > 0,
            DistributionType.Weapons => holder.WeaponSource == WeaponSource.Rental,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown distribution type."),
        };
    }

    /// <summary>The date of the distribution of that type, or the festival's first day while it is not planned.</summary>
    public static DateOnly ReferenceDate(DistributionType type, IReadOnlyDictionary<DistributionType, DateOnly> days, DateOnly festivalStartsOn)
    {
        ArgumentNullException.ThrowIfNull(days);
        return days.TryGetValue(type, out var date) ? date : festivalStartsOn;
    }

    /// <summary>An issued license of any type that does not expire before the reference date; never a pending or missing one.</summary>
    public static bool LicenseHolds(ArquebusierLicenseFacts? license, DateOnly reference) =>
        license is ArquebusierLicenseFacts.Issued issued && issued.ExpiresOn >= reference;

    /// <summary>
    /// The first rule a new proxy breaks, as the field and reason of a <c>400</c>, or null when it holds:
    /// the same order (BR-06), not the holder, something to collect, then the proxy's license. The
    /// absence and uniqueness rules are checked under the comparsa's lock, by the writer.
    /// </summary>
    /// <param name="proxyLicense">The proxy arquebusier's license, or null when they have none or left the registry.</param>
    public static (string Field, string Reason)? CheckRegistration(
        EditionEntryFacts holder, EditionEntryFacts proxy, DistributionType type, ArquebusierLicenseFacts? proxyLicense, DateOnly reference) =>
        CheckEntries(holder, proxy, type) ?? (LicenseHolds(proxyLicense, reference) ? null : ("proxyEntryId", LicenseInvalid));

    /// <summary>The rules that need only the two entries, checked before the proxy license is read.</summary>
    public static (string Field, string Reason)? CheckEntries(EditionEntryFacts holder, EditionEntryFacts proxy, DistributionType type)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(proxy);
        if (proxy.OrderId != holder.OrderId)
        {
            return ("proxyEntryId", NotInOrder);
        }

        if (proxy.EntryId == holder.EntryId)
        {
            return ("proxyEntryId", SameAsHolder);
        }

        return HasSomethingToCollect(holder, type) ? null : ("holderEntryId", NothingToCollect);
    }

    /// <summary>The problem a stored proxy shows now, or null when it still holds (the holder first, then the license).</summary>
    public static ProxyProblem? ProblemOf(EditionEntryFacts holder, DistributionType type, ArquebusierLicenseFacts? proxyLicense, DateOnly reference) =>
        !HasSomethingToCollect(holder, type) ? ProxyProblem.NotApplicable
        : !LicenseHolds(proxyLicense, reference) ? ProxyProblem.LicenseInvalid
        : null;
}
