using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Design D3: other modules read the settings through <see cref="IFederationSettings"/>, a snapshot
/// read once per scope (a request or a notification run).
/// </summary>
public sealed class FederationSettingsContractTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory? _factory;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync());
        Assert.Equal(0, await MigrateCommand.RunAsync(_factory.Services, Token));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task The_snapshot_carries_the_stored_values()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();

        var settings = await scope.ServiceProvider.GetRequiredService<IFederationSettings>().GetAsync(Token);

        Assert.Equal(
            new FederationSettingsSnapshot(
                "Unión de Comparsas de Moros y Cristianos «Ber-Largas»",
                "Unió de Comparses de Moros i Cristians «Ber-Largas»",
                "Unión de Comparsas",
                ContactEmail: null,
                Website: null,
                SenderName: "PolvorApp",
                ReplyTo: null,
                CloseReminderLeadDays: 7,
                MilestoneLeadDays: 7),
            settings);
    }

    [Fact]
    public async Task The_snapshot_is_read_once_per_scope_and_again_in_a_new_one()
    {
        await using var first = _factory!.Services.CreateAsyncScope();
        var reader = first.ServiceProvider.GetRequiredService<IFederationSettings>();
        var before = await reader.GetAsync(Token);

        await ExecuteAsync("UPDATE catalog.federation_settings SET short_name = 'Unión Cambiada'");

        Assert.Same(before, await reader.GetAsync(Token));
        await using var second = _factory.Services.CreateAsyncScope();
        Assert.Equal("Unión Cambiada", (await second.ServiceProvider.GetRequiredService<IFederationSettings>().GetAsync(Token)).ShortName);
    }

    [Theory]
    [InlineData(FederationNameForm.Spanish, "Unión de Comparsas de Moros y Cristianos «Ber-Largas»")]
    [InlineData(FederationNameForm.Valencian, "Unió de Comparses de Moros i Cristians «Ber-Largas»")]
    public async Task The_official_name_is_given_in_the_requested_form(FederationNameForm form, string expected)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();

        var settings = await scope.ServiceProvider.GetRequiredService<IFederationSettings>().GetAsync(Token);

        Assert.Equal(expected, settings.OfficialName(form));
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>().Database.ExecuteSqlRawAsync(sql, Token);
    }
}
