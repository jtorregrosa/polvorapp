using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.OwnedWeapons;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ArquebusierRegistry.Photos;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FederationCatalog.WeaponModels;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// Synthetic registry and catalogue rows written directly, for tests that need state the API under
/// test does not create. National IDs come from a counter with the check letter computed, so every
/// arquebusier is valid and unique (SEC-11: synthetic only).
/// </summary>
public static class RegistryData
{
    private const string Letters = "TRWAGMYFPDXBNJZSQVHLCKE";
    private const int FirstFederationId = 800000;
    /// <summary>Test identities start at 00000101: low numbers unlikely to be in use, apart from the seed (00000001 to 00000013).</summary>
    private static int _sequence = 100;

    /// <summary>
    /// A valid synthetic DNI built from a very low number unlikely to be in use, and a federation id from 800000 up.
    /// Hard-coded ids in tests stay below 800000 so they never collide with these.
    /// </summary>
    public static (string NationalId, int FederationId) NextIdentity()
    {
        var number = Interlocked.Increment(ref _sequence);
        return (number.ToString("D8", CultureInfo.InvariantCulture) + Letters[number % Letters.Length], FirstFederationId + number);
    }

    internal static Comparsa NewComparsa(string name, bool active = true) =>
        new() { Id = Guid.CreateVersion7(), Name = name, Side = Side.Moorish, Active = active, CreatedAt = DateTimeOffset.UtcNow };

    internal static WeaponModel NewWeaponModel(string label, WeaponKind kind = WeaponKind.Arcabuz, bool active = true) => new()
    {
        Id = Guid.CreateVersion7(),
        Kind = kind,
        Label = label,
        Side = kind == WeaponKind.Pistol ? null : Side.Moorish,
        Handedness = kind == WeaponKind.Pistol ? null : Handedness.Right,
        Size = kind == WeaponKind.Pistol ? null : WeaponSize.Normal,
        Active = active,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    internal static Arquebusier NewArquebusier(Guid comparsaId, string lastName = "Sintético Prueba")
    {
        var (nationalId, federationId) = NextIdentity();
        return new Arquebusier
        {
            Id = Guid.CreateVersion7(),
            ComparsaId = comparsaId,
            FederationId = federationId,
            NationalId = nationalId,
            FirstName = "Arcabucero",
            LastName = lastName,
            BirthDate = new DateOnly(1990, 5, 1),
            Gender = Gender.Unspecified,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    internal static OwnedWeapon NewOwnedWeapon(Guid arquebusierId, Guid weaponModelId, string guide) => new()
    {
        Id = Guid.CreateVersion7(),
        ArquebusierId = arquebusierId,
        WeaponModelId = weaponModelId,
        WeaponNumber = "1234",
        OwnershipGuideNumber = guide,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>A photo row whose object key follows the registry convention; the object itself is not stored.</summary>
    internal static ArquebusierPhoto NewPhoto(Guid arquebusierId, ArquebusierPhotoKind kind)
    {
        var id = Guid.CreateVersion7();
        return new ArquebusierPhoto
        {
            Id = id,
            ArquebusierId = arquebusierId,
            Kind = kind,
            ObjectKey = ArquebusierPhoto.KeyFor(id),
            Width = 600,
            Height = 800,
            SizeBytes = 1000,
            UploadedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Saves comparsas and weapon models in the catalog schema.</summary>
    internal static async Task SaveCatalogAsync(this IServiceProvider services, params object[] entities)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Saves arquebusiers and owned weapons in the registry schema.</summary>
    internal static async Task SaveRegistryAsync(this IServiceProvider services, params object[] entities)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
