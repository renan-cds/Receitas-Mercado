using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReceitasMercado.Domain;

public static partial class TheMealDbMapper
{
    [GeneratedRegex(@"^\s*(step\s*\d+[:.)-]?|\d+[.)])\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StepPrefix();

    public static IReadOnlyList<ImportedRecipe> MapSearchResponse(string json, int max = 10)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("meals", out var meals) || meals.ValueKind != JsonValueKind.Array)
            return [];
        return meals.EnumerateArray().Select(Map).Where(r => r is not null).Cast<ImportedRecipe>().Take(max).ToList();
    }

    public static ImportedRecipe? Map(JsonElement meal)
    {
        var title = JsonLdRecipeParser.Clean(Get(meal, "strMeal"), 200);
        if (title is null) return null;

        var ingredients = new List<string>();
        for (var i = 1; i <= 20; i++)
        {
            var name = Get(meal, $"strIngredient{i}")?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            var measure = Get(meal, $"strMeasure{i}")?.Trim();
            ingredients.Add(string.IsNullOrWhiteSpace(measure) ? name : $"{measure} {name}");
        }
        if (ingredients.Count == 0) return null;

        var steps = (Get(meal, "strInstructions") ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => StepPrefix().Replace(l.Trim(), string.Empty).Trim())
            .Where(l => l.Length > 2)
            .Select(l => JsonLdRecipeParser.Clean(l, 2000)!)
            .ToList();

        var tags = new[] { Get(meal, "strCategory"), Get(meal, "strArea") }
            .Concat((Get(meal, "strTags") ?? string.Empty).Split(','))
            .Select(t => TextNormalizer.Normalize(t))
            .Where(t => t.Length > 0)
            .Distinct()
            .Take(10)
            .ToList();

        var id = Get(meal, "idMeal");
        var image = Get(meal, "strMealThumb");
        return new ImportedRecipe(
            title, null, null, null,
            image is not null && image.StartsWith("https://", StringComparison.Ordinal) ? image : null,
            "TheMealDB",
            tags, ingredients, steps,
            id is null ? null : $"https://www.themealdb.com/meal/{id}");
    }

    private static string? Get(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
