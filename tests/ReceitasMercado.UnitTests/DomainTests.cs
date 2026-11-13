using System.Net;
using ReceitasMercado.Domain;

namespace ReceitasMercado.UnitTests;

public class UnitConversionTests
{
    [Theory]
    [InlineData(1500, "G", "KG", 1.5, false)]
    [InlineData(2, "KG", "G", 2000, false)]
    [InlineData(1.5, "L", "ML", 1500, false)]
    [InlineData(1, "XICARA", "ML", 240, true)]
    [InlineData(2, "COLHER_SOPA", "ML", 30, true)]
    [InlineData(1, "XICARA", "COLHER_SOPA", 16, true)]
    public void Converts_within_same_dimension(double qty, string from, string to, double expected, bool approx)
    {
        var result = UnitCatalog.Convert((decimal)qty, from, to);
        Assert.NotNull(result);
        Assert.Equal((decimal)expected, result!.Value.Value);
        Assert.Equal(approx, result.Value.Approximate);
    }

    [Theory]
    [InlineData("G", "ML")]
    [InlineData("KG", "L")]
    [InlineData("UN", "G")]
    [InlineData("UN", "ML")]
    [InlineData("G", "NOPE")]
    public void Refuses_incompatible_dimensions(string from, string to) =>
        Assert.Null(UnitCatalog.Convert(1, from, to));

    [Fact]
    public void Same_unit_is_exact_even_for_culinary() =>
        Assert.False(UnitCatalog.Convert(3, "XICARA", "XICARA")!.Value.Approximate);

    [Fact]
    public void Seeds_conversion_rows_for_every_non_base_unit() =>
        Assert.Equal(10, UnitCatalog.Conversions().Count());
}

public class NormalizerTests
{
    [Theory]
    [InlineData("  Açúcar   Refinado ", "acucar refinado")]
    [InlineData("FEIJÃO", "feijao")]
    [InlineData(null, "")]
    public void Normalizes(string? input, string expected) =>
        Assert.Equal(expected, TextNormalizer.Normalize(input));
}

