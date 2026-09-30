using System.Net;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests;

[Collection(PostgresGroup.Name)]
public sealed class OpenApiDocumentTests(PostgresFixture postgres)
{
    private static readonly Uri DocumentUri = new("/api/openapi/v1.json", UriKind.Relative);

    [Fact]
    public async Task Development_serves_the_openapi_document_with_the_system_info_operation()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(DocumentUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("PolvorApp API", document.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal(
            "GetSystemInfo",
            document.RootElement.GetProperty("paths").GetProperty("/api/system/info").GetProperty("get").GetProperty("operationId").GetString());
    }

    [Fact]
    public async Task Production_does_not_serve_the_openapi_document()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, "Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(DocumentUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
