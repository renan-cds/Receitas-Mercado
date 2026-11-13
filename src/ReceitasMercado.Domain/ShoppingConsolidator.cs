namespace ReceitasMercado.Domain;

public sealed record NeedLine(Guid? IngredientId, string Text, decimal Quantity, string UnitCode);

public sealed record ConsolidatedItem(
    Guid? IngredientId,
    string Text,
    string UnitCode,
    decimal Needed,
    decimal FromStock,
    decimal ToBuy,
    bool Approximate);

public static class ShoppingConsolidator
{
    public static IReadOnlyList<ConsolidatedItem> Consolidate(
        IEnumerable<NeedLine> lines,
        IEnumerable<StockEntry> stock,
        IReadOnlyDictionary<Guid, string> ingredientNames)
    {
        var stockList = stock.ToList();
        var entries = stockList.ToDictionary(s => s.IngredientId);

        var buckets = new Dictionary<string, Bucket>();
        var order = new List<string>();

        foreach (var line in lines.Where(l => l.Quantity > 0))
        {
            Bucket? bucket;
            if (line.IngredientId is { } ingredientId && entries.TryGetValue(ingredientId, out var entry))
            {
                var groupKey = entry.GroupId.HasValue ? $"g:{entry.GroupId}" : $"i:{ingredientId}";
                var target = entry.RefUnitCode;
                var converted = UnitCatalog.Convert(line.Quantity, line.UnitCode, target);
                var unit = converted.HasValue ? target : line.UnitCode;
                var key = $"{groupKey}|{unit}";
                if (!buckets.TryGetValue(key, out bucket))
                {
                    bucket = new Bucket(ingredientId, ingredientNames.GetValueOrDefault(ingredientId, line.Text), unit);
                    buckets[key] = bucket;
                    order.Add(key);
                }
                bucket.Needed += converted?.Value ?? line.Quantity;
                bucket.Approximate |= converted?.Approximate ?? false;
            }
            else
            {
                var key = $"t:{TextNormalizer.Normalize(line.Text)}|{line.UnitCode}";
                if (!buckets.TryGetValue(key, out bucket))
                {
                    bucket = new Bucket(null, line.Text.Trim(), line.UnitCode);
                    buckets[key] = bucket;
                    order.Add(key);
                }
                bucket.Needed += line.Quantity;
            }
        }

        var requirements = new List<Requirement>();
        for (var i = 0; i < order.Count; i++)
        {
            var b = buckets[order[i]];
            requirements.Add(new Requirement(i, b.IngredientId, b.Needed, b.Unit, false,
                b.IngredientId.HasValue ? Confidence.Confirmada : Confidence.Incerta));
        }

        var plans = StockPlanner.Plan(requirements, stockList);
        var result = new List<ConsolidatedItem>();
        for (var i = 0; i < order.Count; i++)
        {
            var b = buckets[order[i]];
            var plan = plans[i];
            var needed = UnitCatalog.Round(b.Needed);
            var covered = UnitCatalog.Round(Math.Min(plan.Covered, b.Needed));
            result.Add(new ConsolidatedItem(
                b.IngredientId, b.Text, b.Unit, needed, covered,
                UnitCatalog.Round(Math.Max(0m, needed - covered)), b.Approximate));
        }
        return result;
    }

    private sealed class Bucket(Guid? ingredientId, string text, string unit)
    {
        public Guid? IngredientId { get; } = ingredientId;
        public string Text { get; } = text;
        public string Unit { get; } = unit;
        public decimal Needed { get; set; }
        public bool Approximate { get; set; }
    }
}
