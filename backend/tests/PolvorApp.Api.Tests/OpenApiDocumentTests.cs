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

    [Fact]
    public async Task Integers_are_typed_as_integers_only()
    {
        using var document = await DocumentAsync();

        var recoveryCodesLeft = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("AccountResponse").GetProperty("properties").GetProperty("recoveryCodesLeft");

        Assert.Equal("integer", recoveryCodesLeft.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Enum_schemas_list_only_their_codes_and_nullability_stays_on_the_property()
    {
        using var document = await DocumentAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        var enumsWithNull = schemas.EnumerateObject()
            .Where(s => s.Value.TryGetProperty("enum", out var values) && values.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.Null))
            .Select(s => s.Name);

        Assert.Empty(enumsWithNull);
        Assert.Equal(["RIGHT", "LEFT"], schemas.GetProperty("Handedness").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        var handedness = schemas.GetProperty("WeaponModelResponse").GetProperty("properties").GetProperty("handedness");
        Assert.Contains(handedness.GetProperty("oneOf").EnumerateArray(), o => o.TryGetProperty("type", out var type) && type.GetString() == "null");
    }

    /// <summary>Spec "Authenticated API by default": the anonymous surface is exactly this list.</summary>
    [Fact]
    public async Task Only_the_declared_operations_are_anonymous()
    {
        using var document = await DocumentAsync();

        var anonymous = Operations(document)
            .Where(o => !o.Operation.TryGetProperty("security", out var security) || security.GetArrayLength() == 0)
            .Select(o => o.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "GET /api/auth/antiforgery", "GET /api/auth/enrolment", "GET /api/auth/invitations/validate", "GET /api/system/info",
                "POST /api/auth/enrolment", "POST /api/auth/invitations/accept", "POST /api/auth/login", "POST /api/auth/login/recovery-code",
                "POST /api/auth/login/second-factor", "POST /api/auth/logout", "POST /api/auth/password/forgot", "POST /api/auth/password/reset",
            ],
            anonymous);
    }

    [Fact]
    public async Task Every_state_changing_operation_documents_the_antiforgery_header()
    {
        using var document = await DocumentAsync();

        var missing = Operations(document)
            .Where(o => !o.Name.StartsWith("GET ", StringComparison.Ordinal))
            .Where(o => !o.Operation.TryGetProperty("parameters", out var parameters)
                || !parameters.EnumerateArray().Any(p => p.GetProperty("name").GetString() == "X-XSRF-TOKEN"))
            .Select(o => o.Name)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>Spec audit-privacy "Audit trail is append-only": no endpoint updates or deletes audit entries.</summary>
    [Fact]
    public async Task No_operation_modifies_the_audit_trail()
    {
        using var document = await DocumentAsync();

        Assert.DoesNotContain(Operations(document), o => o.Name.Contains("audit", StringComparison.OrdinalIgnoreCase) && !o.Name.StartsWith("GET ", StringComparison.Ordinal));
    }

    /// <summary>Spec "User management by Admins": users are deactivated, never deleted.</summary>
    [Fact]
    public async Task No_operation_deletes_users()
    {
        using var document = await DocumentAsync();

        Assert.DoesNotContain(Operations(document), o => o.Name.StartsWith("DELETE /api/users", StringComparison.Ordinal));
    }

    private async Task<JsonDocument> DocumentAsync()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        var body = await client.GetStringAsync(DocumentUri, TestContext.Current.CancellationToken);
        return JsonDocument.Parse(body);
    }

    private static List<(string Name, JsonElement Operation)> Operations(JsonDocument document) =>
        [.. document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(op => (Name: $"{op.Name.ToUpperInvariant()} {path.Name}", Operation: op.Value)))];
}
