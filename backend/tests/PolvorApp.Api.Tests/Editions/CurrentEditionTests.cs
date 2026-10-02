using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.FestivalEditions.Endpoints;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.EditionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Editions;

/// <summary>
/// Spec "Current edition" and design D4/D7: the edition in progress with its orders flag and next
/// order window date, and the contract other modules read (IEditionDirectory).
/// </summary>
public sealed class CurrentEditionTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        _admin = await _host.SignInAsync(await _host.CreateUserAsync("admin.actual@example.test", UserRole.Admin));
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_current_edition_says_whether_its_orders_are_open(bool ordersOpen)
    {
        var (id, _) = await _admin.CreateCompleteEditionAsync(2031);
        await _host.SetStateAsync(id, EditionStatus.InProgress, ordersOpen);
        using var chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.actual@example.test"));

        using var response = await chief.GetAsync("/api/editions/current", TestContext.Current.CancellationToken);

        var edition = (await ReadAsync<CurrentJson>(response)).Edition;
        Assert.Equal((id, ordersOpen), (edition!.Id, edition.OrdersOpen));

        var snapshot = await DirectoryAsync(d => d.GetCurrentAsync(TestContext.Current.CancellationToken));
        Assert.Equal((id, 2031, EditionStatus.InProgress, ordersOpen), (snapshot!.Id, snapshot.Year, snapshot.Status, snapshot.OrdersOpen));
        Assert.Equal((new DateOnly(2031, 4, 22), new DateOnly(2031, 4, 25)), (snapshot.FestivalStartsOn, snapshot.FestivalEndsOn));
    }

    [Fact]
    public async Task There_is_no_current_edition_without_one_in_progress()
    {
        var (closed, _) = await _admin.CreateCompleteEditionAsync(2030);
        await _host.SetStateAsync(closed, EditionStatus.Closed);
        await _admin.CreateEditionAsync(2031);

        Assert.Null(await DirectoryAsync(d => d.GetCurrentAsync(TestContext.Current.CancellationToken)));
        Assert.Equal(EditionStatus.Closed, (await DirectoryAsync(d => d.FindAsync(closed, TestContext.Current.CancellationToken)))!.Status);
        Assert.Null(await DirectoryAsync(d => d.FindAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Models_no_longer_rentable_are_not_offered_to_other_modules()
    {
        var (id, _) = await _admin.CreateEditionAsync(2031);
        var kept = await _admin.CreateRentableModelAsync("ARCABUZ OFRECIDO");
        var retired = await _admin.CreateRentableModelAsync("ARCABUZ YA NO OFRECIDO", handedness: "LEFT");
        await _host.OfferAsync(id, kept, retired);
        using (var deactivate = await _admin.PostAsync($"/api/weapon-models/{retired}/deactivate", new { }))
        {
            deactivate.EnsureSuccessStatusCode();
        }

        var snapshot = await DirectoryAsync(d => d.FindAsync(id, TestContext.Current.CancellationToken));

        Assert.Equal([kept], snapshot!.OfferedWeaponModelIds);
    }

    [Theory]
    [InlineData(5, 30, "OPENS", 5)]
    [InlineData(-5, 30, "CLOSES", 30)]
    [InlineData(-5, 0, "CLOSES", 0)]
    public async Task The_next_window_date_is_relative_to_today_in_madrid(int opensIn, int closesIn, string kind, int nextIn)
    {
        // Relative to the test clock's today in Madrid: moving the clock would expire the session.
        var today = PolvorApp.SharedKernel.Time.FederationCalendar.Today(_host.Time);
        var year = today.Year + 1;
        var (id, version) = await _admin.CreateEditionAsync(year);
        using (var window = await _admin.PutAsJsonAsync(
            $"/api/editions/{id}",
            new
            {
                festivalStartsOn = $"{year}-12-01",
                festivalEndsOn = $"{year}-12-02",
                ordersOpenOn = today.AddDays(opensIn).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ordersCloseOn = today.AddDays(closesIn).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                version,
            },
            TestContext.Current.CancellationToken))
        {
            window.EnsureSuccessStatusCode();
        }

        await _host.SetStateAsync(id, EditionStatus.InProgress);

        using var response = await _admin.GetAsync("/api/editions/current", TestContext.Current.CancellationToken);

        Assert.Equal(new NextWindowJson(kind, today.AddDays(nextIn)), (await ReadAsync<CurrentJson>(response)).Edition!.NextWindow);
    }

    [Fact]
    public void There_is_no_next_window_date_when_both_have_passed_or_are_not_set()
    {
        var edition = new FestivalEdition
        {
            Id = Guid.CreateVersion7(),
            Year = 2031,
            FestivalStartsOn = new DateOnly(2031, 4, 22),
            FestivalEndsOn = new DateOnly(2031, 4, 25),
            OrdersOpenOn = new DateOnly(2031, 1, 10),
            OrdersCloseOn = new DateOnly(2031, 2, 10),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Assert.Null(EditionViews.NextWindow(edition, new DateOnly(2031, 2, 11)));
        Assert.Equal(new EditionNextWindow(EditionWindowKind.Opens, new DateOnly(2031, 1, 10)), EditionViews.NextWindow(edition, new DateOnly(2031, 1, 10)));
        edition.OrdersOpenOn = null;
        edition.OrdersCloseOn = null;
        Assert.Null(EditionViews.NextWindow(edition, new DateOnly(2031, 1, 1)));
    }

    private async Task<T> DirectoryAsync<T>(Func<IEditionDirectory, Task<T>> read)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<IEditionDirectory>());
    }
}
