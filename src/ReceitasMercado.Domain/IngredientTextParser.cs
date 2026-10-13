using System.Globalization;
using System.Text.RegularExpressions;

namespace ReceitasMercado.Domain;

public sealed record ParsedIngredient(
    string Original,
    decimal? Quantity,
    string? UnitCode,
    string Name,
    bool Optional);

public static partial class IngredientTextParser
{
    private static readonly Dictionary<char, decimal> VulgarFractions = new()
    {
        ['½'] = 0.5m, ['¼'] = 0.25m, ['¾'] = 0.75m, ['⅓'] = 1m / 3m, ['⅔'] = 2m / 3m,
        ['⅛'] = 0.125m,
    };

    private static readonly (string[] Aliases, string Code)[] UnitAliases =
    [
        (["g", "gr", "grama", "gramas", "gram", "grams"], "G"),
        (["kg", "kilo", "kilos", "quilo", "quilos", "quilograma", "quilogramas", "kilogram", "kilograms"], "KG"),
        (["ml", "mililitro", "mililitros", "millilitre", "milliliter", "millilitres", "milliliters"], "ML"),
        (["l", "lt", "litro", "litros", "liter", "litre", "liters", "litres"], "L"),
        (["xic", "xicara", "xicaras", "cup", "cups"], "XICARA"),
        (["tbsp", "tablespoon", "tablespoons", "tbs"], "COLHER_SOPA"),
        (["tsp", "teaspoon", "teaspoons"], "COLHER_CHA"),
        (["un", "unid", "unidade", "unidades"], "UN"),
    ];

    private static readonly Dictionary<string, string> UnitLookup = UnitAliases
        .SelectMany(u => u.Aliases.Select(a => (Alias: a, u.Code)))
        .ToDictionary(x => x.Alias, x => x.Code);

    private static readonly string[] OptionalMarkers =
        ["a gosto", "to taste", "pitada", "pinch", "opcional", "optional", "para decorar", "para servir"];

    [GeneratedRegex(@"^\s*(?<q>\d+\s+\d+/\d+|\d+/\d+|\d+(?:[.,]\d+)?\s*[½¼¾⅓⅔⅛]?|[½¼¾⅓⅔⅛])", RegexOptions.CultureInvariant)]
    private static partial Regex QuantityRegex();

    [GeneratedRegex(@"\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ParenthesesRegex();

    [GeneratedRegex(@"^(?<range>\s*(?:-|a|ou|to)\s*\d+(?:[.,]\d+)?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex RangeRegex();

    public static ParsedIngredient Parse(string? text)
    {
        var original = (text ?? string.Empty).Trim();
        var working = ParenthesesRegex().Replace(original, " ").Trim();
        var optional = OptionalMarkers.Any(m => TextNormalizer.Normalize(working).Contains(m));

        decimal? quantity = null;
        var match = QuantityRegex().Match(working);
        if (match.Success)
        {
            quantity = ParseQuantity(match.Groups["q"].Value);
            working = working[match.Length..];
            var range = RangeRegex().Match(working);
            if (range.Success) working = working[range.Length..];
        }

        string? unitCode = null;
        var (rest, unit) = ExtractUnit(working.TrimStart());
        if (unit is not null)
        {
            unitCode = unit;
            working = rest;
        }
        else
        {
            working = working.TrimStart();
        }

        working = StripLeadingConnector(working);
        var commaIndex = working.IndexOf(',');
        if (commaIndex > 0) working = working[..commaIndex];
        var name = CleanName(working);
        if (string.IsNullOrWhiteSpace(name)) name = original;

        if (quantity.HasValue && unitCode is null) unitCode = "UN";
        return new ParsedIngredient(original, quantity, unitCode, name, optional);
    }

    private static (string Remaining, string? Unit) ExtractUnit(string text)
    {
        if (text.Length == 0) return (text, null);

        var spoon = Regex.Match(text,
            @"^colhere?s?(?:\s+de)?\s+(?<k>sopa|ch[aá]|cha)\b\.?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (spoon.Success)
        {
            var kind = TextNormalizer.Normalize(spoon.Groups["k"].Value);
            return (text[spoon.Length..].TrimStart(), kind == "sopa" ? "COLHER_SOPA" : "COLHER_CHA");
        }

        var wordMatch = Regex.Match(text, @"^(?<w>[\p{L}]+)\.?(?=\s|$|,)", RegexOptions.CultureInvariant);
        if (wordMatch.Success)
        {
            var key = TextNormalizer.Normalize(wordMatch.Groups["w"].Value);
            if (UnitLookup.TryGetValue(key, out var code))
                return (text[wordMatch.Length..].TrimStart(), code);
        }

        var glued = Regex.Match(text, @"^(?<w>kg|g|ml|l)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (glued.Success)
            return (text[glued.Length..].TrimStart(), UnitLookup[glued.Groups["w"].Value.ToLowerInvariant()]);

        return (text, null);
    }

    private static string StripLeadingConnector(string text)
    {
        var m = Regex.Match(text, @"^(de|da|do|das|dos|of)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return m.Success ? text[m.Length..].TrimStart() : text;
    }

    private static string CleanName(string text)
    {
        var cleaned = Regex.Replace(text, @"\s+", " ").Trim().Trim('.', ';', ':', '-', ' ');
        foreach (var marker in OptionalMarkers)
        {
            var idx = TextNormalizer.Normalize(cleaned).IndexOf(marker, StringComparison.Ordinal);
            if (idx > 0 && TextNormalizer.Normalize(cleaned).Length == cleaned.Length)
                cleaned = cleaned[..idx].Trim();
        }
        return cleaned;
    }

    private static decimal? ParseQuantity(string raw)
    {
        raw = raw.Trim();
        decimal total = 0m;
        foreach (var (ch, value) in VulgarFractions)
        {
            if (raw.Contains(ch))
            {
                total += value;
                raw = raw.Replace(ch.ToString(), string.Empty).Trim();
            }
        }
        if (raw.Length == 0) return total;

        var mixed = Regex.Match(raw, @"^(\d+)\s+(\d+)/(\d+)$");
        if (mixed.Success)
            return total + int.Parse(mixed.Groups[1].Value) + Fraction(mixed.Groups[2].Value, mixed.Groups[3].Value);

        var fraction = Regex.Match(raw, @"^(\d+)/(\d+)$");
        if (fraction.Success) return total + Fraction(fraction.Groups[1].Value, fraction.Groups[2].Value);

        return decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? total + number
            : null;
    }

    private static decimal Fraction(string numerator, string denominator)
    {
        var d = decimal.Parse(denominator, CultureInfo.InvariantCulture);
        return d == 0 ? 0 : decimal.Parse(numerator, CultureInfo.InvariantCulture) / d;
    }
}
