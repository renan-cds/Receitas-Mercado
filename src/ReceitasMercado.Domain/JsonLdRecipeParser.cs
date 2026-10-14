using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace ReceitasMercado.Domain;

/// <summary>
/// Extrai apenas dados estruturados (schema.org/Recipe). O HTML original nunca é retido.
/// </summary>
public static partial class JsonLdRecipeParser
{
    private const int MaxIngredients = 100;
    private const int MaxSteps = 100;

    [GeneratedRegex(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(?<json>.*?)</script>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 32,
    };

    public static ImportedRecipe? Parse(string? html, string? sourceUrl = null)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        foreach (Match script in ScriptRegex().Matches(html))
        {
            try
            {
                using var doc = JsonDocument.Parse(script.Groups["json"].Value.Trim(), JsonOptions);
                var node = FindRecipe(doc.RootElement, 0);
                if (node is { } element && Map(element, sourceUrl) is { } recipe) return recipe;
            }
            catch (JsonException)
            {
            }
        }
        return null;
    }

    private static JsonElement? FindRecipe(JsonElement element, int depth)
    {
        if (depth > 6) return null;
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (FindRecipe(item, depth + 1) is { } found) return found;
                return null;
            case JsonValueKind.Object:
                if (IsRecipeType(element)) return element;
                if (element.TryGetProperty("@graph", out var graph)) return FindRecipe(graph, depth + 1);
                return null;
            default:
                return null;
        }
    }

    private static bool IsRecipeType(JsonElement element)
    {
        if (!element.TryGetProperty("@type", out var type)) return false;
        return type.ValueKind switch
        {
            JsonValueKind.String => string.Equals(type.GetString(), "Recipe", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => type.EnumerateArray().Any(t =>
                t.ValueKind == JsonValueKind.String && string.Equals(t.GetString(), "Recipe", StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    private static ImportedRecipe? Map(JsonElement r, string? sourceUrl)
    {
        var title = Clean(Str(r, "name"), 200);
        if (string.IsNullOrWhiteSpace(title)) return null;

        var ingredients = Strings(r, "recipeIngredient")
            .Concat(Strings(r, "ingredients"))
            .Select(s => Clean(s, 300))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .Take(MaxIngredients)
            .ToList();
        if (ingredients.Count == 0) return null;

        var steps = new List<string>();
        if (r.TryGetProperty("recipeInstructions", out var instructions)) CollectSteps(instructions, steps, 0);

        var tags = Strings(r, "keywords").SelectMany(k => k.Split(','))
            .Concat(Strings(r, "recipeCategory"))
            .Concat(Strings(r, "recipeCuisine"))
            .Select(t => TextNormalizer.Normalize(Clean(t, 40)))
            .Where(t => t.Length > 0)
            .Distinct()
            .Take(10)
            .ToList();

        return new ImportedRecipe(
            title!,
            Clean(Str(r, "description"), 1000),
            ParseServings(r),
            ParseMinutes(r),
            ParseImage(r),
            ParseAuthor(r),
            tags,
            ingredients,
            steps.Take(MaxSteps).ToList(),
            sourceUrl);
    }

    private static void CollectSteps(JsonElement element, List<string> steps, int depth)
    {
        if (depth > 4) return;
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                foreach (var line in (element.GetString() ?? string.Empty).Split('\n'))
                    if (Clean(line, 2000) is { Length: > 0 } cleaned) steps.Add(cleaned);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CollectSteps(item, steps, depth + 1);
                break;
            case JsonValueKind.Object:
                if (element.TryGetProperty("itemListElement", out var list)) CollectSteps(list, steps, depth + 1);
                else if (element.TryGetProperty("text", out var text)) CollectSteps(text, steps, depth + 1);
                else if (element.TryGetProperty("name", out var name)) CollectSteps(name, steps, depth + 1);
                break;
        }
    }

    private static decimal? ParseServings(JsonElement r)
    {
        if (!r.TryGetProperty("recipeYield", out var y)) return null;
        var raw = y.ValueKind switch
        {
            JsonValueKind.Number => y.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.String => y.GetString(),
            JsonValueKind.Array => y.EnumerateArray().Select(e => e.ToString()).FirstOrDefault(),
            _ => null,
        };
        var m = raw is null ? null : NumberRegex().Match(raw);
        if (m is { Success: true } &&
            decimal.TryParse(m.Value.Replace(',', '.'), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var value) && value is > 0 and <= 500)
            return value;
        return null;
    }

    private static int? ParseMinutes(JsonElement r)
    {
        var total = Duration(Str(r, "totalTime"));
        if (total is > 0) return total;
        var sum = (Duration(Str(r, "prepTime")) ?? 0) + (Duration(Str(r, "cookTime")) ?? 0);
        return sum > 0 ? sum : null;
    }

    private static int? Duration(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        try
        {
            var minutes = (int)Math.Round(XmlConvert.ToTimeSpan(iso.Trim()).TotalMinutes);
            return minutes is > 0 and < 60 * 24 * 7 ? minutes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? ParseImage(JsonElement r)
    {
        if (!r.TryGetProperty("image", out var image)) return null;
        string? url = image.ValueKind switch
        {
            JsonValueKind.String => image.GetString(),
            JsonValueKind.Array => image.EnumerateArray().Select(ImageUrl).FirstOrDefault(u => u is not null),
            JsonValueKind.Object => ImageUrl(image),
            _ => null,
        };
        return url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && url.Length <= 2048
            ? url
            : null;
    }

    private static string? ImageUrl(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Object => Str(e, "url"),
        _ => null,
    };

    private static string? ParseAuthor(JsonElement r)
    {
        if (!r.TryGetProperty("author", out var a)) return null;
        return Clean(a.ValueKind switch
        {
            JsonValueKind.String => a.GetString(),
            JsonValueKind.Object => Str(a, "name"),
            JsonValueKind.Array => a.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : Str(e, "name")).FirstOrDefault(),
            _ => null,
        }, 120);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static IEnumerable<string> Strings(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p)) yield break;
        if (p.ValueKind == JsonValueKind.String)
        {
            if (p.GetString() is { } s) yield return s;
        }
        else if (p.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in p.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s) yield return s;
        }
    }

    internal static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = TagRegex().Replace(WebUtility.HtmlDecode(value), " ");
        text = SpacesRegex().Replace(text, " ").Trim();
        if (text.Length == 0) return null;
        return text.Length > max ? text[..max] : text;
    }
}
