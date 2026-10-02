using PolvorApp.Api.Tests.Infrastructure;

namespace PolvorApp.Api.Tests;

public sealed class HostSmokeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Host_starts_with_a_valid_configuration()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }
}
