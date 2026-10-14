namespace ReceitasMercado.Domain;

public sealed record ImportedRecipe(
    string Title,
    string? Description,
    decimal? Servings,
    int? PrepMinutes,
    string? ImageUrl,
    string? Author,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> IngredientLines,
    IReadOnlyList<string> Steps,
    string? SourceUrl);
