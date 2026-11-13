using ReceitasMercado.Domain;

namespace ReceitasMercado.UnitTests;

public class JsonLdRecipeParserTests
{
    private static string Page(string json) =>
        $"<html><head><script type=\"application/ld+json\">{json}</script></head><body>conteúdo protegido</body></html>";

    [Fact]
    public void Parses_plain_recipe()
    {
        var html = Page("""
        {"@context":"https://schema.org","@type":"Recipe","name":"Bolo de cenoura",
         "description":"Clássico <b>fofinho</b>",
         "recipeYield":"8 porções","prepTime":"PT20M","cookTime":"PT40M",
         "image":["https://exemplo.com/bolo.jpg"],
         "author":{"@type":"Person","name":"Ana"},
         "keywords":"bolo, Sobremesa",
         "recipeIngredient":["3 cenouras","4 ovos","2 xícaras de açúcar"],
         "recipeInstructions":[{"@type":"HowToStep","text":"Bata tudo."},{"@type":"HowToStep","text":"Asse."}]}
        """);
        var r = JsonLdRecipeParser.Parse(html, "https://exemplo.com/bolo");
        Assert.NotNull(r);
        Assert.Equal("Bolo de cenoura", r!.Title);
        Assert.Equal("Clássico fofinho", r.Description);
        Assert.Equal(8m, r.Servings);
        Assert.Equal(60, r.PrepMinutes);
        Assert.Equal("https://exemplo.com/bolo.jpg", r.ImageUrl);
        Assert.Equal("Ana", r.Author);
        Assert.Equal(["bolo", "sobremesa"], r.Tags);
        Assert.Equal(3, r.IngredientLines.Count);
        Assert.Equal(["Bata tudo.", "Asse."], r.Steps);
        Assert.Equal("https://exemplo.com/bolo", r.SourceUrl);
    }

    [Fact]
    public void Finds_recipe_inside_graph_and_how_to_sections()
    {
        var html = Page("""
        {"@graph":[{"@type":"WebSite","name":"Site"},
          {"@type":["Recipe","Thing"],"name":"Sopa","recipeIngredient":["1 cebola"],
           "recipeInstructions":[{"@type":"HowToSection","itemListElement":[{"@type":"HowToStep","text":"Refogue"}]}]}]}
        """);
        var r = JsonLdRecipeParser.Parse(html);
        Assert.Equal("Sopa", r!.Title);
        Assert.Equal(["Refogue"], r.Steps);
    }

    [Fact]
    public void Splits_string_instructions_by_line()
    {
        var r = JsonLdRecipeParser.Parse(Page("""{"@type":"Recipe","name":"X","recipeIngredient":["a"],"recipeInstructions":"Passo um\nPasso dois"}"""));
        Assert.Equal(2, r!.Steps.Count);
    }

