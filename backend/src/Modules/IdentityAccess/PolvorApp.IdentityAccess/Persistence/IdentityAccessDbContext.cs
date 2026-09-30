using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Localization;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.IdentityAccess.Persistence;

/// <summary>
/// Schema <c>identity</c>: users and their Identity data (no role tables: a user has one role
/// column), and the Data Protection key ring so cookies survive restarts (design D2).
/// </summary>
internal sealed class IdentityAccessDbContext(DbContextOptions<IdentityAccessDbContext> options)
    : IdentityUserContext<User, Guid>(options), IDataProtectionKeyContext
{
    public const string Schema = "identity";

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);
        builder.AddAuditTrail();

        builder.Entity<User>(user =>
        {
            user.ToTable("users", table =>
            {
                table.HasCheckConstraint("ck_users_role", $"role IN ({SqlList([UserRoleCodes.Admin, UserRoleCodes.FiringChief])})");
                table.HasCheckConstraint("ck_users_locale", $"locale IN ({SqlList(SupportedLocales.All)})");
            });
            user.Property(u => u.Name).HasMaxLength(User.NameMaxLength);
            user.Property(u => u.Role).HasConversion(role => role.ToCode(), code => UserRoleCodes.Parse(code)).HasMaxLength(20);
            user.Property(u => u.Locale).HasMaxLength(20);
            user.Property(u => u.Email).IsRequired();
            user.Property(u => u.NormalizedEmail).IsRequired();
            user.Property(u => u.UserName).IsRequired();
            user.Property(u => u.NormalizedUserName).IsRequired();
            user.Ignore(u => u.Status);
            user.Ignore(u => u.PhoneNumber);
            user.Ignore(u => u.PhoneNumberConfirmed);

            // Blocking data-integrity rule (spec: Users and roles): one account per email.
            user.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_users_normalized_email").IsUnique();
            user.HasIndex(u => u.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name").IsUnique();
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
    }

    private static string SqlList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
internal sealed class IdentityAccessDbContextDesignTimeFactory : IDesignTimeDbContextFactory<IdentityAccessDbContext>
{
    public IdentityAccessDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IdentityAccessDbContext>()
            .UseModuleDatabase("Host=design-time-only", IdentityAccessDbContext.Schema)
            .Options);
}
