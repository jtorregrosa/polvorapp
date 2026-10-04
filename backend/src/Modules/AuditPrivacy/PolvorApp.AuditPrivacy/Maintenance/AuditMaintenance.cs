using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.AuditPrivacy.Maintenance;

/// <summary>
/// The only code allowed to change audit entries (spec: Audit trail is append-only; design D4). The
/// database trigger refuses every update and delete unless the transaction set the
/// <c>polvorapp.audit_maintenance</c> setting: <c>purge</c> lets the retention purge delete expired
/// entries, <c>redact</c> lets a GDPR erasure replace personal values in <c>data</c> and nothing
/// else. The setting is transaction-local, so every method runs on an explicit transaction.
/// An architecture test allows audit writes and the setting's name in this file only.
/// </summary>
internal static class AuditMaintenance
{
    /// <summary>The SQLSTATE the guard triggers raise.</summary>
    public const string GuardSqlState = "PA001";

    private const string Setting = "polvorapp.audit_maintenance";

    // Two fixed statements rather than one with an interpolated condition. The batch takes no defined
    // order (the purge only needs to remove every expired row) and skips rows another purge holds.
    private const string DeleteMatching =
        """
        DELETE FROM audit.audit_entries WHERE id IN (
            SELECT id FROM audit.audit_entries
            WHERE action = ANY($1) AND occurred_at < $2
            LIMIT $3
            FOR UPDATE SKIP LOCKED)
        """;

    private const string DeleteOthers =
        """
        DELETE FROM audit.audit_entries WHERE id IN (
            SELECT id FROM audit.audit_entries
            WHERE NOT (action = ANY($1)) AND occurred_at < $2
            LIMIT $3
            FOR UPDATE SKIP LOCKED)
        """;

    /// <summary>
    /// Deletes up to <paramref name="limit"/> entries of <paramref name="retention"/> older than
    /// <paramref name="cutoff"/>: the <paramref name="securityActions"/> for the security class, every
    /// other action for the standard class. Returns how many were deleted.
    /// </summary>
    public static async Task<int> DeleteExpiredAsync(
        DbTransaction transaction,
        IReadOnlyCollection<string> securityActions,
        AuditRetentionClass retention,
        DateTimeOffset cutoff,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(securityActions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await EnterAsync(transaction, "purge", cancellationToken);
        await using var command = Command(
            transaction,
            retention == AuditRetentionClass.Security ? DeleteMatching : DeleteOthers,
            new NpgsqlParameter { Value = securityActions.ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text },
            new NpgsqlParameter { Value = cutoff.UtcDateTime, NpgsqlDbType = NpgsqlDbType.TimestampTz },
            new NpgsqlParameter { Value = limit });
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        await LeaveAsync(transaction, cancellationToken);
        return deleted;
    }

    /// <summary>
    /// Replaces <paramref name="property"/> of the <c>data</c> of the entries with
    /// <paramref name="action"/> whose value equals <paramref name="value"/>, by <c>"[erased]"</c>.
    /// Returns how many entries were redacted.
    /// </summary>
    public static async Task<int> RedactAsync(
        DbTransaction transaction, string action, string property, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        ArgumentNullException.ThrowIfNull(value);
        await EnterAsync(transaction, "redact", cancellationToken);
        await using var command = Command(
            transaction,
            """
            UPDATE audit.audit_entries
            SET data = jsonb_set(data, ARRAY[$2], '"[erased]"'::jsonb)
            WHERE action = $1 AND data->>$2 = $3
            """,
            new NpgsqlParameter { Value = action },
            new NpgsqlParameter { Value = property },
            new NpgsqlParameter { Value = value });
        var redacted = await command.ExecuteNonQueryAsync(cancellationToken);
        await LeaveAsync(transaction, cancellationToken);
        return redacted;
    }

    private static Task EnterAsync(DbTransaction transaction, string mode, CancellationToken cancellationToken) =>
        SetModeAsync(transaction, mode, cancellationToken);

    /// <summary>
    /// Ends the mode as soon as the statement is done: an erasure runs other modules' SQL in the same
    /// transaction, which must not inherit it. A failed statement aborts the transaction, which ends it too.
    /// </summary>
    private static Task LeaveAsync(DbTransaction transaction, CancellationToken cancellationToken) =>
        SetModeAsync(transaction, string.Empty, cancellationToken);

    private static async Task SetModeAsync(DbTransaction transaction, string mode, CancellationToken cancellationToken)
    {
        await using var command = Command(
            transaction, "SELECT set_config($1, $2, true)", new NpgsqlParameter { Value = Setting }, new NpgsqlParameter { Value = mode });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand Command(DbTransaction transaction, string sql, params NpgsqlParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var command = (transaction.Connection ?? throw new InvalidOperationException("The transaction has no connection.")).CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return command;
    }
}
