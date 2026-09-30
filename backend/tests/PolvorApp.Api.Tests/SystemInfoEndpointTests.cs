using System.Net;
using System.Text.Json;
using PolvorApp.Api.Platform.SystemInfo;
using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests;

[Collection(PostgresGroup.Name)]
public sealed class SystemInfoEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task System_info_returns_only_version_and_commit()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/system/info", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var properties = body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["commit", "version"], properties);
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("version").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("commit").GetString()));
    }

    [Theory]
    [InlineData("1.4.0+3f2a9c1d8e", "1.4.0", "3f2a9c1d8e")]
    [InlineData("0.1.0", "0.1.0", "unknown")]
    [InlineData("1.0.0+", "1.0.0", "unknown")]
    [InlineData("2.0.0-rc.1+abc+def", "2.0.0-rc.1", "abc+def")]
    public void Informational_version_is_split_into_version_and_commit(string informational, string version, string commit)
    {
        var info = SystemInfoResponse.FromInformationalVersion(informational);

        Assert.Equal(version, info.Version);
        Assert.Equal(commit, info.Commit);
    }
}
