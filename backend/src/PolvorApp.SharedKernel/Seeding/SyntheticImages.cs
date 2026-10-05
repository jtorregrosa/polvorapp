using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolvorApp.SharedKernel.Seeding;

/// <summary>A committed face: a person who does not exist, with the gender and age band it was generated for.</summary>
public sealed record SyntheticFace(string File, SyntheticGender Gender, string AgeBand, int Age);

/// <summary>A committed emblem for one seeded comparsa.</summary>
public sealed record SyntheticLogo(string Comparsa, string File);

/// <summary>A person who needs a face; a <see langword="null"/> gender takes a face of any gender.</summary>
public sealed record SyntheticFaceRequest(int Key, SyntheticGender? Gender, DateOnly BirthDate);

/// <summary>The faces assigned by key, and how many had to be reused or taken from another age band.</summary>
public sealed record SyntheticFaceAssignment(IReadOnlyDictionary<int, SyntheticFace> Faces, int Reused, int OutsideBand);

/// <summary>
/// Reads the committed seed images (realistic-seed-data, design D5): <c>backend/synthetic-data</c>,
/// copied to the API's build output as <c>SyntheticData/</c>. Manifests are read once, lazily, and
/// every file name must be a plain name inside its folder. A missing folder, manifest or file fails
/// the seed with its name: the images are part of the build, so seeding without them would hide a
/// broken build behind placeholder data.
/// </summary>
public sealed class SyntheticImages(string root)
{
    public const string FolderName = "SyntheticData";

    private static readonly string[] Bands = ["16-17", "18-29", "30-44", "45-59", "60-75"];
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly Lazy<IReadOnlyList<SyntheticFace>> _faces = new(() => LoadFaces(root));
    private readonly Lazy<IReadOnlyList<SyntheticLogo>> _logos = new(() => LoadLogos(root));

    /// <summary>The images copied next to the running assembly.</summary>
    public static SyntheticImages FromBuildOutput() => new(Path.Combine(AppContext.BaseDirectory, FolderName));

    public IReadOnlyList<SyntheticFace> Faces => _faces.Value;

    /// <summary>The age band a face was generated for, from the person's age on <paramref name="today"/>.</summary>
    public static string AgeBandOf(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;
        if (birthDate.AddYears(age) > today)
        {
            age--;
        }

        return age switch
        {
            < 18 => "16-17",
            < 30 => "18-29",
            < 45 => "30-44",
            < 60 => "45-59",
            _ => "60-75",
        };
    }

    /// <summary>
    /// One face per request, in request order: a face of the person's gender and age band, the least
    /// used one first (manifest order breaks ties), so a face is reused only when its gender and band
    /// run out. With no face of that band, the closest band of the same gender is used; the result
    /// counts both, so the seeder can report them. A gender without any face fails.
    /// </summary>
    public SyntheticFaceAssignment AssignFaces(IEnumerable<SyntheticFaceRequest> requests, DateOnly today)
    {
        var all = Faces;
        if (all.Count == 0)
        {
            throw new InvalidOperationException($"The synthetic faces manifest in '{Path.Combine(root, "faces")}' lists no face.");
        }

        var uses = new Dictionary<SyntheticFace, int>();
        var assigned = new Dictionary<int, SyntheticFace>();
        var (reused, outsideBand) = (0, 0);
        foreach (var request in requests)
        {
            var band = Array.IndexOf(Bands, AgeBandOf(request.BirthDate, today));
            var pool = all.Where(f => request.Gender is null || f.Gender == request.Gender).ToList();
            if (pool.Count == 0)
            {
                throw new InvalidOperationException($"The synthetic faces manifest in '{Path.Combine(root, "faces")}' has no {request.Gender} face.");
            }

            var closest = pool.Min(f => Math.Abs(Array.IndexOf(Bands, f.AgeBand) - band));
            var face = pool
                .Where(f => Math.Abs(Array.IndexOf(Bands, f.AgeBand) - band) == closest)
                .MinBy(f => uses.GetValueOrDefault(f))!;
            reused += uses.ContainsKey(face) ? 1 : 0;
            outsideBand += closest > 0 ? 1 : 0;
            uses[face] = uses.GetValueOrDefault(face) + 1;
            assigned[request.Key] = face;
        }

        return new SyntheticFaceAssignment(assigned, reused, outsideBand);
    }

