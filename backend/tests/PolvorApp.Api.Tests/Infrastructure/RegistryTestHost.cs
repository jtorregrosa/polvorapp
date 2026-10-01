using System.Text.Json;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// A signed-in Admin and a FiringChief assigned to <see cref="Own"/>, plus an active comparsa outside
/// the FiringChief's scope (<see cref="Other"/>) and an inactive one assigned to them
/// (<see cref="Inactive"/>). All data is synthetic.
/// </summary>
public sealed class RegistryTestHost : IAsyncDisposable
{
    private RegistryTestHost(IdentityTestHost host) => Host = host;

    public IdentityTestHost Host { get; }

    public HttpClient Admin { get; private set; } = null!;

    public HttpClient FiringChief { get; private set; } = null!;

    public Guid AdminId { get; private set; }

    public Guid FiringChiefId { get; private set; }

    private SyntheticUser ChiefUser { get; set; } = null!;

    internal Comparsa Own { get; } = RegistryData.NewComparsa("Comparsa Sintética Propia");

    internal Comparsa Other { get; } = RegistryData.NewComparsa("Comparsa Sintética Ajena");

    internal Comparsa Inactive { get; } = RegistryData.NewComparsa("Comparsa Sintética Inactiva", active: false);

    public IServiceProvider Services => Host.Services;

    /// <summary>Today in Europe/Madrid, as the API sees it.</summary>
    public DateOnly Today => FederationCalendar.Today(Host.Time);

    public static async Task<RegistryTestHost> StartAsync(
        PostgresFixture postgres,
        MailpitFixture mailpit,
        Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var registry = new RegistryTestHost(await IdentityTestHost.StartAsync(postgres, mailpit, settings, configureServices));
        await registry.Services.SaveCatalogAsync(registry.Own, registry.Other, registry.Inactive);

        var admin = await registry.Host.CreateUserAsync("admin.registro@example.test", UserRole.Admin);
        var chief = await registry.Host.CreateUserAsync("jefe.registro@example.test", UserRole.FiringChief);
        await registry.Host.AssignAsync(registry.Own.Id, chief.Id);
        await registry.Host.AssignAsync(registry.Inactive.Id, chief.Id);
        (registry.AdminId, registry.FiringChiefId, registry.ChiefUser) = (admin.Id, chief.Id, chief);
        registry.Admin = await registry.Host.SignInAsync(admin);
        registry.FiringChief = await registry.Host.SignInAsync(chief);
        return registry;
    }

    /// <summary>A new signed-in client for the FiringChief, e.g. after moving the clock past the session lifetime.</summary>
    public Task<HttpClient> SignInFiringChiefAgainAsync() => Host.SignInAsync(ChiefUser);

    /// <summary>
    /// Issue date of the default AE license: a year before the real date, so the license is valid for
    /// four more years whatever day the tests run (the host clock starts at the real time).
    /// </summary>
    public static DateOnly DefaultIssuedOn { get; } = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1);

    /// <summary>The BR-03 default expiry of <see cref="DefaultIssuedOn"/>.</summary>
    public static DateOnly DefaultExpiresOn { get; } = DefaultIssuedOn.AddYears(5);

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A valid registration body for <paramref name="comparsaId"/> with a fresh synthetic identity.</summary>
    public static Dictionary<string, object?> NewArquebusier(Guid comparsaId)
    {
        var (nationalId, federationId) = RegistryData.NextIdentity();
        return new Dictionary<string, object?>
        {
            ["comparsaId"] = comparsaId,
            ["federationId"] = federationId,
            ["nationalId"] = nationalId,
            ["firstName"] = "Arcabucera",
            ["lastName"] = "Sintética Uno",
            ["birthDate"] = "1990-05-01",
            ["email"] = "arcabucera.uno@polvorapp.example",
            ["phone"] = "+34 600 000 001",
            ["gender"] = "FEMALE",
            ["trainingCompletedOn"] = "2025-11-15",
            ["license"] = new Dictionary<string, object?> { ["type"] = "AE", ["pending"] = false, ["issuedOn"] = Iso(DefaultIssuedOn) },
        };
    }

    /// <summary>Registers an arquebusier as the Admin and returns the response body.</summary>
    public async Task<JsonElement> RegisterAsync(Guid comparsaId, Action<Dictionary<string, object?>>? change = null)
    {
        var body = NewArquebusier(comparsaId);
        change?.Invoke(body);
        using var response = await Admin.PostAsync("/api/arquebusiers", body);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return await IdentityAssertions.ReadAsync<JsonElement>(response);
    }

    public async ValueTask DisposeAsync()
    {
        Admin?.Dispose();
        FiringChief?.Dispose();
        await Host.DisposeAsync();
    }
}
