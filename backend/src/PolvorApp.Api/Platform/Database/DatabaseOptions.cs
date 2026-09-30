using Microsoft.Extensions.Options;
using Npgsql;

namespace PolvorApp.Api.Platform.Database;

/// <summary>PostgreSQL settings, read from <c>ConnectionStrings__Postgres</c> (NFR-14).</summary>
internal sealed class DatabaseOptions
{
    public const string ConnectionStringKey = "ConnectionStrings:Postgres";

    public string? ConnectionString { get; set; }
}

/// <summary>
/// Fails startup when the connection string is missing or malformed. Messages name the setting
/// but never include its value, which may contain credentials.
/// </summary>
internal sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail($"The {DatabaseOptions.ConnectionStringKey} setting is required.");
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString);
            return string.IsNullOrWhiteSpace(builder.Host)
                ? ValidateOptionsResult.Fail($"The {DatabaseOptions.ConnectionStringKey} setting has no Host.")
                : ValidateOptionsResult.Success;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return ValidateOptionsResult.Fail($"The {DatabaseOptions.ConnectionStringKey} setting is not a valid connection string.");
        }
    }
}
