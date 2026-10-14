using Microsoft.Extensions.Caching.Memory;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Integrations;

public interface IRecipeSearchClient
{
    Task<IReadOnlyList<ImportedRecipe>> SearchAsync(string query, CancellationToken ct);
}

public sealed class TheMealDbClient(HttpClient http, IMemoryCache cache) : IRecipeSearchClient
{
    public async Task<IReadOnlyList<ImportedRecipe>> SearchAsync(string query, CancellationToken ct)
    {
        var normalized = TextNormalizer.Normalize(query);
        if (normalized.Length < 2) return [];
        var cacheKey = $"themealdb:{normalized}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<ImportedRecipe>? cached) && cached is not null) return cached;

        try
        {
            var json = await http.GetStringAsync($"search.php?s={Uri.EscapeDataString(normalized)}", ct);
            var recipes = TheMealDbMapper.MapSearchResponse(json);
            cache.Set(cacheKey, recipes, TimeSpan.FromMinutes(5));
            return recipes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            throw new ImportException("BUSCA_INDISPONIVEL", "A fonte de receitas está indisponível no momento. Cadastre a receita manualmente ou tente mais tarde.");
        }
    }
}
