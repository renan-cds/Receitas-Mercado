using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ReceitasMercado.Api.Integrations;
using ReceitasMercado.Domain;

namespace ReceitasMercado.IntegrationTests;

public class ImportAiFilesTests(FactoryFixture fx) : IClassFixture<FactoryFixture>
{
    private AppFactory F => fx.Factory;

    private const string RecipeHtml = """
        <html><head><script type="application/ld+json">
        {"@context":"https://schema.org","@type":"Recipe","name":"Bolo de cenoura","recipeYield":"8 porções",
         "recipeIngredient":["3 cenouras","2 xícaras de farinha de trigo","1 pitada de sal"],
         "recipeInstructions":[{"@type":"HowToStep","text":"Bata tudo."},{"@type":"HowToStep","text":"Asse."}],
         "author":{"@type":"Person","name":"Dona Maria"}}
        </script></head><body><script>alert(1)</script>conteúdo</body></html>
        """;

    [Theory]
    [InlineData("http://exemplo.com/receita")]
    [InlineData("https://127.0.0.1/receita")]
    [InlineData("https://localhost/receita")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://10.0.0.5/x")]
    [InlineData("https://[::1]/x")]
    [InlineData("https://user:pass@exemplo.com/x")]
    [InlineData("https://exemplo.com:8443/x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://exemplo.com/x")]
    public async Task Import_rejects_dangerous_urls_before_fetching(string url)
    {
        var a = await Env.NewUser(F);
        var fetched = false;
        F.Fetcher.Handler = _ => { fetched = true; throw new InvalidOperationException(); };
        var (status, body, _) = await a.Post("/api/receitas/importar-url", new { url });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("URL_BLOQUEADA", body.GetProperty("codigo").GetString());
        Assert.False(fetched);
    }

    [Fact]
    public async Task Import_returns_a_draft_that_is_not_saved_and_never_confirms_matches()
    {
        var a = await Env.NewUser(F);
        var flour = await a.NewIngredient("Farinha de trigo", "G");
        await a.NewIngredient("Sal", "G");
        F.Fetcher.Handler = uri => new FetchedPage(RecipeHtml, "text/html", uri);

        var draft = await a.Ok(a.Post("/api/receitas/importar-url", new { url = "https://exemplo.com/bolo" }));
        Assert.Equal("Bolo de cenoura", draft.GetProperty("titulo").GetString());
        Assert.Equal(8, draft.GetProperty("porcoes").GetDecimal());
        Assert.Equal("url", draft.GetProperty("origem").GetString());
        Assert.Equal("Dona Maria", draft.GetProperty("autorAtribuicao").GetString());
        Assert.Equal(2, draft.GetProperty("etapas").GetArrayLength());
        var lines = draft.GetProperty("ingredientes").EnumerateArray().ToList();
        Assert.Equal(3, lines.Count);
        var flourLine = lines.First(l => l.GetProperty("textoOriginal").GetString()!.Contains("farinha"));
        Assert.Equal(flour, flourLine.GetProperty("ingredienteId").GetGuid());
        Assert.NotEqual("confirmada", flourLine.GetProperty("confianca").GetString());
        Assert.DoesNotContain("alert(1)", draft.ToString());

        var saved = await a.Ok(a.Get("/api/receitas"));
        Assert.Empty(saved.GetProperty("itens").EnumerateArray());
    }

    [Fact]
    public async Task Import_maps_fetch_and_parse_failures_to_friendly_errors()
    {
        var a = await Env.NewUser(F);
        F.Fetcher.Handler = uri => new FetchedPage("<html><body>sem receita</body></html>", "text/html", uri);
        var (noRecipe, body, _) = await a.Post("/api/receitas/importar-url", new { url = "https://exemplo.com/artigo" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noRecipe);
        Assert.Equal("RECEITA_NAO_ENCONTRADA", body.GetProperty("codigo").GetString());

        F.Fetcher.Handler = _ => throw new ImportException("TEMPO_ESGOTADO", "O site demorou demais para responder.");
        var (timeout, tb, _) = await a.Post("/api/receitas/importar-url", new { url = "https://exemplo.com/lento" });
        Assert.Equal(HttpStatusCode.BadGateway, timeout);
        Assert.Equal("TEMPO_ESGOTADO", tb.GetProperty("codigo").GetString());

        var reader = await Env.Join(F, a, "leitor");
        var (denied, _, _) = await reader.Post("/api/receitas/importar-url", new { url = "https://exemplo.com/x" });
        Assert.Equal(HttpStatusCode.Forbidden, denied);
    }

    [Fact]
    public async Task External_search_returns_attributed_drafts()
    {
        var a = await Env.NewUser(F);
        F.Search.Results = [new ImportedRecipe("Arrabiata", "Massa", null, null, "https://img/x.jpg", null, ["Pasta"], ["200 g penne", "2 cloves garlic"], ["Cook."], "https://www.themealdb.com/meal/1")];
        var result = await a.Ok(a.Get("/api/receitas/busca-externa?q=arrabiata"));
        Assert.Equal("TheMealDB", result.GetProperty("fonte").GetProperty("nome").GetString());
        var item = result.GetProperty("itens")[0];
        Assert.Equal("busca", item.GetProperty("origem").GetString());
        Assert.Equal(4, item.GetProperty("porcoes").GetDecimal());
        Assert.NotEmpty(item.GetProperty("avisos").EnumerateArray());
        var (tooShort, _, _) = await a.Get("/api/receitas/busca-externa?q=a");
        Assert.Equal(HttpStatusCode.BadRequest, tooShort);
    }

    private const string ValidAiRecipe = """
        {"receitas":[{"titulo":"Omelete","descricao":"Rápida","porcoes":2,"tempoPreparoMin":10,
        "ingredientes":[{"nome":"Ovo","quantidade":3,"unidade":"un"},{"nome":"Sal","quantidade":null,"unidade":null}],
        "etapas":["Bata os ovos.","Frite."],"tags":["rápido"]}]}
        """;

    private async Task ConfigureAi(Api admin, string key = "sk-super-secreta", int limit = 20) =>
        await admin.Ok(admin.Send(HttpMethod.Put, "/api/ia/config", new { baseUrl = "http://localhost:11434/v1", modelo = "teste", apiKey = key, ativa = true, limiteHora = limit }));

    [Fact]
    public async Task Ai_is_optional_503_when_off_and_the_rest_of_the_app_works()
    {
        var a = await Env.NewUser(F);
        var (status, body, _) = await a.Post("/api/ia/gerar-receita", new { ingredientes = new[] { "ovo" } });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("IA_INDISPONIVEL", body.GetProperty("codigo").GetString());
        Assert.False((await a.Ok(a.Get("/api/ia/status"))).GetProperty("disponivel").GetBoolean());
        await a.NewIngredient("Ovo", "UN");
    }

    [Fact]
    public async Task Ai_key_is_encrypted_never_returned_and_only_admins_configure_it()
    {
        var admin = await Env.NewUser(F);
        var editor = await Env.Join(F, admin, "editor");
        await ConfigureAi(admin);
        var config = await admin.Ok(admin.Get("/api/ia/config"));
        Assert.True(config.GetProperty("temChave").GetBoolean());
        Assert.DoesNotContain("sk-super-secreta", config.ToString());

        var (denied, _, _) = await editor.Send(HttpMethod.Put, "/api/ia/config", new { baseUrl = "http://x/v1", modelo = "m", ativa = true });
        Assert.Equal(HttpStatusCode.Forbidden, denied);
        var (badUrl, _, _) = await admin.Send(HttpMethod.Put, "/api/ia/config", new { baseUrl = "javascript:alert(1)", modelo = "m", ativa = true });
        Assert.Equal(HttpStatusCode.BadRequest, badUrl);

        await using var scope = F.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReceitasMercado.Api.Data.AppDbContext>();
        var stored = await db.ConfiguracoesIa.AsNoTracking().FirstAsync(c => c.DespensaId == admin.DespensaId);
        Assert.DoesNotContain("sk-super-secreta", System.Text.Encoding.UTF8.GetString(stored.ApiKeyCifrada!));

        await admin.Ok(admin.Send(HttpMethod.Put, "/api/ia/config", new { baseUrl = "http://localhost:11434/v1", modelo = "outro", ativa = true }));
        Assert.True((await admin.Ok(admin.Get("/api/ia/config"))).GetProperty("temChave").GetBoolean());
        await admin.Ok(admin.Send(HttpMethod.Put, "/api/ia/config", new { baseUrl = "http://localhost:11434/v1", modelo = "outro", ativa = true, removerChave = true }));
        Assert.False((await admin.Ok(admin.Get("/api/ia/config"))).GetProperty("temChave").GetBoolean());
    }

    [Fact]
    public async Task Ai_recipes_are_validated_drafts_with_at_most_inferred_confidence()
    {
        var a = await Env.NewUser(F);
        var egg = await a.NewIngredient("Ovo", "UN");
        await ConfigureAi(a);
        F.Ai.Handler = () => ValidAiRecipe;
        var draft = await a.Ok(a.Post("/api/ia/gerar-receita", new { ingredientes = new[] { "ovo", "sal" }, porcoes = 2 }));
        Assert.Equal("ia", draft.GetProperty("origem").GetString());
        var eggLine = draft.GetProperty("ingredientes").EnumerateArray().First(l => l.GetProperty("nome").GetString() == "Ovo");
        Assert.Equal(egg, eggLine.GetProperty("ingredienteId").GetGuid());
        Assert.Equal("inferida", eggLine.GetProperty("confianca").GetString());
        Assert.Contains("IA", draft.GetProperty("avisos")[0].GetString());
        Assert.Empty((await a.Ok(a.Get("/api/receitas"))).GetProperty("itens").EnumerateArray());

        await a.Stock(egg, 6);
        var suggestions = await a.Ok(a.Post("/api/ia/sugerir-receitas", new { quantidade = 1 }));
        Assert.Single(suggestions.GetProperty("itens").EnumerateArray());
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("{\"receitas\":[{\"titulo\":\"x\",\"porcoes\":2,\"ingredientes\":[{\"nome\":\"Ovo\",\"quantidade\":9999999,\"unidade\":\"g\"}],\"etapas\":[\"a\"]}]}")]
    [InlineData("{\"receitas\":[{\"titulo\":\"x\",\"porcoes\":2,\"ingredientes\":[{\"nome\":\"Ovo\",\"quantidade\":1,\"unidade\":\"parsec\"}],\"etapas\":[\"a\"]}]}")]
    [InlineData("{\"receitas\":[]}")]
    public async Task Invalid_ai_output_is_discarded_with_422(string raw)
    {
        var a = await Env.NewUser(F);
        await ConfigureAi(a);
        F.Ai.Handler = () => raw;
        var (status, body, _) = await a.Post("/api/ia/gerar-receita", new { ingredientes = new[] { "ovo" } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal("IA_RESPOSTA_INVALIDA", body.GetProperty("codigo").GetString());
        Assert.Empty((await a.Ok(a.Get("/api/receitas"))).GetProperty("itens").EnumerateArray());
    }

    [Fact]
    public async Task Ai_is_rate_limited_per_pantry_and_failures_open_the_circuit()
    {
        var a = await Env.NewUser(F);
        await ConfigureAi(a, limit: 2);
        F.Ai.Handler = () => ValidAiRecipe;
        await a.Ok(a.Post("/api/ia/gerar-receita", new { ingredientes = new[] { "ovo" } }));
        await a.Ok(a.Post("/api/ia/gerar-receita", new { ingredientes = new[] { "ovo" } }));
        var (limited, body, _) = await a.Post("/api/ia/gerar-receita", new { ingredientes = new[] { "ovo" } });
        Assert.Equal((HttpStatusCode)429, limited);
        Assert.Equal("LIMITE_IA", body.GetProperty("codigo").GetString());

        var b = await Env.NewUser(F);
        await ConfigureAi(b);
        F.Ai.Handler = null;
        for (var i = 0; i < 3; i++)
        {
            var (s, _, _) = await b.Post("/api/ia/gerar-receita", new { ingredientes = new[] { "ovo" } });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, s);
        }
        var before = F.Ai.Calls;
        F.Ai.Handler = () => ValidAiRecipe;
        var (open, _, _) = await b.Post("/api/ia/gerar-receita", new { ingredientes = new[] { "ovo" } });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, open);
        Assert.Equal(before, F.Ai.Calls);
    }

    [Fact]
    public async Task Ai_substitutions_and_normalization_are_validated_suggestions()
    {
        var a = await Env.NewUser(F);
        var butter = await a.NewIngredient("Margarina", "G");
        await a.Stock(butter, 200);
        await ConfigureAi(a);

        F.Ai.Handler = () => "{\"substituicoes\":[{\"nome\":\"Margarina\",\"proporcao\":\"1:1\",\"observacao\":\"<b>ok</b>\"},{\"nome\":\"Óleo\",\"proporcao\":\"3/4\",\"observacao\":\"mais leve\"}]}";
        var subs = await a.Ok(a.Post("/api/ia/substituicoes", new { ingrediente = "Manteiga" }));
        var items = subs.GetProperty("itens").EnumerateArray().ToList();
        Assert.True(items[0].GetProperty("temEmEstoque").GetBoolean());
        Assert.False(items[1].GetProperty("temEmEstoque").GetBoolean());
        Assert.DoesNotContain("<b>", items[0].ToString());

        F.Ai.Handler = () => "{\"linhas\":[{\"nome\":\"Margarina\",\"quantidade\":2,\"unidade\":\"colher_sopa\",\"opcional\":false}]}";
        var normalized = await a.Ok(a.Post("/api/ia/normalizar-ingredientes", new { linhas = new[] { "2 colheres de margarina" } }));
        var line = normalized.GetProperty("linhas")[0];
        Assert.Equal(butter, line.GetProperty("ingredienteId").GetGuid());
        Assert.NotEqual("confirmada", line.GetProperty("confianca").GetString());

        F.Ai.Handler = () => "{\"linhas\":[]}";
        var (mismatch, _, _) = await a.Post("/api/ia/normalizar-ingredientes", new { linhas = new[] { "x" } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatch);
    }

    private static readonly byte[] Png =
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static async Task<(HttpStatusCode Status, JsonElement Body)> Upload(Api api, byte[] content, string name)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "arquivo", name);
        using var request = api.Build(HttpMethod.Post, "/api/arquivos");
        request.Content = form;
        var response = await api.Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task Uploads_check_real_content_size_and_tenant_isolation()
    {
        var a = await Env.NewUser(F);
        var other = await Env.NewUser(F);
        var (ok, body) = await Upload(a, Png, "foto.png");
        Assert.Equal(HttpStatusCode.Created, ok);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal("image/png", body.GetProperty("contentType").GetString());

        var (notImage, rejected) = await Upload(a, System.Text.Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"), "x.png");
        Assert.Equal(HttpStatusCode.BadRequest, notImage);
        Assert.Equal("TIPO_NAO_SUPORTADO", rejected.GetProperty("codigo").GetString());
        var (tooBig, _) = await Upload(a, [.. Png, .. new byte[3 * 1024 * 1024]], "grande.png");
        Assert.Equal(HttpStatusCode.BadRequest, tooBig);

        var download = await a.Http.SendAsync(a.Build(HttpMethod.Get, $"/api/despensas/{a.DespensaId}/arquivos/{id}", tenant: false));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await download.Content.ReadAsByteArrayAsync());

        var stolen = await other.Http.SendAsync(other.Build(HttpMethod.Get, $"/api/despensas/{a.DespensaId}/arquivos/{id}", tenant: false));
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);

        var recipe = await a.Ok(a.Post("/api/receitas", new
        {
            titulo = "Com foto", porcoes = 2, status = "completa", etapas = new[] { "a" }, ingredientes = new[] { Api.Line("X", null, 1, "UN") }, imagemArquivoId = id,
        }));
        Assert.Equal(id, recipe.GetProperty("imagemArquivoId").GetGuid());
        var (foreignRef, _, _) = await other.Post("/api/receitas", new
        {
            titulo = "Roubo", porcoes = 2, status = "completa", etapas = new[] { "a" }, ingredientes = new[] { Api.Line("X", null, 1, "UN") }, imagemArquivoId = id,
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreignRef);
    }
}
