using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Spec "Comparsa logos" (design D4): an upload stores the image under a new key, commits the
/// reference and only then erases the replaced image; every failure leaves at most an unreferenced
/// image, erased at once when the reference was surely not committed. Each test has its own bucket,
/// so it can assert exactly which images exist.
/// </summary>
public sealed class ComparsaLogoAdministrationTests(PostgresFixture postgres, MailpitFixture mailpit, MinioFixture minio) : IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private IdentityTestHost _host = null!;
    private string _bucket = null!;
    private Comparsa _comparsa = null!;

    public async ValueTask InitializeAsync()
    {
        _bucket = await minio.CreateBucketAsync();
        _host = await IdentityTestHost.StartAsync(postgres, mailpit, minio.SettingsFor(_bucket), ConfigureServices);
        _comparsa = RegistryData.NewComparsa("Comparsa Sintética Emblema");
        await _host.Services.SaveCatalogAsync(_comparsa);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>An audit action that fails when recorded, which happens just before the save.</summary>
    private string? FailingAction { get; set; }

    [Fact]
    public async Task An_upload_stores_the_logo_without_touching_the_comparsa_data()
    {
        var upload = await UploadAsync(TestImages.Png(800, 400, transparent: true));

        Assert.Equal(CatalogOutcome.Done, upload.Outcome);
        var stored = await ReadAsync();
        Assert.Equal(
            (_comparsa.Name, _comparsa.Side, _comparsa.Active, upload.Logo!.Id, 800, 400),
            (stored.Name, stored.Side, stored.Active, stored.Logo?.Id, stored.Logo?.Width, stored.Logo?.Height));
        Assert.Equal([LogoStorage.KeyFor(upload.Logo.Id)], await KeysAsync());
    }

    [Fact]
    public async Task A_replacement_erases_the_previous_image()
    {
        var first = await UploadAsync(TestImages.Png(800, 400));

        var second = await UploadAsync(TestImages.Jpeg(600, 600));

        Assert.Equal(CatalogOutcome.Done, second.Outcome);
        Assert.NotEqual(first.Logo!.Id, second.Logo!.Id);
        Assert.Equal(second.Logo.Id, (await ReadAsync()).Logo?.Id);
        Assert.Equal([LogoStorage.KeyFor(second.Logo.Id)], await KeysAsync());
    }

    [Fact]
    public async Task A_removal_clears_the_logo_and_erases_its_image()
    {
        await UploadAsync(TestImages.Png(800, 400));

        var outcome = await RunAsync(administration => administration.RemoveAsync(_comparsa.Id, Token));

        Assert.Equal(CatalogOutcome.Done, outcome);
        Assert.Null((await ReadAsync()).Logo);
        Assert.Empty(await KeysAsync());
    }

    [Fact]
    public async Task Removing_a_missing_logo_is_not_found()
    {
        var outcome = await RunAsync(administration => administration.RemoveAsync(_comparsa.Id, Token));

        Assert.Equal(CatalogOutcome.LogoNotFound, outcome);
    }

    [Fact]
    public async Task An_unknown_comparsa_does_not_exist_and_keeps_no_image()
    {
        var unknown = Guid.CreateVersion7();

        var exists = await RunAsync(administration => administration.ExistsAsync(unknown, Token));
        var upload = await RunAsync(administration => administration.UploadAsync(unknown, new MemoryStream(TestImages.Png(800, 400)), Token));

        // The upload re-checks under the row lock: a comparsa deleted after the first check is not found either.
        Assert.False(exists);
        Assert.Equal(CatalogOutcome.ComparsaNotFound, upload.Outcome);
        Assert.Empty(await KeysAsync());
    }

    [Fact]
    public async Task A_rejected_image_stores_nothing()
    {
        var upload = await UploadAsync(TestImages.Png(200, 200));

        Assert.Equal((CatalogOutcome.ImageRejected, PolvorApp.SharedKernel.Images.ImageRejection.TooSmall), (upload.Outcome, upload.Rejection));
        Assert.Null((await ReadAsync()).Logo);
        Assert.Empty(await KeysAsync());
    }

    [Fact]
    public async Task A_failure_before_the_commit_erases_the_new_image()
    {
        FailingAction = "ComparsaLogoUploaded";

        await Assert.ThrowsAsync<InvalidOperationException>(() => UploadAsync(TestImages.Png(800, 400)));

        Assert.Null((await ReadAsync()).Logo);
        Assert.Empty(await KeysAsync());
    }

    [Fact]
    public async Task A_comparsa_locked_past_the_timeout_is_busy_and_keeps_no_image()
    {
        await using var holder = _host.Services.CreateAsyncScope();
        var db = holder.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        Assert.NotNull(await db.LockComparsaForDeleteAsync(_comparsa.Id, Token));

        // The row stays locked for longer than the 5 s lock timeout of the upload.
        var upload = await UploadAsync(TestImages.Png(800, 400));
        var removal = await RunAsync(administration => administration.RemoveAsync(_comparsa.Id, Token));
        await transaction.RollbackAsync(Token);

        Assert.Equal((CatalogOutcome.Busy, CatalogOutcome.Busy), (upload.Outcome, removal));
        Assert.Null((await ReadAsync()).Logo);
        Assert.Empty(await KeysAsync());
    }

    [Fact]
    public async Task A_name_edit_and_a_logo_upload_one_after_the_other_keep_both_changes()
    {
        // Usually the edit commits while the upload is still normalising; the deterministic race is in
        // LogoDatabaseTests (both loaded before either saves).
        var edit = EditNameAsync("Comparsa Sintética Renombrada");
        var upload = UploadAsync(TestImages.Png(800, 400));

        await Task.WhenAll(edit, upload);

        var stored = await ReadAsync();
        Assert.Equal(("Comparsa Sintética Renombrada", (await upload).Logo?.Id), (stored.Name, stored.Logo?.Id));
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
        RunAsync(administration => administration.UploadAsync(_comparsa.Id, new MemoryStream(image), Token));

    private async Task<T> RunAsync<T>(Func<ComparsaLogoAdministration, Task<T>> action)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ComparsaLogoAdministration>());
    }

    private async Task EditNameAsync(string name)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var (outcome, _) = await scope.ServiceProvider.GetRequiredService<ComparsaAdministration>()
            .UpdateAsync(_comparsa.Id, new ComparsaInput(name, _comparsa.Side), Token);
        Assert.Equal(CatalogOutcome.Done, outcome);
    }

    private async Task<Comparsa> ReadAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>().Comparsas.AsNoTracking()
            .SingleAsync(c => c.Id == _comparsa.Id, Token);
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
