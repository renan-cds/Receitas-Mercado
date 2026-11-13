namespace ReceitasMercado.Domain;

public sealed record LotInfo(Guid Id, decimal Quantity, DateOnly? Expiry, DateTimeOffset EntryDate);

public sealed record LotAllocation(Guid LotId, decimal Quantity);

public sealed record FefoResult(IReadOnlyList<LotAllocation> Allocations, decimal Shortage);

public static class Fefo
{
    public static IEnumerable<LotInfo> Order(IEnumerable<LotInfo> lots) =>
        lots.OrderBy(l => l.Expiry.HasValue ? 0 : 1)
            .ThenBy(l => l.Expiry)
            .ThenBy(l => l.EntryDate)
            .ThenBy(l => l.Id);

    public static FefoResult Allocate(IEnumerable<LotInfo> lots, decimal quantity)
    {
        var remaining = UnitCatalog.Round(quantity);
        var allocations = new List<LotAllocation>();
        foreach (var lot in Order(lots.Where(l => l.Quantity > 0)))
        {
            if (remaining <= 0) break;
            var take = Math.Min(lot.Quantity, remaining);
            allocations.Add(new LotAllocation(lot.Id, take));
            remaining -= take;
        }
        return new FefoResult(allocations, remaining < 0 ? 0 : remaining);
    }
}