public class FefoTests
{
    private static LotInfo Lot(string id, decimal qty, string? expiry, int entryDay) =>
        new(Guid.Parse($"00000000-0000-0000-0000-{id.PadLeft(12, '0')}"), qty,
            expiry is null ? null : DateOnly.Parse(expiry), new DateTimeOffset(2026, 1, entryDay, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Consumes_earliest_expiry_first_and_nulls_last()
    {
        var lots = new[] { Lot("1", 5, null, 1), Lot("2", 5, "2026-03-01", 2), Lot("3", 5, "2026-02-01", 3) };
        var result = Fefo.Allocate(lots, 7);
        Assert.Equal(0, result.Shortage);
        Assert.Equal(2, result.Allocations.Count);
        Assert.Equal(lots[2].Id, result.Allocations[0].LotId);
        Assert.Equal(5, result.Allocations[0].Quantity);
        Assert.Equal(lots[1].Id, result.Allocations[1].LotId);
        Assert.Equal(2, result.Allocations[1].Quantity);
    }

    [Fact]
    public void Ties_break_by_entry_date()
    {
        var lots = new[] { Lot("1", 5, "2026-02-01", 10), Lot("2", 5, "2026-02-01", 2) };
        Assert.Equal(lots[1].Id, Fefo.Allocate(lots, 1).Allocations[0].LotId);
    }

    [Fact]
    public void Reports_shortage_and_never_allocates_more_than_available()
    {
        var lots = new[] { Lot("1", 3, null, 1), Lot("2", 2, null, 2) };
        var result = Fefo.Allocate(lots, 8);
        Assert.Equal(3, result.Shortage);
        Assert.Equal(5, result.Allocations.Sum(a => a.Quantity));
    }

    [Fact]
    public void Ignores_empty_lots()
    {
        var lots = new[] { Lot("1", 0, "2026-01-01", 1), Lot("2", 4, null, 2) };
        Assert.Single(Fefo.Allocate(lots, 1).Allocations);
    }
}

public class CompatibilityTests
{
    private static readonly Guid Flour = Guid.NewGuid();
    private static readonly Guid Sugar = Guid.NewGuid();
    private static readonly Guid Milk = Guid.NewGuid();
    private static readonly Guid Wheat2 = Guid.NewGuid();
    private static readonly Guid Group = Guid.NewGuid();

    private static StockEntry S(Guid id, string unit, decimal total, Guid? group = null, string? expiry = null) =>
        new(id, unit, total, group, expiry is null ? null : DateOnly.Parse(expiry));

    private static Requirement R(int i, Guid? ing, decimal? qty, string? unit, bool optional = false,
        Confidence c = Confidence.Confirmada) => new(i, ing, qty, unit, optional, c);

    [Fact]
    public void Can_prepare_when_everything_is_available()
    {
        var result = StockPlanner.Evaluate(
            [R(0, Flour, 500, "G"), R(1, Sugar, 1, "KG")],
            [S(Flour, "G", 1000), S(Sugar, "KG", 2)]);
        Assert.Equal(CompatibilityState.PodePreparar, result.State);
        Assert.Equal(100m, result.Percent);
    }

    [Fact]
    public void Converts_between_compatible_units()
    {
        var result = StockPlanner.Evaluate([R(0, Flour, 0.5m, "KG")], [S(Flour, "G", 500)]);
        Assert.Equal(CompatibilityState.PodePreparar, result.State);
    }

    [Fact]
    public void Missing_when_stock_is_insufficient_and_everything_confirmed()
    {
        var result = StockPlanner.Evaluate([R(0, Flour, 500, "G")], [S(Flour, "G", 200)]);
        Assert.Equal(CompatibilityState.FaltamIngredientes, result.State);
        Assert.Equal(300, result.Lines[0].Shortage);
        Assert.Equal(0m, result.Percent);
    }

    [Fact]
    public void Uncertain_match_is_never_counted_as_available_even_with_full_stock()
    {
        var result = StockPlanner.Evaluate(
            [R(0, Flour, 100, "G", c: Confidence.Incerta), R(1, Sugar, 100, "G")],
            [S(Flour, "G", 9999), S(Sugar, "G", 500)]);
        Assert.Equal(CompatibilityState.VerificarIngredientes, result.State);
        Assert.Equal(LineStatus.Uncertain, result.Lines[0].Status);
        Assert.Equal(50m, result.Percent);
    }

    [Fact]
    public void Inferred_match_requires_confirmation()
    {
        var result = StockPlanner.Evaluate([R(0, Flour, 1, "G", c: Confidence.Inferida)], [S(Flour, "G", 10)]);
        Assert.Equal(CompatibilityState.VerificarIngredientes, result.State);
    }

    [Fact]
    public void Unmatched_ingredient_is_uncertain()
    {
        var result = StockPlanner.Evaluate([R(0, null, 1, "G")], [S(Flour, "G", 10)]);
        Assert.Equal(LineStatus.Uncertain, result.Lines[0].Status);
    }

    [Fact]
    public void Optional_ingredients_do_not_block_preparation()
    {
        var result = StockPlanner.Evaluate(
            [R(0, Flour, 100, "G"), R(1, Sugar, 100, "G", optional: true)],
            [S(Flour, "G", 500), S(Sugar, "G", 0)]);
        Assert.Equal(CompatibilityState.PodePreparar, result.State);
        Assert.Equal(LineStatus.Insufficient, result.Lines[1].Status);
    }

    [Fact]
    public void Optional_never_steals_stock_from_required_lines()
    {
        var result = StockPlanner.Evaluate(
            [R(0, Flour, 400, "G", optional: true), R(1, Flour, 400, "G")],
            [S(Flour, "G", 500)]);
        Assert.Equal(LineStatus.Ok, result.Lines[1].Status);
        Assert.Equal(LineStatus.Insufficient, result.Lines[0].Status);
    }

    [Fact]
    public void Same_ingredient_in_two_lines_accumulates()
    {
        var result = StockPlanner.Evaluate(
            [R(0, Flour, 300, "G"), R(1, Flour, 300, "G")],
            [S(Flour, "G", 500)]);
        Assert.Equal(CompatibilityState.FaltamIngredientes, result.State);
        Assert.Equal(100, result.Lines[1].Shortage);
    }

    [Fact]
    public void Approximate_conversion_requires_checking()
    {
        var result = StockPlanner.Evaluate([R(0, Milk, 1, "XICARA")], [S(Milk, "ML", 1000)]);
        Assert.Equal(CompatibilityState.VerificarIngredientes, result.State);
        Assert.Equal(LineStatus.Approximate, result.Lines[0].Status);
        Assert.Equal(240, result.Lines[0].Parts[0].QuantityInRefUnit);
    }

    [Fact]
    public void Mass_to_volume_is_incompatible_and_needs_checking()
    {
        var result = StockPlanner.Evaluate([R(0, Milk, 200, "G")], [S(Milk, "ML", 1000)]);
        Assert.Equal(LineStatus.IncompatibleUnit, result.Lines[0].Status);
        Assert.Equal(CompatibilityState.VerificarIngredientes, result.State);
    }

    [Fact]
    public void Missing_quantity_needs_checking()
    {
        var result = StockPlanner.Evaluate([R(0, Flour, null, null)], [S(Flour, "G", 100)]);
        Assert.Equal(LineStatus.NoQuantity, result.Lines[0].Status);
    }

    [Fact]
    public void Equivalent_ingredient_covers_the_gap()
    {
        var result = StockPlanner.Evaluate(
            [R(0, Flour, 500, "G")],
            [S(Flour, "G", 200, Group), S(Wheat2, "KG", 1, Group)]);
        Assert.Equal(CompatibilityState.PodePreparar, result.State);
        Assert.True(result.Lines[0].UsedEquivalent);
        Assert.Equal(2, result.Lines[0].Parts.Count);
        Assert.Equal(0.3m, result.Lines[0].Parts[1].QuantityInRefUnit);
    }

    [Fact]
    public void Draft_recipes_are_never_preparable()
    {
        var result = StockPlanner.Evaluate([R(0, Flour, 1, "G")], [S(Flour, "G", 10)], isDraft: true);
        Assert.Equal(CompatibilityState.Rascunho, result.State);
    }

    [Fact]
    public void Recipe_without_required_lines_needs_checking()
    {
        Assert.Equal(CompatibilityState.VerificarIngredientes, StockPlanner.Evaluate([], []).State);
    }
}

public class ShoppingConsolidatorTests
{
    private static readonly Guid Flour = Guid.NewGuid();
    private static readonly Guid Flour2 = Guid.NewGuid();
    private static readonly Guid Group = Guid.NewGuid();
    private static readonly Guid Milk = Guid.NewGuid();

    private static readonly Dictionary<Guid, string> Names = new()
    {
        [Flour] = "Farinha de trigo", [Flour2] = "Farinha integral", [Milk] = "Leite",
    };

    [Fact]
    public void Sums_same_ingredient_converting_units_and_discounts_stock()
    {
        var items = ShoppingConsolidator.Consolidate(
            [new(Flour, "farinha", 500, "G"), new(Flour, "farinha", 0.5m, "KG")],
            [new StockEntry(Flour, "G", 300, null, null)], Names);
        var item = Assert.Single(items);
        Assert.Equal(1000, item.Needed);
        Assert.Equal(300, item.FromStock);
        Assert.Equal(700, item.ToBuy);
        Assert.Equal("G", item.UnitCode);
    }

    [Fact]
    public void Consolidates_equivalent_ingredients_in_one_line()
    {
        var items = ShoppingConsolidator.Consolidate(
            [new(Flour, "farinha", 200, "G"), new(Flour2, "farinha integral", 300, "G")],
            [new StockEntry(Flour, "G", 0, Group, null), new StockEntry(Flour2, "G", 100, Group, null)], Names);
        var item = Assert.Single(items);
        Assert.Equal(500, item.Needed);
        Assert.Equal(400, item.ToBuy);
    }

    [Fact]
    public void Keeps_incompatible_units_in_separate_lines()
    {
        var items = ShoppingConsolidator.Consolidate(
            [new(Milk, "leite", 200, "ML"), new(Milk, "leite", 100, "G")],
            [new StockEntry(Milk, "ML", 0, null, null)], Names);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void Free_text_items_are_summed_by_text_and_unit_and_always_bought()
    {
        var items = ShoppingConsolidator.Consolidate(
            [new(null, "Cravo", 2, "UN"), new(null, "cravo", 3, "UN")], [], Names);
        var item = Assert.Single(items);
        Assert.Equal(5, item.ToBuy);
    }

    [Fact]
    public void Nothing_to_buy_when_stock_covers()
    {
        var items = ShoppingConsolidator.Consolidate(
            [new(Flour, "farinha", 100, "G")], [new StockEntry(Flour, "G", 500, null, null)], Names);
        Assert.Equal(0, items[0].ToBuy);
    }
}

public class IngredientParserTests
{
    [Theory]
    [InlineData("200 g de farinha de trigo", 200, "G", "farinha de trigo")]
    [InlineData("200g farinha", 200, "G", "farinha")]
    [InlineData("1 1/2 xícara de açúcar", 1.5, "XICARA", "açúcar")]
    [InlineData("2 colheres de sopa de azeite", 2, "COLHER_SOPA", "azeite")]
    [InlineData("1 colher de chá de sal", 1, "COLHER_CHA", "sal")]
    [InlineData("3 ovos", 3, "UN", "ovos")]
    [InlineData("1/2 cebola", 0.5, "UN", "cebola")]
    [InlineData("½ xícara de leite", 0.5, "XICARA", "leite")]
    [InlineData("500 ml de leite integral", 500, "ML", "leite integral")]
    [InlineData("1,5 kg de batata, descascada", 1.5, "KG", "batata")]
    [InlineData("2 tbsp olive oil", 2, "COLHER_SOPA", "olive oil")]
    [InlineData("1 cup flour", 1, "XICARA", "flour")]
    [InlineData("3 dentes de alho (picados)", 3, "UN", "dentes de alho")]
    public void Parses_quantity_unit_and_name(string text, double qty, string unit, string name)
    {
        var parsed = IngredientTextParser.Parse(text);
        Assert.Equal((decimal)qty, parsed.Quantity!.Value, 3);
        Assert.Equal(unit, parsed.UnitCode);
        Assert.Equal(name, parsed.Name);
    }

    [Fact]
    public void Salt_to_taste_is_optional_without_quantity()
    {
        var parsed = IngredientTextParser.Parse("sal a gosto");
        Assert.Null(parsed.Quantity);
        Assert.True(parsed.Optional);
        Assert.Equal("sal", parsed.Name);
    }

    [Fact]
    public void Plain_name_has_no_quantity()
    {
        var parsed = IngredientTextParser.Parse("manjericão fresco");
        Assert.Null(parsed.Quantity);
        Assert.Null(parsed.UnitCode);
    }
}

public class IngredientMatcherTests
{
    private static readonly IngredientCandidate Flour = new(Guid.NewGuid(), "Farinha de trigo");
    private static readonly IngredientCandidate Egg = new(Guid.NewGuid(), "Ovo");
    private static readonly IngredientCandidate Whole = new(Guid.NewGuid(), "Farinha integral");

    [Fact]
    public void Exact_match_ignoring_accents_case_plural_is_inferred()
    {
        var m = IngredientMatcher.Match("Ovos", [Flour, Egg]);
        Assert.Equal(Egg.Id, m.BestId);
        Assert.Equal(Confidence.Inferida, m.Confidence);
    }

    [Fact]
    public void Partial_overlap_lists_candidates_but_stays_uncertain()
    {
        var m = IngredientMatcher.Match("farinha", [Flour, Whole]);
        Assert.Null(m.BestId);
        Assert.Equal(Confidence.Incerta, m.Confidence);
        Assert.Equal(2, m.OtherCandidates.Count);
    }

    [Fact]
    public void Unknown_ingredient_has_no_candidates()
    {
        var m = IngredientMatcher.Match("trufa negra", [Flour, Egg]);
        Assert.Null(m.BestId);
        Assert.Empty(m.OtherCandidates);
    }
}

public class UrlGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("2002:7f00:0001::1")]
    public void Blocks_internal_addresses(string ip) => Assert.True(UrlGuard.IsBlocked(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.32.0.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Allows_public_addresses(string ip) => Assert.False(UrlGuard.IsBlocked(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("http://example.com/receita")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://localhost/")]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("https://example.com:8443/")]
    [InlineData("ftp://example.com/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://servico.internal/")]
    [InlineData("")]
    [InlineData("não é url")]
    public void Rejects_unsafe_urls(string url) => Assert.NotNull(UrlGuard.ValidateUrl(url, out _));

    [Fact]
    public void Accepts_plain_https_url()
    {
        Assert.Null(UrlGuard.ValidateUrl("https://www.exemplo.com.br/receita/bolo", out var uri));
        Assert.Equal("www.exemplo.com.br", uri!.Host);
    }
}

public class RecipeScalerTests
{
    [Fact]
    public void Scales_linearly() => Assert.Equal(750m, RecipeScaler.Scale(500, 4, 6));

    [Fact]
    public void Rejects_non_positive_servings() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeScaler.Scale(1, 0, 2));
}