    [Fact]
    public void Rejects_non_https_images()
    {
        var r = JsonLdRecipeParser.Parse(Page("""{"@type":"Recipe","name":"X","image":"http://x.com/a.jpg","recipeIngredient":["a"]}"""));
        Assert.Null(r!.ImageUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>sem json-ld</html>")]
    [InlineData("<script type=\"application/ld+json\">{ quebrado</script>")]
    [InlineData("<script type=\"application/ld+json\">{\"@type\":\"Article\",\"name\":\"x\"}</script>")]
    [InlineData("<script type=\"application/ld+json\">{\"@type\":\"Recipe\",\"name\":\"sem ingredientes\"}</script>")]
    public void Returns_null_when_no_usable_recipe(string html) => Assert.Null(JsonLdRecipeParser.Parse(html));

    [Fact]
    public void Never_exposes_markup_from_the_page_body()
    {
        var r = JsonLdRecipeParser.Parse(Page("""{"@type":"Recipe","name":"&lt;script&gt;alert(1)&lt;/script&gt;Bolo","recipeIngredient":["1 ovo"]}"""));
        Assert.DoesNotContain("<", r!.Title);
    }
}

public class TheMealDbMapperTests
{
    private const string Json = """
    {"meals":[{"idMeal":"52772","strMeal":"Teriyaki Chicken Casserole","strCategory":"Chicken","strArea":"Japanese",
      "strInstructions":"STEP 1\r\nPreheat oven.\r\nSTEP 2\r\nMix soy sauce.\r\n3. Bake.","strMealThumb":"https://www.themealdb.com/images/media/meals/x.jpg","strTags":"Meat,Casserole",
      "strIngredient1":"soy sauce","strMeasure1":"3/4 cup","strIngredient2":"water","strMeasure2":"1/2 cup","strIngredient3":"","strMeasure3":" "}]}
    """;

    [Fact]
    public void Maps_meals_with_steps_and_ingredients()
    {
        var r = Assert.Single(TheMealDbMapper.MapSearchResponse(Json));
        Assert.Equal("Teriyaki Chicken Casserole", r.Title);
        Assert.Equal(["3/4 cup soy sauce", "1/2 cup water"], r.IngredientLines);
        Assert.Equal(["Preheat oven.", "Mix soy sauce.", "Bake."], r.Steps);
        Assert.Equal("TheMealDB", r.Author);
        Assert.Equal("https://www.themealdb.com/meal/52772", r.SourceUrl);
        Assert.Contains("japanese", r.Tags);
    }

    [Fact]
    public void Null_meals_is_empty() => Assert.Empty(TheMealDbMapper.MapSearchResponse("{\"meals\":null}"));

    [Fact]
    public void Parsed_measures_become_convertible_lines()
    {
        var parsed = IngredientTextParser.Parse("3/4 cup soy sauce");
        Assert.Equal(0.75m, parsed.Quantity);
        Assert.Equal("XICARA", parsed.UnitCode);
    }
}

public class AiRecipeValidatorTests
{
    private const string Valid = """
    {"receitas":[{"titulo":"Omelete","descricao":"Rápida","porcoes":2,"tempoPreparoMin":10,
      "ingredientes":[{"nome":"ovo","quantidade":3,"unidade":"un"},{"nome":"sal","quantidade":null,"unidade":null},{"nome":"leite","quantidade":100,"unidade":"ml"}],
      "etapas":["Bata os ovos.","Frite."],"tags":["Rápido"]}]}
    """;

    [Fact]
    public void Accepts_valid_structure_and_resolves_units()
    {
        var result = AiRecipeValidator.ParseRecipes(Valid);
        Assert.True(result.IsValid);
        var recipe = result.Recipes[0];
        Assert.Equal("UN", recipe.Ingredients[0].UnitCode);
        Assert.Null(recipe.Ingredients[1].Quantity);
        Assert.Equal("ML", recipe.Ingredients[2].UnitCode);
        Assert.Equal(["rapido"], recipe.Tags);
    }

    [Fact]
    public void Accepts_markdown_fenced_json() =>
        Assert.True(AiRecipeValidator.ParseRecipes("```json\n" + Valid + "\n```").IsValid);

    [Theory]
    [InlineData("não é json")]
    [InlineData("""{"receitas":[]}""")]
    [InlineData("""{"titulo":"x","porcoes":2,"ingredientes":[],"etapas":["a"]}""")]
    [InlineData("""{"titulo":"x","porcoes":2,"ingredientes":[{"nome":"a","quantidade":1,"unidade":"tonelada"}],"etapas":["a"]}""")]
    [InlineData("""{"titulo":"x","porcoes":2,"ingredientes":[{"nome":"a","quantidade":1000000000,"unidade":"g"}],"etapas":["a"]}""")]
    [InlineData("""{"titulo":"x","porcoes":2,"ingredientes":[{"nome":"a","quantidade":-1,"unidade":"g"}],"etapas":["a"]}""")]
    [InlineData("""{"titulo":"x","porcoes":"muitas","ingredientes":[{"nome":"a"}],"etapas":["a"]}""")]
    [InlineData("""{"titulo":"x","porcoes":2,"ingredientes":[{"nome":"a"}],"etapas":[]}""")]
    [InlineData("""{"porcoes":2,"ingredientes":[{"nome":"a"}],"etapas":["a"]}""")]
    public void Rejects_invalid_structures(string raw) => Assert.False(AiRecipeValidator.ParseRecipes(raw).IsValid);

    [Fact]
    public void Strips_html_from_text()
    {
        var result = AiRecipeValidator.ParseRecipes("""{"titulo":"<b>Bolo</b>","porcoes":2,"ingredientes":[{"nome":"a"}],"etapas":["<script>x</script>Asse"]}""");
        Assert.Equal("Bolo", result.Recipes[0].Title);
        Assert.DoesNotContain("<", result.Recipes[0].Steps[0]);
    }

    [Fact]
    public void One_bad_recipe_does_not_hide_the_error_but_keeps_good_ones()
    {
        var result = AiRecipeValidator.ParseRecipes("""
        {"receitas":[{"titulo":"ok","porcoes":2,"ingredientes":[{"nome":"a"}],"etapas":["a"]},{"titulo":"ruim","porcoes":2,"ingredientes":[],"etapas":[]}]}
        """);
        Assert.Single(result.Recipes);
        Assert.NotEmpty(result.Errors);
    }
}
