using System.Globalization;
using System.Net.Http.Headers;

namespace PolvorApp.Api.Tests.Registry.Import;

/// <summary>Multipart requests to the import endpoints, as the UI sends them.</summary>
internal static class ImportRequests
{
    public const string PreviewUri = "/api/arquebusiers/import/preview";
    public const string ImportUri = "/api/arquebusiers/import";

    public static Task<HttpResponseMessage> PreviewAsync(HttpClient client, Guid? comparsaId, byte[]? workbook) =>
        PostAsync(client, PreviewUri, comparsaId, workbook);

    public static Task<HttpResponseMessage> ImportAsync(HttpClient client, Guid? comparsaId, byte[]? workbook) =>
        PostAsync(client, ImportUri, comparsaId, workbook);

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string uri, Guid? comparsaId, byte[]? workbook)
    {
        using var content = new MultipartFormDataContent();
        if (comparsaId is { } id)
        {
            content.Add(new StringContent(id.ToString("D", CultureInfo.InvariantCulture)), "comparsaId");
        }

        if (workbook is not null)
        {
            var file = new ByteArrayContent(workbook);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            content.Add(file, "file", "arcabuceros.xlsx");
        }

        return await client.PostAsync(new Uri(uri, UriKind.Relative), content, TestContext.Current.CancellationToken);
    }
}
