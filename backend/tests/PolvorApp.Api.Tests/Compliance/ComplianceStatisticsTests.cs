using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Compliance;

/// <summary>Spec "Statistics (UC-07)": aggregates of the caller's scope, filters, and no personal data.</summary>
public sealed class ComplianceStatisticsTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private static readonly string[] LicenseStates = ["valid", "expiring", "expired", "pending", "none"];

    private RegistryTestHost _registry = null!;
    private readonly List<Arquebusier> _arquebusiers = [];

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        var today = _registry.Today;
        var trabuco = RegistryData.NewWeaponModel("TRABUCO SINTÉTICO ESTADÍSTICO", WeaponKind.Trabuco);
        var pistol = RegistryData.NewWeaponModel("PISTOLA SINTÉTICA ESTADÍSTICA", WeaponKind.Pistol);
        await _registry.Services.SaveCatalogAsync(trabuco, pistol);

        object[][] people =
        [
            // Own: four active or reserve arquebusiers covering every bracket and license state but expired.
            ComplianceData.Arquebusier(_registry.Own.Id, today, age: 24, gender: Gender.Female),
            ComplianceData.Arquebusier(_registry.Own.Id, today, age: 25, gender: Gender.Male, course: false, license: LicenseCase.Expiring),
            ComplianceData.Arquebusier(_registry.Own.Id, today, age: 44, status: ArquebusierStatus.Reserve, license: LicenseCase.Pending),
            ComplianceData.Arquebusier(_registry.Own.Id, today, age: 45, gender: Gender.Female, license: LicenseCase.None),

            // Inactive (also the FiringChief's) and Other (outside their scope).
            ComplianceData.Arquebusier(_registry.Inactive.Id, today, age: 60, gender: Gender.Male, status: ArquebusierStatus.Reserve, license: LicenseCase.Expired),
            ComplianceData.Arquebusier(_registry.Other.Id, today, age: 30, gender: Gender.Female),
        ];
        _arquebusiers.AddRange(people.Select(ComplianceData.Row));
        var owner = _arquebusiers[0];
        await _registry.Services.SaveRegistryAsync(
        [
            .. people.SelectMany(rows => rows),
            RegistryData.NewOwnedWeapon(owner.Id, trabuco.Id, "SINT-EST-1"),
            RegistryData.NewOwnedWeapon(owner.Id, pistol.Id, "SINT-EST-2"),
        ]);
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_FiringChief_gets_only_their_comparsas_with_a_row_for_each()
    {
        var statistics = await StatisticsAsync(_registry.FiringChief, string.Empty);

        Assert.Equal((5, 3, 2), (Int(statistics, "total"), Int(statistics, "active"), Int(statistics, "reserve")));
        Assert.Equal(
            ["Comparsa Sintética Inactiva", "Comparsa Sintética Propia"],
            statistics.GetProperty("comparsas").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task A_FiringChief_of_one_comparsa_gets_no_comparsa_rows()
    {
        var chief = await _registry.Host.CreateUserAsync("jefe.una.comparsa@example.test", UserRole.FiringChief);
        await _registry.Host.AssignAsync(_registry.Other.Id, chief.Id);
        using var client = await _registry.Host.SignInAsync(chief);

        var statistics = await StatisticsAsync(client, string.Empty);

        Assert.Equal(1, Int(statistics, "total"));
        Assert.Empty(statistics.GetProperty("comparsas").EnumerateArray());
    }

    [Fact]
    public async Task An_Admin_gets_a_sorted_row_per_comparsa_that_adds_up()
    {
        var statistics = await StatisticsAsync(_registry.Admin, string.Empty);

        var rows = statistics.GetProperty("comparsas").EnumerateArray().ToList();
        Assert.Equal(["Comparsa Sintética Ajena", "Comparsa Sintética Inactiva", "Comparsa Sintética Propia"], rows.Select(r => r.GetProperty("name").GetString()));
        Assert.Equal(Int(statistics, "total"), rows.Sum(r => Int(r, "total")));
        var own = rows.Single(r => r.GetProperty("comparsaId").GetGuid() == _registry.Own.Id);
        Assert.Equal((4, 3, 1, 3), (Int(own, "total"), Int(own, "active"), Int(own, "reserve"), Int(own, "withWarnings")));
        Assert.Equal((1, 2, 1), Genders(own.GetProperty("gender")));
    }

    [Fact]
    public async Task Filters_by_comparsa_and_status()
    {
        var statistics = await StatisticsAsync(_registry.Admin, $"?comparsaId={_registry.Own.Id}&status=ACTIVE");

        Assert.Equal((3, 3, 0), (Int(statistics, "total"), Int(statistics, "active"), Int(statistics, "reserve")));
        Assert.Equal((1, 2, 0), Genders(statistics.GetProperty("gender")));
        Assert.Empty(statistics.GetProperty("comparsas").EnumerateArray());
    }

    [Fact]
    public async Task Counts_gender_course_licenses_and_owned_weapons()
    {
        var statistics = await StatisticsAsync(_registry.Admin, $"?comparsaId={_registry.Own.Id}");

        Assert.Equal((1, 2, 1), Genders(statistics.GetProperty("gender")));
        var course = statistics.GetProperty("course");
        Assert.Equal((0, 2, 1), Genders(course.GetProperty("done")));
        Assert.Equal((1, 0, 0), Genders(course.GetProperty("notDone")));
        var licenses = statistics.GetProperty("licenses");
        Assert.Equal(
            (1, 1, 0, 1, 1),
            (Int(licenses, "valid"), Int(licenses, "expiring"), Int(licenses, "expired"), Int(licenses, "pending"), Int(licenses, "none")));
        var weapons = statistics.GetProperty("ownedWeapons");
        Assert.Equal((0, 1, 0), Genders(weapons.GetProperty("withWeapon")));
        Assert.Equal((1, 1, 1), Genders(weapons.GetProperty("withoutWeapon")));
        Assert.Equal(
            new Dictionary<string, int> { ["TRABUCO"] = 1, ["ARCABUZ"] = 0, ["PISTOL"] = 1 },
            weapons.GetProperty("byKind").EnumerateArray().ToDictionary(k => k.GetProperty("kind").GetString()!, k => Int(k, "count")));
    }

    [Fact]
    public async Task Age_brackets_split_by_gender()
    {
        var statistics = await StatisticsAsync(_registry.Admin, $"?comparsaId={_registry.Own.Id}");

        var brackets = statistics.GetProperty("ageBrackets").EnumerateArray()
            .ToDictionary(b => b.GetProperty("bracket").GetString()!, b => Genders(b.GetProperty("counts")));
        Assert.Equal(["UNDER_25", "FROM_25_TO_34", "FROM_35_TO_44", "FROM_45"], brackets.Keys);
        Assert.Equal((0, 1, 0), brackets["UNDER_25"]);
        Assert.Equal((1, 0, 0), brackets["FROM_25_TO_34"]);
        Assert.Equal((0, 0, 1), brackets["FROM_35_TO_44"]);
        Assert.Equal((0, 1, 0), brackets["FROM_45"]);
    }

    [Fact]
    public async Task The_25th_birthday_moves_to_the_next_bracket()
    {
        var comparsa = RegistryData.NewComparsa("Comparsa Sintética Cumpleaños");
        await _registry.Services.SaveCatalogAsync(comparsa);
        var turning = RegistryData.NewArquebusier(comparsa.Id);
        turning.BirthDate = _registry.Today.AddYears(-25);
        var almost = RegistryData.NewArquebusier(comparsa.Id);
        almost.BirthDate = _registry.Today.AddYears(-25).AddDays(1);
        await _registry.Services.SaveRegistryAsync(turning, almost);

        var statistics = await StatisticsAsync(_registry.Admin, $"?comparsaId={comparsa.Id}");

        var brackets = statistics.GetProperty("ageBrackets").EnumerateArray()
            .ToDictionary(b => b.GetProperty("bracket").GetString()!, b => Genders(b.GetProperty("counts")).Unspecified);
        Assert.Equal(1, brackets["UNDER_25"]);
        Assert.Equal(1, brackets["FROM_25_TO_34"]);
    }

    [Fact]
    public async Task A_comparsa_outside_the_scope_or_unknown_is_not_found()
    {
        using var foreign = await _registry.FiringChief.GetAsync($"/api/compliance/statistics?comparsaId={_registry.Other.Id}", TestContext.Current.CancellationToken);
        using var unknown = await _registry.Admin.GetAsync($"/api/compliance/statistics?comparsaId={Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "compliance.comparsaNotFound");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "compliance.comparsaNotFound");
    }

    [Fact]
    public async Task An_unknown_status_is_invalid()
    {
        using var response = await _registry.Admin.GetAsync("/api/compliance/statistics?status=INACTIVE", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("invalid", (await ErrorsAsync(response))["status"]);
    }

    [Fact]
    public async Task A_request_without_a_session_is_unauthorized()
    {
        using var anonymous = _registry.Host.Factory.CreateClient();

        using var response = await anonymous.GetAsync("/api/compliance/statistics", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_response_holds_only_counts_and_comparsa_names()
    {
        using var response = await _registry.Admin.GetAsync("/api/compliance/statistics", TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var arquebusier in _arquebusiers)
        {
            Assert.DoesNotContain(arquebusier.Id.ToString(), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(arquebusier.NationalId, text, StringComparison.Ordinal);
            Assert.DoesNotContain(RegistryTestHost.Iso(arquebusier.BirthDate), text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Arcabucero", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Sintético Prueba", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?status=ACTIVE")]
    [InlineData("?status=RESERVE")]
    public async Task Every_breakdown_adds_up_to_the_total(string query)
    {
        var statistics = await StatisticsAsync(_registry.Admin, query);
        var total = Int(statistics, "total");

        static int Sum((int Male, int Female, int Unspecified) g) => g.Male + g.Female + g.Unspecified;
        var licenses = statistics.GetProperty("licenses");
        var weapons = statistics.GetProperty("ownedWeapons");
        var course = statistics.GetProperty("course");
        Assert.Equal(total, Int(statistics, "active") + Int(statistics, "reserve"));
        Assert.Equal(total, Sum(Genders(statistics.GetProperty("gender"))));
        Assert.Equal(total, statistics.GetProperty("ageBrackets").EnumerateArray().Sum(b => Sum(Genders(b.GetProperty("counts")))));
        Assert.Equal(total, Sum(Genders(course.GetProperty("done"))) + Sum(Genders(course.GetProperty("notDone"))));
        Assert.Equal(total, LicenseStates.Sum(state => Int(licenses, state)));
        Assert.Equal(total, Sum(Genders(weapons.GetProperty("withWeapon"))) + Sum(Genders(weapons.GetProperty("withoutWeapon"))));
        Assert.Equal(total, statistics.GetProperty("comparsas").EnumerateArray().Sum(c => Int(c, "total")));
    }

    [Fact]
    public async Task A_status_filter_drops_comparsas_without_matching_arquebusiers()
    {
        var statistics = await StatisticsAsync(_registry.Admin, "?status=RESERVE");

        Assert.Equal((2, 0, 2), (Int(statistics, "total"), Int(statistics, "active"), Int(statistics, "reserve")));
        Assert.Equal(1, Int(statistics.GetProperty("licenses"), "expired"));
        Assert.Equal(
            ["Comparsa Sintética Inactiva", "Comparsa Sintética Propia"],
            statistics.GetProperty("comparsas").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Two_weapons_of_one_kind_count_twice()
    {
        var comparsa = RegistryData.NewComparsa("Comparsa Sintética Armera");
        var arcabuz = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO DOBLE");
        await _registry.Services.SaveCatalogAsync(comparsa, arcabuz);
        var owner = RegistryData.NewArquebusier(comparsa.Id);
        await _registry.Services.SaveRegistryAsync(
            owner,
            RegistryData.NewOwnedWeapon(owner.Id, arcabuz.Id, "SINT-DOBLE-1"),
            RegistryData.NewOwnedWeapon(owner.Id, arcabuz.Id, "SINT-DOBLE-2"));

        var statistics = await StatisticsAsync(_registry.Admin, $"?comparsaId={comparsa.Id}");

        var byKind = statistics.GetProperty("ownedWeapons").GetProperty("byKind").EnumerateArray()
            .ToDictionary(k => k.GetProperty("kind").GetString()!, k => Int(k, "count"));
        Assert.Equal(2, byKind["ARCABUZ"]);
        Assert.Equal(1, Genders(statistics.GetProperty("ownedWeapons").GetProperty("withWeapon")).Unspecified);
    }

    /// <summary>Owned weapons follow the scope and the filters like every other figure (test analysis).</summary>
    [Fact]
    public async Task Owned_weapons_follow_the_scope_and_the_filters()
    {
        var arcabuz = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO FILTRADO");
        await _registry.Services.SaveCatalogAsync(arcabuz);
        var reserveInOwn = _arquebusiers[2];
        var inOther = _arquebusiers[5];
        await _registry.Services.SaveRegistryAsync(
            RegistryData.NewOwnedWeapon(reserveInOwn.Id, arcabuz.Id, "SINT-FILT-1"),
            RegistryData.NewOwnedWeapon(inOther.Id, arcabuz.Id, "SINT-FILT-2"));

        static int Arcabuces(JsonElement statistics) =>
            statistics.GetProperty("ownedWeapons").GetProperty("byKind").EnumerateArray()
                .Single(k => k.GetProperty("kind").GetString() == "ARCABUZ").GetProperty("count").GetInt32();

        Assert.Equal(2, Arcabuces(await StatisticsAsync(_registry.Admin, string.Empty)));
        Assert.Equal(1, Arcabuces(await StatisticsAsync(_registry.FiringChief, string.Empty)));
        Assert.Equal(0, Arcabuces(await StatisticsAsync(_registry.FiringChief, "?status=ACTIVE")));
        Assert.Equal(1, Arcabuces(await StatisticsAsync(_registry.Admin, $"?comparsaId={_registry.Other.Id}")));
    }

    [Fact]
    public async Task A_FiringChief_filtering_one_of_their_comparsas_gets_no_comparsa_rows()
    {
        var statistics = await StatisticsAsync(_registry.FiringChief, $"?comparsaId={_registry.Inactive.Id}");

        Assert.Equal(1, Int(statistics, "total"));
        Assert.Empty(statistics.GetProperty("comparsas").EnumerateArray());
    }

    [Fact]
    public async Task Foreign_and_unknown_comparsas_look_the_same_to_a_FiringChief()
    {
        using var foreign = await _registry.FiringChief.GetAsync($"/api/compliance/statistics?comparsaId={_registry.Other.Id}", TestContext.Current.CancellationToken);
        using var unknown = await _registry.FiringChief.GetAsync($"/api/compliance/statistics?comparsaId={Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        static async Task<string> WithoutTraceAsync(HttpResponseMessage response)
        {
            var body = (await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(TestContext.Current.CancellationToken))!;
            body.Remove("traceId");
            return JsonSerializer.Serialize(body);
        }

        Assert.Equal(foreign.StatusCode, unknown.StatusCode);
        Assert.Equal(await WithoutTraceAsync(foreign), await WithoutTraceAsync(unknown));
    }

    [Fact]
    public async Task Insight_responses_are_not_cached()
    {
        foreach (var path in new[] { "/api/compliance/summary", "/api/compliance/statistics" })
        {
            using var response = await _registry.Admin.GetAsync(path, TestContext.Current.CancellationToken);

            Assert.True(response.Headers.CacheControl?.NoStore, path);
        }
    }

    /// <summary>
    /// An allow-list of the response shape (security review): every leaf is a count or an enum code,
    /// except the comparsa id and name of the per-comparsa rows. A new field fails here.
    /// </summary>
    [Fact]
    public async Task The_response_shape_holds_only_counts_codes_and_comparsas()
    {
        var statistics = await StatisticsAsync(_registry.Admin, string.Empty);
        string[] codes = ["UNDER_25", "FROM_25_TO_34", "FROM_35_TO_44", "FROM_45", "TRABUCO", "ARCABUZ", "PISTOL"];
        var leaves = new List<(string Path, JsonElement Value)>();

        void Walk(JsonElement element, string path)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        Walk(property.Value, $"{path}.{property.Name}");
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, $"{path}[]");
                    }

                    break;
                default:
                    leaves.Add((path, element));
                    break;
            }
        }

        Walk(statistics, "$");

        Assert.All(leaves, leaf =>
        {
            switch (leaf.Path)
            {
                case "$.comparsas[].comparsaId":
                    Assert.True(leaf.Value.TryGetGuid(out _), leaf.Path);
                    break;
                case "$.comparsas[].name":
                    Assert.StartsWith("Comparsa Sintética", leaf.Value.GetString(), StringComparison.Ordinal);
                    break;
                case "$.ageBrackets[].bracket" or "$.ownedWeapons.byKind[].kind":
                    Assert.Contains(leaf.Value.GetString(), codes);
                    break;
                default:
                    Assert.True(leaf.Value.ValueKind == JsonValueKind.Number, $"{leaf.Path} is not a count.");
                    break;
            }
        });
    }

    private static int Int(JsonElement element, string property) => element.GetProperty(property).GetInt32();

    private static (int Male, int Female, int Unspecified) Genders(JsonElement counts) =>
        (Int(counts, "male"), Int(counts, "female"), Int(counts, "unspecified"));

    private static async Task<JsonElement> StatisticsAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync("/api/compliance/statistics" + query, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JsonElement>(response);
    }
}
