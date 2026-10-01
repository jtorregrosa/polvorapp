using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PolvorApp.SharedKernel.Hosting;

namespace PolvorApp.Api.Platform.Storage;

/// <summary>Server-side encryption requested on every write, for providers that need it per request.</summary>
internal enum StorageEncryption
{
    /// <summary>Rely on the bucket's default encryption (S3 default encryption, or MinIO with KMS).</summary>
    None,

    /// <summary>Ask for SSE-S3 (<c>AES256</c>) on every put.</summary>
    Aes256,
}

/// <summary>S3-compatible storage settings from <c>Storage__*</c> environment variables (ADR-0005, design D1).</summary>
internal sealed class StorageOptions
{
    public const string Section = "Storage";
    public const int MaxSweepIntervalMinutes = 24 * 60;

    public Uri? ServiceUrl { get; set; }

    public string? Bucket { get; set; }

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>Used to sign requests only; S3-compatible providers usually accept the default.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>Bucket in the path (<c>host/bucket/key</c>), as MinIO and most providers expect.</summary>
    public bool ForcePathStyle { get; set; } = true;

    public StorageEncryption ServerSideEncryption { get; set; } = StorageEncryption.None;

    /// <summary>
    /// Whether the orphan sweep runs (design D2). Turn it off while restoring a database or a bucket
    /// from a backup, so the sweep never compares a database with a bucket that is not its own.
    /// </summary>
    public bool SweepEnabled { get; set; } = true;

    /// <summary>How often the orphan sweep runs (design D2).</summary>
    public int SweepIntervalMinutes { get; set; } = 60;

    /// <summary>How long <c>migrate</c> waits for the storage to come up before failing.</summary>
    public int BootstrapMaxWaitSeconds { get; set; } = 30;

    /// <summary>Settings present in configuration that could not be parsed; reported by name only.</summary>
    public IReadOnlyList<string> UnparsableSettings { get; set; } = [];

    /// <summary>
    /// Reads the settings as strings and parses them here, so an invalid value never reaches an
    /// exception message (the configuration binder would include it).
    /// </summary>
    public static void Bind(StorageOptions options, IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        var unparsable = new List<string>();
        options.Bucket = section["Bucket"];
        options.AccessKey = section["AccessKey"];
        options.SecretKey = section["SecretKey"];

        if (section["ServiceUrl"] is { } url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            {
                options.ServiceUrl = parsed;
            }
            else
            {
                unparsable.Add($"{Section}:ServiceUrl");
            }
        }

        if (section["Region"] is { Length: > 0 } region)
        {
            options.Region = region;
        }

        if (section["ForcePathStyle"] is { } pathStyle)
        {
            if (bool.TryParse(pathStyle, out var parsed))
            {
                options.ForcePathStyle = parsed;
            }
            else
            {
                unparsable.Add($"{Section}:ForcePathStyle");
            }
        }

        if (section["SweepEnabled"] is { } sweep)
        {
            if (bool.TryParse(sweep, out var parsed))
            {
                options.SweepEnabled = parsed;
            }
            else
            {
                unparsable.Add($"{Section}:SweepEnabled");
            }
        }

        if (section["ServerSideEncryption"] is { } encryption)
        {
            // Names only: Enum.TryParse would also accept numbers such as "1".
            var match = Enum.GetValues<StorageEncryption>()
                .Where(value => string.Equals(value.ToString(), encryption, StringComparison.OrdinalIgnoreCase))
                .Select(value => (StorageEncryption?)value)
                .FirstOrDefault();
            if (match is { } parsed)
            {
                options.ServerSideEncryption = parsed;
            }
            else
            {
                unparsable.Add($"{Section}:ServerSideEncryption");
            }
        }

        if (section["SweepIntervalMinutes"] is { } interval)
        {
            if (int.TryParse(interval, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                options.SweepIntervalMinutes = parsed;
            }
            else
            {
                unparsable.Add($"{Section}:SweepIntervalMinutes");
            }
        }

        if (section["BootstrapMaxWaitSeconds"] is { } wait)
        {
            if (int.TryParse(wait, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                options.BootstrapMaxWaitSeconds = parsed;
            }
            else
            {
                unparsable.Add($"{Section}:BootstrapMaxWaitSeconds");
            }
        }

        options.UnparsableSettings = unparsable;
    }
}

/// <summary>Fails startup naming the setting, never its value (spec: Private object storage).</summary>
internal sealed partial class StorageOptionsValidator(IHostEnvironment environment) : IValidateOptions<StorageOptions>
{
    private const int MaxBootstrapWaitSeconds = 600;

    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        var failures = new List<string>();
        failures.AddRange(options.UnparsableSettings.Select(setting => $"The {setting} setting is not valid."));

        if (options.ServiceUrl is { } url)
        {
            // Plain http only locally, or to a storage on the same machine (the traffic never leaves it).
            var secure = url.Scheme == Uri.UriSchemeHttps
                || (url.Scheme == Uri.UriSchemeHttp && (LocalEnvironments.IsLocal(environment) || url.IsLoopback));
            if (!secure || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0)
            {
                failures.Add("The Storage:ServiceUrl setting must be an absolute https URL (http only locally or on loopback) without credentials, query or fragment.");
            }
        }
        else if (!options.UnparsableSettings.Contains("Storage:ServiceUrl"))
        {
            failures.Add("The Storage:ServiceUrl setting is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Bucket))
        {
            failures.Add("The Storage:Bucket setting is required.");
        }
        else if (!BucketName().IsMatch(options.Bucket))
        {
            failures.Add("The Storage:Bucket setting is not a valid bucket name (3-63 lower-case letters, digits, dots or hyphens).");
        }

        if (string.IsNullOrWhiteSpace(options.AccessKey))
        {
            failures.Add("The Storage:AccessKey setting is required.");
        }

        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            failures.Add("The Storage:SecretKey setting is required.");
        }

        if (options.SweepIntervalMinutes is < 1 or > StorageOptions.MaxSweepIntervalMinutes)
        {
            failures.Add("The Storage:SweepIntervalMinutes setting must be at least one minute and at most one day.");
        }

        if (options.BootstrapMaxWaitSeconds is < 1 or > MaxBootstrapWaitSeconds)
        {
            failures.Add("The Storage:BootstrapMaxWaitSeconds setting must be at least one second and at most ten minutes.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>S3 bucket naming rules: 3–63 characters, starting and ending with a letter or digit.</summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex BucketName();
}
