using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>Spec "Statistics (UC-07)": the first-year counts by gender (add-comparsa-orders, design D5).</summary>
public sealed class FirstYearStatisticsTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task The_statistics_count_the_first_year_by_gender()
    {
        // Two women (one only RESERVE in 2030) and one man without an earlier ACTIVE entry; one veteran.
        var women = await AddAsync(Gender.Female, 2);
        await AddAsync(Gender.Male, 1);
        var veteran = (await AddAsync(Gender.Male, 1))[0];
        var previous = NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated);
        await _orders.Services.SaveOrdersAsync(previous, NewEntry(previous, veteran), NewEntry(previous, women[0], ArquebusierStatus.Reserve));

        var statistics = await StatisticsAsync(_orders.Admin);

        var firstYear = statistics.GetProperty("firstYear");
        Assert.Equal((2, 1, 0), Genders(firstYear.GetProperty("firstYear")));
        Assert.Equal((0, 1, 0), Genders(firstYear.GetProperty("notFirstYear")));
    }

    [Fact]
    public async Task The_first_year_counts_follow_the_filters()
    {
        await AddAsync(Gender.Female, 1);
        await AddAsync(Gender.Female, 1, _orders.Other.Id);
        await _orders.Services.SaveOrdersAsync(NewOrder(_orders.Previous, _orders.Other.Id, OrderStatus.Validated));

        var own = await StatisticsAsync(_orders.FiringChief);
        var reserve = await StatisticsAsync(_orders.Admin, "?status=RESERVE");

        Assert.Equal((1, 0, 0), Genders(own.GetProperty("firstYear").GetProperty("firstYear")));
        Assert.Equal((0, 0, 0), Genders(reserve.GetProperty("firstYear").GetProperty("firstYear")));
    }

    [Fact]
    public async Task Without_history_the_first_year_is_unknown()
    {
        await AddAsync(Gender.Female, 1);

        Assert.Equal(JsonValueKind.Null, (await StatisticsAsync(_orders.Admin)).GetProperty("firstYear").ValueKind);
    }

    [Fact]
    public async Task Without_an_edition_in_progress_the_first_year_is_unknown()
    {
        await AddAsync(Gender.Female, 1);
        await _orders.Services.SaveOrdersAsync(NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated));
        await _orders.SetCurrentStatusAsync(EditionStatus.Closed);

        Assert.Equal(JsonValueKind.Null, (await StatisticsAsync(_orders.Admin)).GetProperty("firstYear").ValueKind);
    }

    [Fact]
    public async Task The_first_year_counts_hold_no_identifier()
    {
        var ids = await AddAsync(Gender.Female, 2);
        await _orders.Services.SaveOrdersAsync(NewOrder(_orders.Previous, _orders.Own.Id, OrderStatus.Validated));

        using var response = await _orders.Admin.GetAsync("/api/compliance/statistics", TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(ids, id => Assert.DoesNotContain(id.ToString(), text, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<Guid>> AddAsync(Gender gender, int count, Guid? comparsaId = null)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var (arquebusier, _) = await _orders.AddArquebusierAsync(comparsaId ?? _orders.Own.Id, $"Estadística Sintética {gender} {i}");
            ids.Add(arquebusier.Id);
        }

        await using var scope = _orders.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>().Arquebusiers
            .Where(a => ids.Contains(a.Id))
            .ExecuteUpdateAsync(a => a.SetProperty(x => x.Gender, gender), TestContext.Current.CancellationToken);
        return ids;
    }

    private static (int Female, int Male, int Unspecified) Genders(JsonElement counts) =>
        (counts.GetProperty("female").GetInt32(), counts.GetProperty("male").GetInt32(), counts.GetProperty("unspecified").GetInt32());

    private static async Task<JsonElement> StatisticsAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync($"/api/compliance/statistics{query}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }
}
