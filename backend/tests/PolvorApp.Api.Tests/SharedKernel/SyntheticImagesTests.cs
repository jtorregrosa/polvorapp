using System.Text.Json;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.Api.Tests.SharedKernel;

/// <summary>
/// The committed seed images (realistic-seed-data, design D5): manifests read safely from the build
/// output, faces matched by gender and age band without early reuse, logos by comparsa.
/// </summary>
public sealed class SyntheticImagesTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 4);

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("polvorapp-synthetic-");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void The_build_output_holds_the_committed_faces()
    {
        var images = SyntheticImages.FromBuildOutput();

        Assert.Equal(400, images.Faces.Count);
        Assert.All(images.Faces.Take(5), face => Assert.True(images.Read(face).Length > 10_000));
    }

    [Fact]
    public void A_missing_folder_fails_naming_it()
    {
        var images = new SyntheticImages(Path.Combine(_root.FullName, "nowhere"));

        var error = Assert.Throws<InvalidOperationException>(() => images.Faces);

        Assert.Contains("nowhere", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../face.jpg")]
    [InlineData("..")]
    [InlineData("sub/face.jpg")]
    [InlineData("sub\\face.jpg")]
    [InlineData("C:\\face.jpg")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void A_manifest_entry_outside_the_folder_is_refused(string file)
    {
        WriteFaces(new { file, gender = "male", ageBand = "30-44", age = 35, seed = 1 });

        var error = Assert.Throws<InvalidOperationException>(() => new SyntheticImages(_root.FullName).Faces);

        Assert.Contains("manifest.json", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("other", "30-44")]
    [InlineData("male", "30-40")]
    public void A_manifest_entry_with_an_unknown_gender_or_band_names_the_manifest(string gender, string band)
    {
        WriteFaces(new { file = "face-0001.jpg", gender, ageBand = band, age = 35, seed = 1 });

        var error = Assert.Throws<InvalidOperationException>(() => new SyntheticImages(_root.FullName).Faces);

        Assert.Contains("manifest.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_face_missing_from_disk_fails_naming_the_file()
    {
        WriteFaces(new { file = "face-0009.jpg", gender = "male", ageBand = "30-44", age = 35, seed = 1 });
        var images = new SyntheticImages(_root.FullName);

        var error = Assert.Throws<InvalidOperationException>(() => images.Read(images.Faces[0]));

        Assert.Contains("face-0009.jpg", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2010, 1, 1, "16-17")]
    [InlineData(2008, 10, 4, "18-29")]
    [InlineData(1996, 10, 5, "18-29")]
    [InlineData(1982, 1, 1, "30-44")]
    [InlineData(1970, 6, 1, "45-59")]
    [InlineData(1950, 1, 1, "60-75")]
    public void The_age_band_follows_the_birth_date(int year, int month, int day, string band)
    {
        Assert.Equal(band, SyntheticImages.AgeBandOf(new DateOnly(year, month, day), Today));
    }

    [Fact]
    public void Faces_match_gender_and_band_and_are_reused_only_when_the_band_runs_out()
    {
        WriteFaces(
            Face(1, "male", "30-44"), Face(2, "male", "30-44"), Face(3, "female", "30-44"),
            Face(4, "male", "60-75"), Face(5, "female", "18-29"));
        var images = new SyntheticImages(_root.FullName);
        var thirtyFive = Today.AddYears(-35);

        var assignment = images.AssignFaces(
        [
            new SyntheticFaceRequest(1, SyntheticGender.Male, thirtyFive),
            new SyntheticFaceRequest(2, SyntheticGender.Male, thirtyFive),
            new SyntheticFaceRequest(3, SyntheticGender.Male, thirtyFive),
            new SyntheticFaceRequest(4, SyntheticGender.Female, thirtyFive),
            new SyntheticFaceRequest(5, null, Today.AddYears(-70)),
            new SyntheticFaceRequest(6, SyntheticGender.Female, Today.AddYears(-50)),
        ], Today);
        var faces = assignment.Faces;

        Assert.Equal("face-0001.jpg", faces[1].File);
        Assert.Equal("face-0002.jpg", faces[2].File);
        Assert.Equal("face-0001.jpg", faces[3].File);
        Assert.Equal("face-0003.jpg", faces[4].File);
        Assert.Equal("face-0004.jpg", faces[5].File);

        // No woman of 45-59: the closest band of her gender (30-44, face 3), counted.
        Assert.Equal(SyntheticGender.Female, faces[6].Gender);
        Assert.Equal(2, assignment.Reused); // face 1 for the third man, face 3 for the woman of 50
        Assert.Equal(1, assignment.OutsideBand);
    }

    [Fact]
    public void A_gender_without_faces_fails()
    {
        WriteFaces(Face(1, "male", "30-44"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            new SyntheticImages(_root.FullName).AssignFaces([new SyntheticFaceRequest(1, SyntheticGender.Female, Today.AddYears(-35))], Today));

        Assert.Contains("Female", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_face_listed_twice_or_with_an_age_outside_its_band_is_refused()
    {
        WriteFaces(Face(1, "male", "30-44"), Face(1, "male", "30-44"));
        Assert.Throws<InvalidOperationException>(() => new SyntheticImages(_root.FullName).Faces);

        WriteFaces(new { file = "face-0001.jpg", gender = "male", ageBand = "30-44", age = 50, seed = 1 });
        Assert.Throws<InvalidOperationException>(() => new SyntheticImages(_root.FullName).Faces);
    }

    [Fact]
    public void Logos_are_matched_by_comparsa_name_ignoring_case()
    {
        Directory.CreateDirectory(Path.Combine(_root.FullName, "logos"));
        File.WriteAllBytes(Path.Combine(_root.FullName, "logos", "cruzados.png"), SyntheticPng.Rgb(300, 300, (_, _) => (0, 0, 0)));
        WriteManifest("logos", new { logos = new[] { new { comparsa = "Cruzados", file = "cruzados.png", seed = 1, prompt = "p" } } });
        var images = new SyntheticImages(_root.FullName);

        var logo = images.LogoFor("CRUZADOS");

        Assert.NotNull(logo);
        Assert.True(images.Read(logo).Length > 0);
        Assert.Null(images.LogoFor("Abencerrajes"));
    }

    private static object Face(int number, string gender, string band) =>
        new { file = $"face-{number:D4}.jpg", gender, ageBand = band, age = int.Parse(band[..2], System.Globalization.CultureInfo.InvariantCulture), seed = number };

    private void WriteFaces(params object[] faces) => WriteManifest("faces", new { faces });

    private void WriteManifest(string kind, object manifest)
    {
        Directory.CreateDirectory(Path.Combine(_root.FullName, kind));
        File.WriteAllText(Path.Combine(_root.FullName, kind, "manifest.json"), JsonSerializer.Serialize(manifest));
    }
}
