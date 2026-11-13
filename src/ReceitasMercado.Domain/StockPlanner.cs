namespace ReceitasMercado.Domain;

public enum Confidence { Confirmada, Inferida, Incerta }

public enum LineStatus { Ok, Approximate, Insufficient, Uncertain, NoQuantity, IncompatibleUnit }

public enum CompatibilityState { PodePreparar, FaltamIngredientes, VerificarIngredientes, Rascunho }

public sealed record Requirement(
    int Index,
    Guid? IngredientId,
    decimal? Quantity,
    string? UnitCode,
    bool Optional,
    Confidence Confidence);

public sealed record StockEntry(
    Guid IngredientId,
    string RefUnitCode,
    decimal Total,
    Guid? GroupId,
    DateOnly? NearestExpiry);

public sealed record ConsumptionPart(
    Guid IngredientId,
    decimal QuantityInRefUnit,
    decimal QuantityInRecipeUnit,
    bool Approximate,
    bool IsSubstitute);

public sealed record LinePlan(
    int Index,
    bool Optional,
    LineStatus Status,
    string UnitCode,
    decimal Needed,
    decimal Covered,
    decimal Shortage,
    bool UsedEquivalent,
    IReadOnlyList<ConsumptionPart> Parts);

public sealed record CompatibilityResult(
    CompatibilityState State,
    IReadOnlyList<LinePlan> Lines,
    int RequiredTotal,
    int RequiredOk,
    decimal Percent);

public static class StockPlanner
{
    private const decimal Epsilon = 0.0005m;

    public const string PercentDisclaimer =
        "Compatibilidade estimada. Itens sem correspondência confirmada e medidas aproximadas não entram como disponíveis.";

    public static CompatibilityResult Evaluate(
        IReadOnlyList<Requirement> requirements,
        IEnumerable<StockEntry> stock,
        bool isDraft = false)
    {
        var lines = Plan(requirements, stock);
        var required = lines.Where(l => !l.Optional).ToList();
        var requiredOk = required.Count(l => l.Status == LineStatus.Ok);
        var percent = required.Count == 0 ? 0m : decimal.Round(100m * requiredOk / required.Count, 1);

        CompatibilityState state;
        if (isDraft) state = CompatibilityState.Rascunho;
        else if (required.Count == 0) state = CompatibilityState.VerificarIngredientes;
        else if (required.Any(l => l.Status is LineStatus.Uncertain or LineStatus.NoQuantity
                     or LineStatus.IncompatibleUnit or LineStatus.Approximate))
            state = CompatibilityState.VerificarIngredientes;
        else if (required.Any(l => l.Status == LineStatus.Insufficient))
            state = CompatibilityState.FaltamIngredientes;
        else state = CompatibilityState.PodePreparar;

        return new CompatibilityResult(state, lines, required.Count, requiredOk, percent);
    }

    /// <summary>
    /// Consome o estoque de forma sequencial: obrigatórios primeiro, opcionais depois,
    /// para que um opcional nunca reserve estoque de um obrigatório.
    /// </summary>
    public static IReadOnlyList<LinePlan> Plan(IReadOnlyList<Requirement> requirements, IEnumerable<StockEntry> stock)
    {
        var entries = stock.ToDictionary(s => s.IngredientId);
        var remaining = entries.ToDictionary(kv => kv.Key, kv => kv.Value.Total);
        var byGroup = entries.Values
            .Where(s => s.GroupId.HasValue)
            .GroupBy(s => s.GroupId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var results = new Dictionary<int, LinePlan>();
        foreach (var req in requirements.Where(r => !r.Optional).Concat(requirements.Where(r => r.Optional)))
            results[req.Index] = PlanLine(req, entries, remaining, byGroup);

        return requirements.Select(r => results[r.Index]).ToList();
    }

    private static LinePlan PlanLine(
        Requirement req,
        Dictionary<Guid, StockEntry> entries,
        Dictionary<Guid, decimal> remaining,
        Dictionary<Guid, List<StockEntry>> byGroup)
    {
        var unit = req.UnitCode ?? "UN";
        var needed = req.Quantity ?? 0m;

        LinePlan Fail(LineStatus status) =>
            new(req.Index, req.Optional, status, unit, needed, 0m, needed, false, []);

        if (req.Quantity is null or <= 0) return Fail(LineStatus.NoQuantity);
        if (req.IngredientId is null || req.Confidence != Confidence.Confirmada) return Fail(LineStatus.Uncertain);
        if (!entries.TryGetValue(req.IngredientId.Value, out var primary)) return Fail(LineStatus.Uncertain);
        if (!UnitCatalog.Exists(unit)) return Fail(LineStatus.IncompatibleUnit);

        var candidates = new List<StockEntry> { primary };
        if (primary.GroupId.HasValue && byGroup.TryGetValue(primary.GroupId.Value, out var members))
        {
            candidates.AddRange(members
                .Where(m => m.IngredientId != primary.IngredientId)
                .OrderBy(m => m.NearestExpiry.HasValue ? 0 : 1)
                .ThenBy(m => m.NearestExpiry)
                .ThenBy(m => m.IngredientId));
        }

        var remainingNeed = needed;
        var parts = new List<ConsumptionPart>();
        var anyConvertible = false;

        foreach (var candidate in candidates)
        {
            if (remainingNeed <= Epsilon) break;
            var availableRef = remaining[candidate.IngredientId];
            var availableInRecipe = UnitCatalog.Convert(availableRef, candidate.RefUnitCode, unit);
            if (availableInRecipe is null) continue;
            anyConvertible = true;
            if (availableRef <= 0) continue;

            var takeRecipe = Math.Min(remainingNeed, availableInRecipe.Value.Value);
            var takeRef = takeRecipe >= availableInRecipe.Value.Value
                ? availableRef
                : UnitCatalog.Round(UnitCatalog.Convert(takeRecipe, unit, candidate.RefUnitCode)!.Value.Value);
            takeRef = Math.Min(takeRef, availableRef);
            if (takeRef <= 0) continue;

            remaining[candidate.IngredientId] = availableRef - takeRef;
            remainingNeed -= takeRecipe;
            parts.Add(new ConsumptionPart(
                candidate.IngredientId,
                takeRef,
                takeRecipe,
                availableInRecipe.Value.Approximate,
                candidate.IngredientId != primary.IngredientId));
        }

        if (!anyConvertible) return Fail(LineStatus.IncompatibleUnit);

        var covered = parts.Sum(p => p.QuantityInRecipeUnit);
        var shortage = Math.Max(0m, needed - covered);
        var usedEquivalent = parts.Any(p => p.IsSubstitute);

        LineStatus status;
        if (shortage > Epsilon) status = LineStatus.Insufficient;
        else if (parts.Any(p => p.Approximate)) status = LineStatus.Approximate;
        else status = LineStatus.Ok;

        return new LinePlan(req.Index, req.Optional, status, unit, needed, covered,
            shortage > Epsilon ? shortage : 0m, usedEquivalent, parts);
    }
}
