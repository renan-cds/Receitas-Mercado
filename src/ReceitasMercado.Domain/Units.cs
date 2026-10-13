namespace ReceitasMercado.Domain;

public enum UnitDimension { Massa, Volume, Unidade, Culinaria }

public sealed record UnitDef(
    string Code,
    string Name,
    string Abbreviation,
    UnitDimension Dimension,
    bool Approximate,
    string BaseCode,
    decimal ToBase);

public readonly record struct Conversion(decimal Value, bool Approximate);

public sealed record UnitConversionRow(string From, string To, decimal Factor, bool Approximate);

public static class UnitCatalog
{
    public static readonly IReadOnlyList<UnitDef> All =
    [
        new("G", "grama", "g", UnitDimension.Massa, false, "G", 1m),
        new("KG", "quilograma", "kg", UnitDimension.Massa, false, "G", 1000m),
        new("ML", "mililitro", "ml", UnitDimension.Volume, false, "ML", 1m),
        new("L", "litro", "L", UnitDimension.Volume, false, "ML", 1000m),
        new("UN", "unidade", "un", UnitDimension.Unidade, false, "UN", 1m),
        new("XICARA", "xícara (240 ml)", "xíc", UnitDimension.Culinaria, true, "ML", 240m),
        new("COLHER_SOPA", "colher de sopa", "col. sopa", UnitDimension.Culinaria, true, "ML", 15m),
        new("COLHER_CHA", "colher de chá", "col. chá", UnitDimension.Culinaria, true, "ML", 5m),
    ];

    private static readonly Dictionary<string, UnitDef> ByCode =
        All.ToDictionary(u => u.Code, StringComparer.OrdinalIgnoreCase);

    public static UnitDef? Find(string? code) =>
        code is not null && ByCode.TryGetValue(code, out var unit) ? unit : null;

    public static bool Exists(string? code) => Find(code) is not null;

    public static IEnumerable<UnitConversionRow> Conversions()
    {
        foreach (var unit in All.Where(u => u.Code != u.BaseCode))
        {
            yield return new UnitConversionRow(unit.Code, unit.BaseCode, unit.ToBase, unit.Approximate);
            yield return new UnitConversionRow(unit.BaseCode, unit.Code, 1m / unit.ToBase, unit.Approximate);
        }
    }

    /// <summary>
    /// Converte entre unidades da mesma base. Retorna null quando incompatíveis
    /// (por exemplo massa para volume, que exigiria densidade).
    /// </summary>
    public static Conversion? Convert(decimal quantity, string from, string to)
    {
        var f = Find(from);
        var t = Find(to);
        if (f is null || t is null) return null;
        if (string.Equals(f.Code, t.Code, StringComparison.OrdinalIgnoreCase)) return new Conversion(quantity, false);
        if (f.BaseCode != t.BaseCode) return null;
        return new Conversion(quantity * f.ToBase / t.ToBase, f.Approximate || t.Approximate);
    }

    public static decimal Round(decimal quantity) =>
        decimal.Round(quantity, 3, MidpointRounding.AwayFromZero);
}
