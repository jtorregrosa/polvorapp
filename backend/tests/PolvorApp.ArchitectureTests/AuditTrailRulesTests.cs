using System.Text.RegularExpressions;

namespace PolvorApp.ArchitectureTests;

/// <summary>
/// Spec audit-privacy "Audit trail is append-only": the save interceptor cannot see bulk updates,
/// bulk deletes or raw SQL, so source code must not use them on the audit trail. The only exception
/// is the audit module's maintenance class (retention purge and GDPR redaction, design D4), which is
/// also the only code that may name the database guard's maintenance setting. Only the audit
/// module's own migrations may create or change the guard. These rules help reviewers; the
/// database trigger is what refuses the changes.
/// </summary>
public sealed partial class AuditTrailRulesTests
{
    /// <summary>The one file allowed to change audit entries, relative to <c>src/</c>.</summary>
    private const string MaintenanceFile = "Modules/AuditPrivacy/PolvorApp.AuditPrivacy/Maintenance/AuditMaintenance.cs";

    /// <summary>The only migrations allowed to touch the audit table and its guard, relative to <c>src/</c>.</summary>
    private const string AuditMigrations = "Modules/AuditPrivacy/PolvorApp.AuditPrivacy/Persistence/Migrations/";

    [Fact]
    public void No_source_file_bulk_updates_deletes_or_writes_raw_sql_to_the_audit_trail()
    {
        var offenders = Offenders(ForbiddenAuditWrite());

        Assert.True(offenders.Count == 0, "Bulk or raw writes to the audit trail: " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_source_file_but_the_maintenance_class_names_the_maintenance_setting_or_tampers_with_the_guard()
    {
        var offenders = Offenders(GuardTampering());

        Assert.True(offenders.Count == 0, "Audit guard named or changed outside AuditMaintenance: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_maintenance_class_is_where_the_rules_allow_it() =>
        Assert.True(File.Exists(Path.Combine(SourceRoot(), MaintenanceFile)), MaintenanceFile);

    [Theory]
    [InlineData("await db.Set<AuditEntry>().Where(e => e.Id == id).ExecuteDeleteAsync(ct);")]
    [InlineData("db.Set<AuditEntry>().ExecuteUpdate(s => s.SetProperty(e => e.Action, \"x\"));")]
    [InlineData("await db.Database.ExecuteSqlRawAsync(\"DELETE FROM audit.audit_entries\");")]
    [InlineData("UPDATE audit.audit_entries SET action = 'x'")]
    [InlineData("UPDATE ONLY audit.audit_entries SET action = 'x'")]
    [InlineData("MERGE INTO audit.audit_entries AS a USING x ON true WHEN MATCHED THEN DELETE")]
    public void The_rule_detects_forbidden_writes(string source) =>
        Assert.Matches(ForbiddenAuditWrite(), source);

    [Theory]
    [InlineData("trail.Record(context, new AuditRecord(\"UserInvited\", \"User\"));")]
    [InlineData("await db.Set<AuditEntry>().Where(e => e.ActorUserId == id).ToListAsync(ct);")]
    public void The_rule_allows_recording_and_reading(string source) =>
        Assert.DoesNotMatch(ForbiddenAuditWrite(), source);

    [Theory]
    [InlineData("SELECT set_config('polvorapp.audit_maintenance', 'purge', true)")]
    [InlineData("private const string Setting = \"polvorapp.audit_maintenance\";")]
    [InlineData("ALTER TABLE audit.audit_entries DISABLE TRIGGER guard_audit_entries")]
    [InlineData("DROP TRIGGER refuse_audit_truncate ON audit.audit_entries")]
    [InlineData("SET session_replication_role = replica")]
    public void The_rule_detects_the_maintenance_setting_and_guard_tampering(string source) =>
        Assert.Matches(GuardTampering(), source);

    [Fact]
    public void The_rule_allows_other_settings() =>
        Assert.DoesNotMatch(GuardTampering(), "SELECT set_config('application_name', 'polvorapp', true)");

    /// <summary>Source files, outside the audit module's migrations and maintenance class, that match <paramref name="rule"/>.</summary>
    private static List<string> Offenders(Regex rule)
    {
        var root = SourceRoot();
        return [.. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(file => !file.StartsWith(AuditMigrations, StringComparison.Ordinal))
            .Where(file => !string.Equals(file, MaintenanceFile, StringComparison.Ordinal))
            .Where(file => rule.IsMatch(File.ReadAllText(Path.Combine(root, file))))];
    }

    private static string SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PolvorApp.slnx")))
            {
                return Path.Combine(directory.FullName, "src");
            }
        }

        throw new InvalidOperationException("PolvorApp.slnx not found above the test output directory.");
    }

    [GeneratedRegex(
        @"Set<AuditEntry>\(\)[^;]*\.Execute(Update|Delete)|ExecuteSql[^;]*audit_entries|(UPDATE(\s+ONLY)?|DELETE\s+FROM|TRUNCATE|MERGE\s+INTO)\s+(audit\.)?""?audit_entries",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenAuditWrite();

    [GeneratedRegex(
        @"polvorapp\.audit_maintenance|guard_audit_entries|refuse_audit_truncate|DISABLE\s+TRIGGER|session_replication_role",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GuardTampering();
}
