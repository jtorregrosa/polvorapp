using PolvorApp.Badges.Batches;
using PolvorApp.Badges.Contracts;

namespace PolvorApp.Api.Tests.Badges;

/// <summary>
/// The request rules of a badge sheet (spec: Badge batches, Badge language; design D4): exactly one of a
/// comparsa and a selection, a supported language, a selection of 1 to 200 distinct arquebusiers, and
/// the print order.
/// </summary>
public sealed class BadgeBatchTests
{
    private static readonly Guid Comparsa = Guid.Parse("0190a000-0000-7000-8000-000000000001");

    [Fact]
    public void A_comparsa_batch_is_read_with_its_language()
    {
        var parsed = BadgeBatch.Parse(new BadgeSheetRequest(Comparsa, null, "ca-ES-valencia"));

        Assert.Empty(parsed.Errors);
        Assert.Equal(BadgeBatchKind.Comparsa, parsed.Batch!.Kind);
        Assert.Equal(Comparsa, parsed.Batch.ComparsaId);
        Assert.Equal(BadgeLanguage.Valencian, parsed.Batch.Language);
        Assert.Empty(parsed.Batch.ArquebusierIds);
    }

    [Fact]
    public void A_selection_keeps_the_first_position_of_each_arquebusier()
    {
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        var parsed = BadgeBatch.Parse(new BadgeSheetRequest(null, [ids[0], ids[1], ids[0], ids[2], ids[1]], "en"));

        Assert.Empty(parsed.Errors);
        Assert.Equal(BadgeBatchKind.Selection, parsed.Batch!.Kind);
        Assert.Null(parsed.Batch.ComparsaId);
        Assert.Equal(ids, parsed.Batch.ArquebusierIds);
        Assert.Equal(BadgeLanguage.English, parsed.Batch.Language);
    }

    [Fact]
    public void Both_a_comparsa_and_a_selection_or_neither_is_blocking()
    {
        Assert.Equal("invalid", BadgeBatch.Parse(new BadgeSheetRequest(Comparsa, [Guid.NewGuid()], "es-ES")).Errors["batch"]);
        Assert.Equal("required", BadgeBatch.Parse(new BadgeSheetRequest(null, null, "es-ES")).Errors["batch"]);
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("", "required")]
    [InlineData("fr", "invalid")]
    [InlineData("es", "invalid")]
    [InlineData("ES-es", "invalid")]
    public void The_language_must_be_one_of_the_three(string? language, string reason)
    {
        var parsed = BadgeBatch.Parse(new BadgeSheetRequest(Comparsa, null, language));

        Assert.Null(parsed.Batch);
        Assert.Equal(reason, parsed.Errors["language"]);
    }

    [Fact]
    public void A_selection_holds_1_to_200_arquebusiers()
    {
        var empty = BadgeBatch.Parse(new BadgeSheetRequest(null, [], "es-ES"));
        var tooMany = BadgeBatch.Parse(new BadgeSheetRequest(null, [.. Enumerable.Range(0, 201).Select(_ => Guid.NewGuid())], "es-ES"));
        var most = BadgeBatch.Parse(new BadgeSheetRequest(null, [.. Enumerable.Range(0, 200).Select(_ => Guid.NewGuid())], "es-ES"));

        Assert.Equal("required", empty.Errors["arquebusierIds"]);
        Assert.Equal("tooMany", tooMany.Errors["arquebusierIds"]);
        Assert.Equal(200, most.Batch!.ArquebusierIds.Count);
    }

    [Fact]
    public void Two_hundred_and_one_ids_that_are_200_distinct_arquebusiers_are_accepted()
    {
        Guid[] ids = [.. Enumerable.Range(0, 200).Select(_ => Guid.NewGuid())];

        var parsed = BadgeBatch.Parse(new BadgeSheetRequest(null, [.. ids, ids[0]], "es-ES"));

        Assert.Equal(200, parsed.Batch!.ArquebusierIds.Count);
    }

    [Fact]
    public void Every_rule_is_reported_at_once()
    {
        var parsed = BadgeBatch.Parse(new BadgeSheetRequest(null, null, "fr"));

        Assert.Equal(["batch", "language"], parsed.Errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Badges_are_ordered_by_comparsa_then_surname_then_name_in_spanish_order()
    {
        var ordered = BadgeBatch.Order(
        [
            new BadgeSubject(Guid.NewGuid(), "Comparsa Sintética Sur", "Zapata", "Ana"),
            new BadgeSubject(Guid.NewGuid(), "Comparsa Sintética Sur", "Álvarez", "Luis"),
            new BadgeSubject(Guid.NewGuid(), "comparsa sintética norte", "Pérez", "Begoña"),
            new BadgeSubject(Guid.NewGuid(), "Comparsa Sintética Sur", "Alvarez", "Ana"),
            new BadgeSubject(Guid.NewGuid(), "Comparsa Sintética Sur", "Ñúñez", "Eva"),
        ]);

        Assert.Equal(["Pérez", "Alvarez", "Álvarez", "Ñúñez", "Zapata"], ordered.Select(s => s.LastName));
    }

    [Fact]
    public void Namesakes_are_always_in_the_same_order()
    {
        var first = new BadgeSubject(Guid.Parse("0190a000-0000-7000-8000-000000000001"), "Comparsa Sintética", "Sintético", "Ana");
        var second = new BadgeSubject(Guid.Parse("0190a000-0000-7000-8000-000000000002"), "Comparsa Sintética", "Sintético", "Ana");

        Assert.Equal(BadgeBatch.Order([first, second]), BadgeBatch.Order([second, first]));
    }
}
