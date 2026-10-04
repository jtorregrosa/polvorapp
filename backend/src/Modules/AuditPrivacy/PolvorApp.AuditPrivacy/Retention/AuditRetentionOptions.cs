using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace PolvorApp.AuditPrivacy.Retention;

/// <summary>Settings from <c>Audit__*</c> environment variables (spec: Audit retention; design D3).</summary>
internal sealed class AuditRetentionOptions
{
    public const string Section = "Audit";
    public const int MinRetentionYears = 5;
    public const int MaxRetentionYears = 100;
    public const int MinSecurityRetentionDays = 365;
    public const int MaxSecurityRetentionDays = 36500;

    /// <summary>Runs the daily purge; test hosts turn it off and call the purge directly.</summary>
    public bool PurgeEnabled { get; set; } = true;

    /// <summary>How long changes, exports and GDPR requests are kept.</summary>
    public int RetentionYears { get; set; } = MinRetentionYears;

    /// <summary>How long access and security events are kept.</summary>
    public int SecurityRetentionDays { get; set; } = MinSecurityRetentionDays;

    /// <summary>Settings present in configuration that could not be parsed; reported by name only.</summary>
    public IReadOnlyList<string> UnparsableSettings { get; set; } = [];

    /// <summary>Parses the settings here, so an invalid value never reaches an exception message.</summary>
    public static void Bind(AuditRetentionOptions options, IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        var unparsable = new List<string>();

        if (section[nameof(PurgeEnabled)] is { } enabled)
        {
            if (bool.TryParse(enabled, out var parsed))
            {
                options.PurgeEnabled = parsed;
            }
            else
            {
                unparsable.Add($"{Section}:{nameof(PurgeEnabled)}");
            }
        }

        options.RetentionYears = ParseWhole(section, nameof(RetentionYears), options.RetentionYears, unparsable);
        options.SecurityRetentionDays = ParseWhole(section, nameof(SecurityRetentionDays), options.SecurityRetentionDays, unparsable);
        options.UnparsableSettings = unparsable;
    }

    private static int ParseWhole(IConfigurationSection section, string key, int fallback, List<string> unparsable)
    {
        if (section[key] is not { } value)
        {
            return fallback;
        }

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        unparsable.Add($"{Section}:{key}");
        return fallback;
    }
}

/// <summary>Fails start-up naming the setting, never its value.</summary>
internal sealed class AuditRetentionOptionsValidator : IValidateOptions<AuditRetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, AuditRetentionOptions options)
    {
        var failures = options.UnparsableSettings.Select(setting => $"The {setting} setting is not valid.").ToList();
        // Bounded both ways: below the minimum breaks the stated retention, above the maximum the
        // purge's cut-off date could not be computed and every run would fail.
        if (options.RetentionYears is < AuditRetentionOptions.MinRetentionYears or > AuditRetentionOptions.MaxRetentionYears)
        {
            failures.Add(
                $"The {AuditRetentionOptions.Section}:RetentionYears setting must be between {AuditRetentionOptions.MinRetentionYears} and {AuditRetentionOptions.MaxRetentionYears}.");
        }

        if (options.SecurityRetentionDays is < AuditRetentionOptions.MinSecurityRetentionDays or > AuditRetentionOptions.MaxSecurityRetentionDays)
        {
            failures.Add(
                $"The {AuditRetentionOptions.Section}:SecurityRetentionDays setting must be between {AuditRetentionOptions.MinSecurityRetentionDays} and {AuditRetentionOptions.MaxSecurityRetentionDays}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
