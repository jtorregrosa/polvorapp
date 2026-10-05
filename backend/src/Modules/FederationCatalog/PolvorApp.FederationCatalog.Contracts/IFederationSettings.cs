namespace PolvorApp.FederationCatalog.Contracts;

/// <summary>Which form of the Federation's official name a document prints.</summary>
public enum FederationNameForm
{
    /// <summary>Spanish, also used by English documents: the name is a proper name.</summary>
    Spanish,

    /// <summary>Valencian.</summary>
    Valencian,
}

/// <summary>
/// The Federation settings as other modules read them (spec: Federation settings;
/// add-federation-settings, design D3). No secrets or personal data.
/// </summary>
/// <param name="OfficialNameEs">The official name in Spanish.</param>
/// <param name="OfficialNameCa">The official name in Valencian.</param>
/// <param name="ShortName">The short name for the interface and the end of emails.</param>
/// <param name="ContactEmail">The Federation's public contact address, when set.</param>
/// <param name="Website">The Federation's public website, when set.</param>
/// <param name="SenderName">The name shown with the deployment's sender address.</param>
/// <param name="ReplyTo">Where replies go, when set.</param>
/// <param name="CloseReminderLeadDays">Days before the planned close the first close reminder is sent (2–14).</param>
/// <param name="MilestoneLeadDays">Days before a milestone its reminder is sent (1–14).</param>
public sealed record FederationSettingsSnapshot(
    string OfficialNameEs,
    string OfficialNameCa,
    string ShortName,
    string? ContactEmail,
    string? Website,
    string SenderName,
    string? ReplyTo,
    int CloseReminderLeadDays,
    int MilestoneLeadDays)
{
    /// <summary>The official name in the form a document prints.</summary>
    public string OfficialName(FederationNameForm form) => form == FederationNameForm.Valencian ? OfficialNameCa : OfficialNameEs;
}

/// <summary>Reads the Federation settings; one read per scope (a request or a notification run).</summary>
public interface IFederationSettings
{
    Task<FederationSettingsSnapshot> GetAsync(CancellationToken cancellationToken);
}
