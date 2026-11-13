using System.Net;
using System.Text.Json;

namespace ReceitasMercado.IntegrationTests;

public class RecipeAndPreparoTests(FactoryFixture fx) : IClassFixture<FactoryFixture>
{
    private AppFactory F => fx.Factory;

    private static string State(JsonElement compat) => compat.GetProperty("estado").GetString()!;

    [Fact]
    public async Task Compatibility_distinguishes_ready_missing_check_and_draft()
    {
        var a = await Env.NewUser(F);
        var rice = await a.NewIngredient("Arroz", "G");
        var beans = await a.NewIngredient("Feijão", "G");
        var salt = await a.NewIngredient("Sal", "G");
        await a.Stock(rice, 1, "KG");
        await a.Stock(beans, 100);

        var ready = await a.NewRecipe("Arroz simples", [Api.Line("Arroz", rice, 300, "G")]);
        var missing = await a.NewRecipe("Feijoada", [Api.Line("Arroz", rice, 300, "G"), Api.Line("Feijão", beans, 500, "G")]);
        var optionalOnly = await a.NewRecipe("Arroz com sal", [Api.Line("Arroz", rice, 100, "G"), Api.Line("Sal", salt, 5, "G", optional: true)]);
        var uncertain = await a.NewRecipe("Arroz com algo", [Api.Line("Arroz", rice, 100, "G"), Api.Line("Tempero da vovó", null, 10, "G")]);
        var inferred = await a.NewRecipe("Arroz inferido", [Api.Line("Arroz", rice, 100, "G"), Api.Line("Feijão preto", beans, 10, "G", confidence: "inferida")]);
        var draft = await a.NewRecipe("Ideia", [], status: "rascunho");

        Assert.Equal("pode_preparar", State(await a.Ok(a.Get($"/api/receitas/{ready}/compatibilidade"))));
        var missingCompat = await a.Ok(a.Get($"/api/receitas/{missing}/compatibilidade"));
        Assert.Equal("faltam_ingredientes", State(missingCompat));
        var beanLine = missingCompat.GetProperty("linhas").EnumerateArray().First(l => l.GetProperty("nome").GetString() == "Feijão");
        Assert.Equal(400, beanLine.GetProperty("faltante").GetDecimal());
        Assert.Equal("pode_preparar", State(await a.Ok(a.Get($"/api/receitas/{optionalOnly}/compatibilidade"))));
        Assert.Equal("verificar_ingredientes", State(await a.Ok(a.Get($"/api/receitas/{uncertain}/compatibilidade"))));
        Assert.Equal("verificar_ingredientes", State(await a.Ok(a.Get($"/api/receitas/{inferred}/compatibilidade"))));
        Assert.Equal("rascunho", State(await a.Ok(a.Get($"/api/receitas/{draft}/compatibilidade"))));

        var scaled = await a.Ok(a.Get($"/api/receitas/{ready}/compatibilidade?porcoes=20"));
        Assert.Equal("faltam_ingredientes", State(scaled));
        Assert.False(string.IsNullOrWhiteSpace(scaled.GetProperty("aviso").GetString()));

        var onlyReady = await a.Ok(a.Get("/api/receitas?compat=pode&limite=50"));
        var titles = onlyReady.GetProperty("itens").EnumerateArray().Select(r => r.GetProperty("titulo").GetString()).ToList();
        Assert.Contains("Arroz simples", titles);
        Assert.DoesNotContain("Feijoada", titles);
        var sorted = await a.Ok(a.Get("/api/receitas?ordem=compat&limite=50"));
        Assert.Equal("pode_preparar", sorted.GetProperty("itens")[0].GetProperty("compatibilidade").GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Equivalence_groups_and_approximate_units_are_handled()
    {
        var a = await Env.NewUser(F);
        var group = (await a.Ok(a.Post("/api/grupos-equivalencia", new { nome = "Açúcares" }))).GetProperty("id").GetGuid();
        var refined = await a.NewIngredient("Açúcar refinado", "G", groupId: group);
        var crystal = await a.NewIngredient("Açúcar cristal", "G", groupId: group);
        await a.Stock(crystal, 400);
        var cake = await a.NewRecipe("Bolo", [Api.Line("Açúcar refinado", refined, 300, "G")]);
        var compat = await a.Ok(a.Get($"/api/receitas/{cake}/compatibilidade"));
        Assert.Equal("pode_preparar", State(compat));
        Assert.True(compat.GetProperty("linhas")[0].GetProperty("usaEquivalente").GetBoolean());

        var flour = await a.NewIngredient("Farinha", "G");
        await a.Stock(flour, 1000);
        var cups = await a.NewRecipe("Massa", [Api.Line("Farinha", flour, 2, "XICARA")]);
        Assert.Equal("verificar_ingredientes", State(await a.Ok(a.Get($"/api/receitas/{cups}/compatibilidade"))));
    }

    [Fact]
    public async Task Recipe_validation_duplicate_and_delete()
    {
        var a = await Env.NewUser(F);
        var (incomplete, body, _) = await a.Post("/api/receitas", new { titulo = "Vazia", porcoes = 2, status = "completa", etapas = Array.Empty<string>(), ingredientes = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, incomplete);
        Assert.Equal("RECEITA_INCOMPLETA", body.GetProperty("codigo").GetString());

        var (badUnit, _, _) = await a.Post("/api/receitas", new
        {
            titulo = "X", porcoes = 2, status = "completa", etapas = new[] { "a" }, ingredientes = new[] { Api.Line("Y", null, 1, "FURLONG") },
        });
        Assert.Equal(HttpStatusCode.BadRequest, badUnit);
        var (badUrl, _, _) = await a.Post("/api/receitas", new
        {
            titulo = "X", porcoes = 2, status = "completa", etapas = new[] { "a" }, ingredientes = new[] { Api.Line("Y", null, 1, "G") },
            imagemUrl = "javascript:alert(1)",
        });
        Assert.Equal(HttpStatusCode.BadRequest, badUrl);

        var id = await a.NewRecipe("Original", [Api.Line("Algo", null, 1, "UN")]);
        var copy = await a.Ok(a.Post($"/api/receitas/{id}/duplicar"));
        Assert.Equal("Original (cópia)", copy.GetProperty("titulo").GetString());
        await a.Ok(a.Put($"/api/receitas/{id}", new
        {
            titulo = "Editada", porcoes = 3, status = "completa", etapas = new[] { "novo passo" },
            ingredientes = new[] { Api.Line("Outro", null, 2, "UN") }, tags = new[] { "Jantar", "Rápido" },
        }));
        var edited = await a.Ok(a.Get($"/api/receitas/{id}"));
        Assert.Equal("Editada", edited.GetProperty("titulo").GetString());
        Assert.Equal(1, edited.GetProperty("ingredientes").GetArrayLength());
        Assert.Equal(new[] { "jantar", "rapido" }, edited.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray());

        var search = await a.Ok(a.Get("/api/receitas?busca=editada"));
        Assert.Single(search.GetProperty("itens").EnumerateArray());
        await a.Ok(a.Delete($"/api/receitas/{id}"));
        var (gone, _, _) = await a.Get($"/api/receitas/{id}");
        Assert.Equal(HttpStatusCode.NotFound, gone);
    }

    [Fact]
    public async Task Favorites_are_personal_and_visible_only_to_their_owner()
    {
        var admin = await Env.NewUser(F);
        var reader = await Env.Join(F, admin, "leitor");
        var id = await admin.NewRecipe("Pão", [Api.Line("Farinha", null, 500, "G")]);
        await reader.Ok(reader.Put($"/api/receitas/{id}/favorito"));
        var mine = await reader.Ok(reader.Get("/api/receitas?favoritas=true"));
        Assert.Single(mine.GetProperty("itens").EnumerateArray());
        var theirs = await admin.Ok(admin.Get("/api/receitas?favoritas=true"));
        Assert.Empty(theirs.GetProperty("itens").EnumerateArray());
        await reader.Ok(reader.Delete($"/api/receitas/{id}/favorito"));
        Assert.Empty((await reader.Ok(reader.Get("/api/receitas?favoritas=true"))).GetProperty("itens").EnumerateArray());
    }

    private async Task<(Api Api, Guid Rice, Guid Recipe, Guid Preparo)> Prepared(decimal riceNeeded = 300)
    {
        var a = await Env.NewUser(F);
        var rice = await a.NewIngredient("Arroz", "G");
        await a.Stock(rice, 200, expiry: "2027-01-01");
        await a.Stock(rice, 500, expiry: "2027-09-01");
        var recipe = await a.NewRecipe("Arroz", [Api.Line("Arroz", rice, riceNeeded, "G")], servings: 2);
        var preparo = await a.Ok(a.Post("/api/preparos", new { receitaId = recipe, porcoes = 2 }));
        return (a, rice, recipe, preparo.GetProperty("preparoId").GetGuid());
    }

    [Fact]
    public async Task Preparo_preview_shows_FEFO_and_confirm_deducts_exactly_once()
    {
        var (a, rice, recipe, id) = await Prepared();
        var preview = await a.Ok(a.Get($"/api/preparos/{id}"));
        var lots = preview.GetProperty("itens")[0].GetProperty("baixas")[0].GetProperty("lotes");
        Assert.Equal(2, lots.GetArrayLength());
        Assert.Equal(200, lots[0].GetProperty("quantidade").GetDecimal());
        Assert.Equal(700, await a.Total(rice));

        var first = await a.Ok(a.Post($"/api/preparos/{id}/confirmar", new { }, "conf-1"));
        Assert.Equal("confirmado", first.GetProperty("status").GetString());
        Assert.Equal(400, await a.Total(rice));
        var again = await a.Ok(a.Post($"/api/preparos/{id}/confirmar", new { }, "conf-2"));
        Assert.Equal("confirmado", again.GetProperty("status").GetString());
        Assert.Equal(400, await a.Total(rice));

        var history = await a.Ok(a.Get("/api/preparos"));
        Assert.Single(history.GetProperty("itens").EnumerateArray());
        var (cancel, _, _) = await a.Post($"/api/preparos/{id}/cancelar");
        Assert.Equal(HttpStatusCode.Conflict, cancel);

        await a.Ok(a.Delete($"/api/receitas/{recipe}"));
        var kept = await a.Ok(a.Get($"/api/preparos/{id}"));
        Assert.Equal("Arroz", kept.GetProperty("receitaTitulo").GetString());
    }

    [Fact]
    public async Task Parallel_confirmations_of_the_same_preparo_deduct_once()
    {
        var (a, rice, _, id) = await Prepared();
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            a.Post($"/api/preparos/{id}/confirmar", new { }, i % 2 == 0 ? "mesma" : $"outra-{i}")));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Equal(400, await a.Total(rice));
        var movements = await a.Ok(a.Get($"/api/estoque/movimentacoes?ingredienteId={rice}&limite=100"));
        Assert.Equal(2, movements.GetProperty("itens").EnumerateArray().Count(m => m.GetProperty("tipo").GetString() == "baixa_preparo"));
    }

    [Fact]
    public async Task Insufficient_stock_returns_409_with_shortages_and_changes_nothing_unless_consuming_available()
    {
        var (a, rice, _, id) = await Prepared(riceNeeded: 900);
        var (status, body, _) = await a.Post($"/api/preparos/{id}/confirmar", new { });
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("ESTOQUE_INSUFICIENTE", body.GetProperty("codigo").GetString());
        var shortage = body.GetProperty("detalhes").GetProperty("faltantes")[0];
        Assert.Equal(200, shortage.GetProperty("faltante").GetDecimal());
        Assert.Equal(700, await a.Total(rice));

        var done = await a.Ok(a.Post($"/api/preparos/{id}/confirmar", new { consumirDisponivel = true }));
        Assert.Equal(700, done.GetProperty("itens")[0].GetProperty("quantidadeBaixada").GetDecimal());
        Assert.Equal(200, done.GetProperty("itens")[0].GetProperty("quantidadeFaltante").GetDecimal());
        Assert.Equal(0, await a.Total(rice));
    }

    [Fact]
    public async Task Uncertain_lines_are_recorded_but_not_deducted_and_scaling_applies()
    {
        var a = await Env.NewUser(F);
        var rice = await a.NewIngredient("Arroz", "G");
        await a.Stock(rice, 1000);
        var recipe = await a.NewRecipe("Mista", [Api.Line("Arroz", rice, 100, "G"), Api.Line("Ervas", null, 5, "G"), Api.Line("Sal", null, null, null)], servings: 2);
        var preparo = await a.Ok(a.Post("/api/preparos", new { receitaId = recipe, porcoes = 6 }));
        Assert.True(preparo.GetProperty("temNaoDescontaveis").GetBoolean());
        var done = await a.Ok(a.Post($"/api/preparos/{preparo.GetProperty("preparoId").GetGuid()}/confirmar", new { }));
        Assert.Equal(700, await a.Total(rice));
        var items = done.GetProperty("itens").EnumerateArray().ToList();
        Assert.Equal(2, items.Count(i => i.GetProperty("naoDescontado").GetBoolean()));
    }

    [Fact]
    public async Task Expired_or_cancelled_preparos_cannot_be_confirmed_and_drafts_cannot_start()
    {
        var (a, rice, recipe, id) = await Prepared();
        await a.Ok(a.Post($"/api/preparos/{id}/cancelar"));
        var (cancelled, body, _) = await a.Post($"/api/preparos/{id}/confirmar", new { });
        Assert.Equal(HttpStatusCode.Conflict, cancelled);
        Assert.Equal("PREPARO_CANCELADO", body.GetProperty("codigo").GetString());

        var draft = await a.NewRecipe("Rascunho", [], status: "rascunho");
        var (started, _, _) = await a.Post("/api/preparos", new { receitaId = draft, porcoes = 2 });
        Assert.Equal(HttpStatusCode.Conflict, started);

        var fresh = await a.Ok(a.Post("/api/preparos", new { receitaId = recipe, porcoes = 2 }));
        var freshId = fresh.GetProperty("preparoId").GetGuid();
        await using (var scope = F.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReceitasMercado.Api.Data.AppDbContext>();
            await db.Preparos.Where(p => p.Id == freshId).ExecuteUpdateAsync(s => s.SetProperty(p => p.ExpiraEm, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        var (expired, expiredBody, _) = await a.Post($"/api/preparos/{freshId}/confirmar", new { });
        Assert.Equal(HttpStatusCode.Conflict, expired);
        Assert.Equal("PREPARO_EXPIRADO", expiredBody.GetProperty("codigo").GetString());
        Assert.Equal(700, await a.Total(rice));
    }

    [Fact]
    public async Task Preparo_uses_equivalent_ingredients_and_readers_cannot_start_one()
    {
        var admin = await Env.NewUser(F);
        var reader = await Env.Join(F, admin, "leitor");
        var group = (await admin.Ok(admin.Post("/api/grupos-equivalencia", new { nome = "Leites" }))).GetProperty("id").GetGuid();
        var whole = await admin.NewIngredient("Leite integral", "ML", groupId: group);
        var skim = await admin.NewIngredient("Leite desnatado", "ML", groupId: group);
        await admin.Stock(skim, 1, "L");
        var recipe = await admin.NewRecipe("Vitamina", [Api.Line("Leite integral", whole, 400, "ML")]);

        var (denied, _, _) = await reader.Post("/api/preparos", new { receitaId = recipe, porcoes = 4 });
        Assert.Equal(HttpStatusCode.Forbidden, denied);

        var preparo = await admin.Ok(admin.Post("/api/preparos", new { receitaId = recipe, porcoes = 4 }));
        await admin.Ok(admin.Post($"/api/preparos/{preparo.GetProperty("preparoId").GetGuid()}/confirmar", new { }));
        Assert.Equal(600, await admin.Total(skim));
        Assert.Equal(0, await admin.Total(whole));
    }
}
