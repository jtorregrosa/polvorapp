using System.Text.RegularExpressions;

namespace PolvorApp.ArchitectureTests;

/// <summary>
/// Spec audit-privacy "Audit trail is append-only": the save interceptor cannot see bulk updates,
/// bulk deletes or raw SQL, so source code must not use them on the audit trail.
/// </summary>
public sealed partial class AuditTrailRulesTests
{
    [Fact]
    public void No_source_file_bulk_updates_deletes_or_writes_raw_sql_to_the_audit_trail()
    {
        var offenders = SourceFiles()
            .Where(file => !file.Replace('\\', '/').Contains("/Migrations/", StringComparison.Ordinal))
            .Where(file => ForbiddenAuditWrite().IsMatch(File.ReadAllText(file)))
            .ToList();

        Assert.True(offenders.Count == 0, "Bulk or raw writes to the audit trail: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("await db.Set<AuditEntry>().Where(e => e.Id == id).ExecuteDeleteAsync(ct);")]
    [InlineData("db.Set<AuditEntry>().ExecuteUpdate(s => s.SetProperty(e => e.Action, \"x\"));")]
    [InlineData("await db.Database.ExecuteSqlRawAsync(\"DELETE FROM audit.audit_entries\");")]
    [InlineData("UPDATE audit.audit_entries SET action = 'x'")]
    public void The_rule_detects_forbidden_writes(string source) =>
        Assert.Matches(ForbiddenAuditWrite(), source);

    [Theory]
    [InlineData("trail.Record(context, new AuditRecord(\"UserInvited\", \"User\"));")]
    [InlineData("await db.Set<AuditEntry>().Where(e => e.ActorUserId == id).ToListAsync(ct);")]
    public void The_rule_allows_recording_and_reading(string source) =>
        Assert.DoesNotMatch(ForbiddenAuditWrite(), source);

    private static IEnumerable<string> SourceFiles()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PolvorApp.slnx")))
            {
                return Directory.EnumerateFiles(Path.Combine(directory.FullName, "src"), "*.cs", SearchOption.AllDirectories);
            }
        }

        throw new InvalidOperationException("PolvorApp.slnx not found above the test output directory.");
    }

    [GeneratedRegex(
        @"Set<AuditEntry>\(\)[^;]*\.Execute(Update|Delete)|ExecuteSql[^;]*audit_entries|(UPDATE|DELETE\s+FROM|TRUNCATE)\s+(audit\.)?""?audit_entries",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenAuditWrite();
}
