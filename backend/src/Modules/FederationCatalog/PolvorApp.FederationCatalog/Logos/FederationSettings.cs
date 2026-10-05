namespace PolvorApp.FederationCatalog.Logos;

/// <summary>
/// The Federation's own settings (add-distribution-planning, design D11; add-federation-settings,
/// design D1): a single row created by the migration, holding the identity printed in documents and
/// emails, the email sender name, the reminder lead times and the logo. The logo is uploaded at run
/// time and never committed: the repository is public and the crest is not PolvorApp's to license.
/// No secret or personal data is ever stored here (spec: Federation settings).
/// </summary>
internal sealed class FederationSettings
{
    /// <summary>The only row's id; a check constraint refuses any other.</summary>
    public const int SingletonId = 1;

    public const int OfficialNameMaxLength = 150;
    public const int ShortNameMaxLength = 40;
    public const int SenderNameMaxLength = 80;
    public const int EmailMaxLength = 254;
    public const int WebsiteMaxLength = 200;
    public const int MinCloseReminderLeadDays = 2;
    public const int MaxCloseReminderLeadDays = 14;
    public const int MinMilestoneLeadDays = 1;
    public const int MaxMilestoneLeadDays = 14;

    public required int Id { get; init; }

    /// <summary>The official name in Spanish, printed in Spanish and English documents.</summary>
    public required string OfficialNameEs { get; set; }

    /// <summary>The official name in Valencian, printed in Valencian documents.</summary>
    public required string OfficialNameCa { get; set; }

    /// <summary>The short name shown in the interface and at the end of emails.</summary>
    public required string ShortName { get; set; }

    /// <summary>The Federation's public contact address, shown at the end of notification emails.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>The Federation's public website (https), shown at the end of notification emails.</summary>
    public string? Website { get; set; }

    /// <summary>The name shown with the deployment's sender address.</summary>
    public required string SenderName { get; set; }

    /// <summary>Where replies to PolvorApp's emails go, when set.</summary>
    public string? ReplyTo { get; set; }

    /// <summary>Days before the planned close the first close reminder is sent (BR-10).</summary>
    public required int CloseReminderLeadDays { get; set; }

    /// <summary>Days before a milestone its reminder is sent.</summary>
    public required int MilestoneLeadDays { get; set; }

    /// <summary>The logo, stored as the comparsa logos are (same rules, prefix and sweep).</summary>
    public ComparsaLogo? Logo { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>PostgreSQL <c>xmin</c>: a save based on an older version is rejected (<c>federationSettings.modified</c>).</summary>
    public uint Version { get; set; }
}