    /// <summary>The emblem generated for <paramref name="comparsa"/>, or <see langword="null"/> when there is none.</summary>
    public SyntheticLogo? LogoFor(string comparsa) =>
        _logos.Value.FirstOrDefault(l => string.Equals(l.Comparsa, comparsa, StringComparison.OrdinalIgnoreCase));

    public byte[] Read(SyntheticFace face) => ReadFile("faces", face.File);

    public byte[] Read(SyntheticLogo logo) => ReadFile("logos", logo.File);

    private byte[] ReadFile(string kind, string file)
    {
        var path = Path.Combine(root, kind, file);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"The synthetic image '{kind}/{file}' is listed in its manifest but missing from '{root}'.");
        }

        return File.ReadAllBytes(path);
    }

    private static List<SyntheticFace> LoadFaces(string root)
    {
        var (manifest, path) = ReadManifest<FacesManifest>(root, "faces");
        List<SyntheticFace> faces =
        [
            .. (manifest.Faces ?? []).Select(entry =>
            {
                var gender = entry.Gender switch
                {
                    "male" => SyntheticGender.Male,
                    "female" => SyntheticGender.Female,
                    _ => throw Invalid(path, $"unknown gender '{entry.Gender}'"),
                };
                if (!Bands.Contains(entry.AgeBand, StringComparer.Ordinal))
                {
                    throw Invalid(path, $"unknown age band '{entry.AgeBand}'");
                }

                var limits = entry.AgeBand!.Split('-');
                if (entry.Age < int.Parse(limits[0], CultureInfo.InvariantCulture) || entry.Age > int.Parse(limits[1], CultureInfo.InvariantCulture))
                {
                    throw Invalid(path, $"the age {entry.Age} of '{entry.File}' is outside its band {entry.AgeBand}");
                }

                return new SyntheticFace(PlainName(entry.File, path), gender, entry.AgeBand, entry.Age);
            }),
        ];
        return Unique(faces, f => f.File, path, "file");
    }

    private static List<SyntheticLogo> LoadLogos(string root)
    {
        var (manifest, path) = ReadManifest<LogosManifest>(root, "logos");
        List<SyntheticLogo> logos =
        [
            .. (manifest.Logos ?? []).Select(entry => string.IsNullOrWhiteSpace(entry.Comparsa)
                ? throw Invalid(path, "an entry has no comparsa")
                : new SyntheticLogo(entry.Comparsa, PlainName(entry.File, path))),
        ];
        Unique(logos, l => l.File, path, "file");
        return Unique(logos, l => l.Comparsa.ToUpperInvariant(), path, "comparsa");
    }

    private static List<T> Unique<T>(List<T> entries, Func<T, string> key, string manifest, string what)
    {
        var duplicate = entries.GroupBy(key, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        return duplicate is null ? entries : throw Invalid(manifest, $"the {what} '{duplicate.Key}' is listed twice");
    }

    private static (T Manifest, string Path) ReadManifest<T>(string root, string kind)
    {
        var path = Path.Combine(root, kind, "manifest.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"The synthetic {kind} manifest '{path}' is missing: the build output must hold backend/synthetic-data.");
        }

        try
        {
            return (JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw Invalid(path, "it is empty"), path);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"The synthetic image manifest '{path}' is not valid JSON.", exception);
        }
    }

    /// <summary>Only a plain file name: no separator, no parent reference, nothing rooted, safe characters.</summary>
    private static string PlainName(string? file, string manifest)
    {
        var safe = !string.IsNullOrEmpty(file)
            && file.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            && !file.Contains("..", StringComparison.Ordinal)
            && Path.GetFileName(file) == file;
        return safe ? file! : throw Invalid(manifest, $"the file name '{file}' is not a plain name inside its folder");
    }

    private static InvalidOperationException Invalid(string manifest, string reason) =>
        new($"The synthetic image manifest '{manifest}' is invalid: {reason}.");

    private sealed record FacesManifest([property: JsonPropertyName("faces")] List<FaceEntry>? Faces);

    private sealed record FaceEntry(string? File, string? Gender, string? AgeBand, int Age);

    private sealed record LogosManifest([property: JsonPropertyName("logos")] List<LogoEntry>? Logos);

    private sealed record LogoEntry(string? Comparsa, string? File);
}
