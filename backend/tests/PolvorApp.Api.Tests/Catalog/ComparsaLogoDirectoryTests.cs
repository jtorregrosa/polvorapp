using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Spec "Logo access (BR-12)": other capabilities read a comparsa's logo on the server through the
/// catalogue contract, without a user scope (design D6), for example to print it in a document.
/// </summary>
public sealed class ComparsaLogoDirectoryTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await RegistryTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Another_module_reads_the_logo_as_png_bytes()
    {
        using (var upload = await LogoRequests.UploadAsync(_host.Admin, _host.Other.Id, TestImages.Png(3000, 1500)))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        var logo = await ReadAsync(_host.Other.Id);

        Assert.NotNull(logo);
        Assert.Equal((1024, 512), (logo.Width, logo.Height));
        Assert.True(logo.Png.Span.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G']));
        Assert.Equal((1024, 512), DecodedSize(logo.Png));
    }

    [Fact]
    public async Task A_comparsa_without_a_logo_or_an_unknown_one_has_none()
    {
        Assert.Null(await ReadAsync(_host.Own.Id));
        Assert.Null(await ReadAsync(Guid.CreateVersion7()));
    }

    private static (int Width, int Height) DecodedSize(ReadOnlyMemory<byte> png)
    {
        using var bitmap = SkiaSharp.SKBitmap.Decode(png.Span);
        return (bitmap.Width, bitmap.Height);
    }

    private async Task<LogoImage?> ReadAsync(Guid comparsaId)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICatalogDirectory>().ReadComparsaLogoAsync(comparsaId, TestContext.Current.CancellationToken);
    }
}
