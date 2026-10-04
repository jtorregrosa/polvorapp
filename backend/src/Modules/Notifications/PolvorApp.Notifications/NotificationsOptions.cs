using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace PolvorApp.Notifications;

/// <summary>Settings from <c>Notifications__*</c> environment variables (design D5).</summary>
internal sealed class NotificationsOptions
{
    public const string Section = "Notifications";
    public const int MaxDispatchIntervalSeconds = 3600;

    /// <summary>Runs the dispatcher and the scheduler; test hosts turn them off and call their runs directly.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the dispatcher expands events and sends the due deliveries.</summary>
    public int DispatchIntervalSeconds { get; set; } = 30;

    /// <summary>Settings present in configuration that could not be parsed; reported by name only.</summary>
    public IReadOnlyList<string> UnparsableSettings { get; set; } = [];

    /// <summary>Parses the settings here, so an invalid value never reaches an exception message.</summary>
    public static void Bind(NotificationsOptions options, IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        var unparsable = new List<string>();

        if (section["Enabled"] is { } enabled)
        {
            if (bool.TryParse(enabled, out var parsed))
            {
                options.Enabled = parsed;
            }
            else
            {
                unparsable.Add($"{Section}:Enabled");
            }
        }

        if (section["DispatchIntervalSeconds"] is { } interval)
        {
            if (int.TryParse(interval, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                options.DispatchIntervalSeconds = parsed;
            }
            else
            {
                unparsable.Add($"{Section}:DispatchIntervalSeconds");
            }
        }

        options.UnparsableSettings = unparsable;
    }
}

/// <summary>Fails startup naming the setting, never its value.</summary>
internal sealed class NotificationsOptionsValidator : IValidateOptions<NotificationsOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationsOptions options)
    {
        var failures = options.UnparsableSettings.Select(setting => $"The {setting} setting is not valid.").ToList();
        if (options.DispatchIntervalSeconds is < 1 or > NotificationsOptions.MaxDispatchIntervalSeconds)
        {
            failures.Add($"The {NotificationsOptions.Section}:DispatchIntervalSeconds setting must be between 1 and {NotificationsOptions.MaxDispatchIntervalSeconds}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
