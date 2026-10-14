using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReceitasMercado.Domain;

public sealed record AiIngredient(string Name, decimal? Quantity, string? UnitCode);

public sealed record AiRecipe(
    string Title,
    string? Description,
    decimal Servings,
    int? PrepMinutes,
    IReadOnlyList<AiIngredient> Ingredients,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Tags);

public sealed record AiValidationResult(IReadOnlyList<AiRecipe> Recipes, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0 && Recipes.Count > 0;
}

/// <summary>
/// Toda saída de IA passa por aqui antes de virar rascunho. Falhas descartam a resposta inteira.
/// </summary>
public static partial class AiRecipeValidator
{
    private const decimal MaxQuantity = 100_000m;

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    public static string StripCodeFences(string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith("```")) return text;
        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0) return text;
        text = text[(firstNewline + 1)..];
        var end = text.LastIndexOf("```", StringComparison.Ordinal);
        return (end >= 0 ? text[..end] : text).Trim();
    }

    public static AiValidationResult ParseRecipes(string raw, int maxRecipes = 5)
    {
        var errors = new List<string>();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(StripCodeFences(raw), new JsonDocumentOptions { MaxDepth = 16, AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            return new AiValidationResult([], ["A resposta da IA não é um JSON válido."]);
        }

        using (doc)
        {
            var root = doc.RootElement;
            IEnumerable<JsonElement> candidates;
            if (root.ValueKind == JsonValueKind.Array) candidates = root.EnumerateArray();
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("receitas", out var list) && list.ValueKind == JsonValueKind.Array)
                candidates = list.EnumerateArray();
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("receita", out var one))
                candidates = [one];
            else if (root.ValueKind == JsonValueKind.Object) candidates = [root];
            else return new AiValidationResult([], ["Formato inesperado na resposta da IA."]);

            var recipes = new List<AiRecipe>();
            var index = 0;
            foreach (var candidate in candidates.Take(maxRecipes))
            {
                index++;
                var recipeErrors = new List<string>();
                var recipe = Validate(candidate, recipeErrors);
                if (recipe is not null && recipeErrors.Count == 0) recipes.Add(recipe);
                else errors.AddRange(recipeErrors.Select(e => $"Receita {index}: {e}"));
            }
            if (recipes.Count == 0 && errors.Count == 0) errors.Add("A IA não devolveu nenhuma receita.");
            return new AiValidationResult(recipes, errors);
        }
    }

    private static AiRecipe? Validate(JsonElement e, List<string> errors)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            errors.Add("estrutura inválida.");
            return null;
        }

        var title = Text(e, "titulo", 200);
        if (title is null) errors.Add("título ausente.");

        decimal servings = 4;
        if (e.TryGetProperty("porcoes", out var p) && p.ValueKind == JsonValueKind.Number)
        {
            servings = p.GetDecimal();
            if (servings is < 0.5m or > 100m) errors.Add("porções fora do intervalo (0,5 a 100).");
        }
        else if (e.TryGetProperty("porcoes", out p) && p.ValueKind != JsonValueKind.Null)
        {
            errors.Add("porções devem ser numéricas.");
        }

        int? minutes = null;
        if (e.TryGetProperty("tempoPreparoMin", out var t) && t.ValueKind == JsonValueKind.Number)
        {
            if (t.TryGetInt32(out var m) && m is > 0 and <= 60 * 24) minutes = m;
            else errors.Add("tempo de preparo inválido.");
        }

        var ingredients = new List<AiIngredient>();
        if (!e.TryGetProperty("ingredientes", out var ing) || ing.ValueKind != JsonValueKind.Array || ing.GetArrayLength() is 0 or > 60)
        {
            errors.Add("lista de ingredientes ausente ou fora do limite (1 a 60).");
        }
        else
        {
            foreach (var item in ing.EnumerateArray())
            {
                var name = item.ValueKind == JsonValueKind.Object ? Text(item, "nome", 120) : null;
                if (name is null)
                {
                    errors.Add("ingrediente sem nome.");
                    continue;
                }

                decimal? quantity = null;
                if (item.TryGetProperty("quantidade", out var q) && q.ValueKind == JsonValueKind.Number)
                {
                    quantity = q.GetDecimal();
                    if (quantity is <= 0 or > MaxQuantity)
                    {
                        errors.Add($"quantidade implausível para '{name}'.");
                        continue;
                    }
                }
                else if (item.TryGetProperty("quantidade", out q) && q.ValueKind != JsonValueKind.Null)
                {
                    errors.Add($"quantidade de '{name}' deve ser numérica.");
                    continue;
                }

                string? unit = null;
                if (item.TryGetProperty("unidade", out var u) && u.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(u.GetString()))
                {
                    unit = IngredientTextParser.ResolveUnit(u.GetString());
                    if (unit is null)
                    {
                        errors.Add($"unidade desconhecida para '{name}': {u.GetString()}.");
                        continue;
                    }
                }
                if (quantity.HasValue && unit is null) unit = "UN";
                ingredients.Add(new AiIngredient(name, quantity, unit));
            }
        }

        var steps = new List<string>();
        if (!e.TryGetProperty("etapas", out var st) || st.ValueKind != JsonValueKind.Array || st.GetArrayLength() is 0 or > 50)
        {
            errors.Add("etapas ausentes ou fora do limite (1 a 50).");
        }
        else
        {
            foreach (var step in st.EnumerateArray())
            {
                var text = step.ValueKind == JsonValueKind.String ? Clean(step.GetString(), 2000) : null;
                if (text is null) errors.Add("etapa vazia ou inválida.");
                else steps.Add(text);
            }
        }

        var tags = new List<string>();
        if (e.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array)
            tags.AddRange(tg.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => TextNormalizer.Normalize(Clean(x.GetString(), 40)))
                .Where(x => x.Length > 0)
                .Distinct()
                .Take(10));

        if (errors.Count > 0 || title is null) return null;
        return new AiRecipe(title, Text(e, "descricao", 1000), servings, minutes, ingredients, steps, tags);
    }

    private static string? Text(JsonElement e, string name, int max) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? Clean(p.GetString(), max) : null;

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = Regex.Replace(TagRegex().Replace(value, " "), @"\s+", " ").Trim();
        if (text.Length == 0) return null;
        return text.Length > max ? text[..max] : text;
    }
}
