using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Persistence;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>Edition helpers for tests: API calls as an Admin, and direct state the API reaches only in several steps.</summary>
public static class EditionData
{
    /// <summary>A complete edition body: festival dates, order window and prices for <paramref name="year"/>.</summary>
    /// <summary>The prices <see cref="CompleteBody"/> sets with the default caps box price.</summary>
    public static readonly PolvorApp.FestivalEditions.Contracts.EditionPrices CompletePrices = new(55.00m, 4.50m, 30.00m, 6.00m);

    public static object CompleteBody(int year, uint version, decimal capsBox = 4.50m) => new
    {
        festivalStartsOn = $"{year}-04-22",
        festivalEndsOn = $"{year}-04-25",
        ordersOpenOn = $"{year}-01-10",
        ordersCloseOn = $"{year}-02-10",
        prices = new { powderPerKg = 55.00m, capsBox, weaponRental = 30m, flaskRental = 6m },
        version,
    };

    /// <summary>Creates a draft through the API and returns its id and version.</summary>
    public static async Task<(Guid Id, uint Version)> CreateEditionAsync(this HttpClient admin, int year)
    {
        using var response = await admin.PostAsync("/api/editions", new { year, festivalStartsOn = $"{year}-04-22", festivalEndsOn = $"{year}-04-25" });
        response.EnsureSuccessStatusCode();
        var edition = await ReadAsync<EditionJson>(response);
        return (edition.Id, edition.Version);
    }

    /// <summary>Creates a draft and completes its window and prices through the API.</summary>
    public static async Task<(Guid Id, uint Version)> CreateCompleteEditionAsync(this HttpClient admin, int year)
    {
        var (id, version) = await admin.CreateEditionAsync(year);
        using var response = await admin.PutAsJsonAsync($"/api/editions/{id}", CompleteBody(year, version), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (id, (await ReadAsync<EditionJson>(response)).Version);
    }

    /// <summary>Creates a rentable arcabuz model through the catalogue API.</summary>
    public static async Task<Guid> CreateRentableModelAsync(this HttpClient admin, string label, string handedness = "RIGHT", string size = "NORMAL")
    {
        using var response = await admin.PostAsync(
            "/api/weapon-models",
            new { kind = "ARCABUZ", side = "MOORISH", handedness, size, rentable = true, label });
        response.EnsureSuccessStatusCode();
        return (await ReadAsync<IdJson>(response)).Id;
    }

    /// <summary>The edition as the API returns it, with its current version.</summary>
    public static async Task<EditionJson> GetEditionAsync(this HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"/api/editions/{id}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<EditionJson>(response);
    }

    /// <summary>Sets the status and orders flag directly, as several API moves would.</summary>
    public static async Task SetStateAsync(this IdentityTestHost host, Guid id, EditionStatus status, bool ordersOpen = false)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        var edition = await db.Editions.SingleAsync(e => e.Id == id, TestContext.Current.CancellationToken);
        (edition.Status, edition.OrdersOpen) = (status, ordersOpen);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Adds models to an edition's set directly, bypassing the rentability check.</summary>
    public static async Task OfferAsync(this IdentityTestHost host, Guid editionId, params Guid[] modelIds)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FestivalEditionsDbContext>();
        db.EditionWeaponModels.AddRange(modelIds.Select(m => new FestivalEditions.Editions.EditionWeaponModel { EditionId = editionId, WeaponModelId = m }));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public sealed record IdJson(Guid Id);

    public sealed record PricesJson(decimal? PowderPerKg, decimal? CapsBox, decimal? WeaponRental, decimal? FlaskRental);

    public sealed record ModelJson(Guid Id, string Label, string Kind, bool Offered);

    public sealed record MilestoneJson(Guid Id, DateOnly Date, string Title);

    public sealed record NextWindowJson(string Kind, DateOnly Date);

    public sealed record EditionJson(
        Guid Id,
        int Year,
        string Status,
        bool OrdersOpen,
        uint Version,
        DateOnly FestivalStartsOn,
        DateOnly FestivalEndsOn,
        DateOnly? OrdersOpenOn,
        DateOnly? OrdersCloseOn,
        PricesJson Prices,
        List<ModelJson> WeaponModels,
        List<MilestoneJson> Milestones,
        NextWindowJson? NextWindow);

    public sealed record EditionRowJson(Guid Id, int Year, DateOnly FestivalStartsOn, DateOnly FestivalEndsOn, string Status, bool OrdersOpen, bool IsCurrent);

    public sealed record CurrentJson(EditionJson? Edition);
}
