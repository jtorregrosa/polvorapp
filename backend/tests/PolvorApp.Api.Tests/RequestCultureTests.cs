using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests;

[Collection(PostgresGroup.Name)]
public sealed class RequestCultureTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("ca-ES-valencia", "No s'ha trobat el recurs")]
    [InlineData("en", "Resource not found")]
    [InlineData("ca", "No s'ha trobat el recurs")]
    [InlineData("ca-ES", "No s'ha trobat el recurs")]
    [InlineData("en-GB,en;q=0.9", "Resource not found")]
    [InlineData("fr-FR, en;q=0.8, es;q=0.5", "Resource not found")]
    [InlineData("es-MX;q=0.4, ca-ES;q=0.9", "No s'ha trobat el recurs")]
    [InlineData("es-ES", "Recurso no encontrado")]
    [InlineData("de-DE", "Recurso no encontrado")]
    [InlineData(null, "Recurso no encontrado")]
    public async Task Problem_title_follows_the_accept_language_header(string? acceptLanguage, string expectedTitle)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/does-not-exist");
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(expectedTitle, body.RootElement.GetProperty("title").GetString());
    }
}
