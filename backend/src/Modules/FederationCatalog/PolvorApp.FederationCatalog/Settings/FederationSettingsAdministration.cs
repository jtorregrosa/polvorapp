using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.FederationCatalog.Settings;

/// <summary>The settings sections an Admin saves one at a time (spec: Federation settings; design D2).</summary>
internal enum SettingsSection
{
    Identity,
    Emails,
    Orders,
    Calendar,
}

/// <summary>The identity section: names and the Federation's public contact.</summary>
internal sealed record IdentityFields(string OfficialNameEs, string OfficialNameCa, string ShortName, string? ContactEmail, string? Website);

/// <summary>The emails section: the sender name and the reply-to address.</summary>
internal sealed record EmailFields(string SenderName, string? ReplyTo);

/// <summary>The orders section: the first close reminder lead time.</summary>
internal sealed record OrderFields(int CloseReminderLeadDays);

/// <summary>The calendar section: the milestone reminder lead time.</summary>
internal sealed record CalendarFields(int MilestoneLeadDays);

/// <summary>How a settings save ended.</summary>
internal enum SettingsOutcome
{
    Done,

    /// <summary>The save was based on an older version (<c>federationSettings.modified</c>).</summary>
    Modified,

    /// <summary>The row stayed locked; retryable.</summary>
    Busy,
}

/// <summary>
/// Reads and saves the Federation settings (spec: Federation settings; add-federation-settings, design
/// D2). Each section is saved on its own under the row lock, against the version the Admin read, and
/// audited with its previous and new values; an unchanged save records nothing. Writes are Admin-only
/// at the endpoint.
/// </summary>
internal sealed class FederationSettingsAdministration(FederationCatalogDbContext db, IAuditTrail trail, TimeProvider time)
{
    /// <summary>The audit entity type of the settings row, its logo included.</summary>
    public const string EntityType = "FederationSettings";

    public Task<FederationSettings> GetAsync(CancellationToken cancellationToken) =>
        db.FederationSettings.AsNoTracking().SingleAsync(cancellationToken);

    public Task<(SettingsOutcome Outcome, FederationSettings? Settings)> SaveIdentityAsync(uint version, IdentityFields fields, CancellationToken cancellationToken) =>
        SaveAsync(SettingsSection.Identity, version, fields, Identity, (settings, value) =>
        {
            settings.OfficialNameEs = value.OfficialNameEs;
            settings.OfficialNameCa = value.OfficialNameCa;
            settings.ShortName = value.ShortName;
            settings.ContactEmail = value.ContactEmail;
            settings.Website = value.Website;
        }, cancellationToken);

    public Task<(SettingsOutcome Outcome, FederationSettings? Settings)> SaveEmailsAsync(uint version, EmailFields fields, CancellationToken cancellationToken) =>
        SaveAsync(SettingsSection.Emails, version, fields, Emails, (settings, value) =>
        {
            settings.SenderName = value.SenderName;
            settings.ReplyTo = value.ReplyTo;
        }, cancellationToken);

    public Task<(SettingsOutcome Outcome, FederationSettings? Settings)> SaveOrdersAsync(uint version, OrderFields fields, CancellationToken cancellationToken) =>
        SaveAsync(SettingsSection.Orders, version, fields, Orders, (settings, value) => settings.CloseReminderLeadDays = value.CloseReminderLeadDays, cancellationToken);

    public Task<(SettingsOutcome Outcome, FederationSettings? Settings)> SaveCalendarAsync(uint version, CalendarFields fields, CancellationToken cancellationToken) =>
        SaveAsync(SettingsSection.Calendar, version, fields, Calendar, (settings, value) => settings.MilestoneLeadDays = value.MilestoneLeadDays, cancellationToken);

    private static IdentityFields Identity(FederationSettings s) => new(s.OfficialNameEs, s.OfficialNameCa, s.ShortName, s.ContactEmail, s.Website);

    private static EmailFields Emails(FederationSettings s) => new(s.SenderName, s.ReplyTo);

    private static OrderFields Orders(FederationSettings s) => new(s.CloseReminderLeadDays);

    private static CalendarFields Calendar(FederationSettings s) => new(s.MilestoneLeadDays);

    private async Task<(SettingsOutcome Outcome, FederationSettings? Settings)> SaveAsync<TFields>(
        SettingsSection section,
        uint version,
        TFields current,
        Func<FederationSettings, TFields> read,
        Action<FederationSettings, TFields> apply,
        CancellationToken cancellationToken)
        where TFields : IEquatable<TFields>
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var settings = await db.LockFederationSettingsAsync(cancellationToken);
            if (settings.Version != version)
            {
                return (SettingsOutcome.Modified, null);
            }

            var previous = read(settings);
            if (previous.Equals(current))
            {
                return (SettingsOutcome.Done, settings);
            }

            apply(settings, current);
            settings.UpdatedAt = time.GetUtcNow();
            trail.Record(db, new AuditRecord(
                FederationCatalogAuditActions.FederationSettingsChanged,
                EntityType,
                FederationSettings.SingletonId.ToString(CultureInfo.InvariantCulture),
                new { section = SectionCode(section), previous, current }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (SettingsOutcome.Done, settings);
        }
        catch (Exception exception) when (CatalogLocks.IsRetryable(exception))
        {
            db.ChangeTracker.Clear();
            return (SettingsOutcome.Busy, null);
        }
    }

    /// <summary>The section as the API path and the audit entry name it.</summary>
    public static string SectionCode(SettingsSection section) => section switch
    {
        SettingsSection.Identity => "identity",
        SettingsSection.Emails => "emails",
        SettingsSection.Orders => "orders",
        SettingsSection.Calendar => "calendar",
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown settings section."),
    };
}
