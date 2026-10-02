using System.Net;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Registry.Import;

/// <summary>Spec "Import template (UC-09)": Admins download it in their language; nothing is audited.</summary>
public sealed class ImportTemplateEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private const string TemplateUri = "/api/arquebusiers/import/template";
    private RegistryTestHost _registry = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _registry = await RegistryTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Theory]
    [InlineData("es-ES", "Arcabuceros", "Apellidos")]
    [InlineData("ca-ES-valencia", "Arcabussers", "Cognoms")]
    [InlineData("en", "Arquebusiers", "Last names")]
    public async Task An_Admin_downloads_the_template_in_their_language(string language, string sheet, string lastNameHeader)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, TemplateUri);
        request.Headers.AcceptLanguage.ParseAdd(language);

        using var response = await _registry.Admin.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Accept-Language", response.Headers.Vary);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("polvorapp-arquebusiers-template.xlsx", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync(Token));
        Assert.Equal(sheet, workbook.Worksheet(1).Name);
        Assert.Equal(lastNameHeader, workbook.Worksheet(1).Cell(1, 2).GetText());
        Assert.Equal(1, workbook.Worksheet(1).LastRowUsed(XLCellsUsedOptions.Contents)!.RowNumber());
    }

    [Fact]
    public async Task A_FiringChief_cannot_download_the_template()
    {
        using var response = await _registry.FiringChief.GetAsync(TemplateUri, Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_signed_out_request_gets_no_template()
    {
        using var anonymous = _registry.Host.Factory.CreateClient();

        using var response = await anonymous.GetAsync(TemplateUri, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Downloading_the_template_is_not_audited()
    {
        var before = await AuditCountAsync();

        using var response = await _registry.Admin.GetAsync(TemplateUri, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await AuditCountAsync());
    }

    private async Task<int> AuditCountAsync()
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Set<AuditEntry>().CountAsync(Token);
    }
}
