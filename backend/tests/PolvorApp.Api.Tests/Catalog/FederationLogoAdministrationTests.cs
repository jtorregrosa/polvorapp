using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Spec "Federation logo" (design D11), the failure paths of the shared upload flow on the Federation's
/// row: a held lock answers busy and keeps no image, concurrent replacements leave one logo and no
/// orphan, and a failure before the commit erases the new image. Each test has its own bucket.
/// </summary>
public sealed class FederationLogoAdministrationTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio) : IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private IdentityTestHost _host = null!;
    private string _bucket = null!;

    public async ValueTask InitializeAsync()
    {
        _bucket = await minio.CreateBucketAsync();
        _host = await IdentityTestHost.StartAsync(postgres, mailpit, minio.SettingsFor(_bucket), ConfigureServices);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>An audit action that fails when recorded, which happens just before the save.</summary>
    private string? FailingAction { get; set; }

    [Fact]
    public async Task A_settings_row_locked_past_the_timeout_is_busy_and_keeps_no_image()
    {
        await UploadAsync(TestImages.Png(800, 400));
        var before = (await ReadAsync()).Logo!.Id;
        await using var holder = _host.Services.CreateAsyncScope();
        var db = holder.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        await db.LockFederationSettingsAsync(Token);

        // The row stays locked for longer than the 5 s lock timeout of the upload and the removal.
        var upload = await UploadAsync(TestImages.Png(900, 450));
        var removal = await RunAsync(administration => administration.RemoveAsync(Token));
        await transaction.RollbackAsync(Token);

        Assert.Equal((CatalogOutcome.Busy, CatalogOutcome.Busy), (upload.Outcome, removal));
        Assert.Equal(before, (await ReadAsync()).Logo?.Id);
        Assert.Equal([LogoStorage.KeyFor(before)], await KeysAsync());
    }

    [Fact]
    public async Task Two_replacements_at_once_leave_one_logo_and_no_orphan()
    {
        await UploadAsync(TestImages.Png(800, 400));

        var uploads = await Task.WhenAll(UploadAsync(TestImages.Png(900, 450)), UploadAsync(TestImages.Png(700, 350)));

        Assert.All(uploads, upload => Assert.Equal(CatalogOutcome.Done, upload.Outcome));
        var current = (await ReadAsync()).Logo;
        Assert.NotNull(current);
        Assert.Contains(current.Id, uploads.Select(u => u.Logo!.Id));
        Assert.Equal([LogoStorage.KeyFor(current.Id)], await KeysAsync());
    }

    [Fact]
    public async Task A_failure_before_the_commit_erases_the_new_image()
    {
        FailingAction = "FederationLogoUploaded";

        await Assert.ThrowsAsync<InvalidOperationException>(() => UploadAsync(TestImages.Png(800, 400)));

        Assert.Null((await ReadAsync()).Logo);
        Assert.Empty(await KeysAsync());
    }

    private void ConfigureServices(IServiceCollection services)
    {
        var original = services.Last(d => d.ServiceType == typeof(IAuditTrail));
        services.Remove(original);
        services.Add(new ServiceDescriptor(
            typeof(IAuditTrail),
            provider => new FailingAuditTrail(() => FailingAction, (IAuditTrail)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)),
            original.Lifetime));
    }

    private Task<LogoUpload> UploadAsync(byte[] image) =>
        RunAsync(administration => administration.UploadAsync(new MemoryStream(image), Token));

    private async Task<T> RunAsync<T>(Func<FederationLogoAdministration, Task<T>> action)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<FederationLogoAdministration>());
    }

    private async Task<FederationSettings> ReadAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>().FederationSettings.AsNoTracking().SingleAsync(Token);
    }

    private Task<IReadOnlyList<string>> KeysAsync() => minio.ListKeysAsync(_bucket, LogoStorage.Prefix);

    /// <summary>Throws when the failing action is recorded: the change fails before it is saved.</summary>
    private sealed class FailingAuditTrail(Func<string?> failingAction, IAuditTrail inner) : IAuditTrail
    {
        public void Record(DbContext context, AuditRecord record)
        {
            if (record.Action == failingAction())
            {
                throw new InvalidOperationException("Synthetic failure before the save.");
            }

            inner.Record(context, record);
        }
    }
}
