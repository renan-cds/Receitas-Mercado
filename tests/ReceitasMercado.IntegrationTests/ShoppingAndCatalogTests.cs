using System.Net;
using System.Text.Json;

namespace ReceitasMercado.IntegrationTests;

public class ShoppingAndCatalogTests(FactoryFixture fx) : IClassFixture<FactoryFixture>
{
    private AppFactory F => fx.Factory;

    [Fact]
    public async Task List_from_recipes_consolidates_needs_and_subtracts_stock()
    {
        var a = await Env.NewUser(F);
        var flour = await a.NewIngredient("Farinha", "G");
        var egg = await a.NewIngredient("Ovo", "UN");
        await a.Stock(flour, 300);
        var cake = await a.NewRecipe("Bolo", [Api.Line("Farinha", flour, 400, "G"), Api.Line("Ovo", egg, 3, "UN"), Api.Line("Sal", null, null, null)], servings: 4);
        var bread = await a.NewRecipe("Pão", [Api.Line("Farinha", flour, 0.5m, "KG"), Api.Line("Ovo", egg, 1, "UN")], servings: 4);

        var response = await a.Ok(a.Post("/api/listas/de-receitas", new
        {
            nome = "Semana", receitas = new[] { new { receitaId = cake, porcoes = 4 }, new { receitaId = bread, porcoes = 8 } },
        }));
        var items = response.GetProperty("lista").GetProperty("itens").EnumerateArray().ToList();
        var flourItem = items.First(i => i.GetProperty("ingredienteId").ValueKind != JsonValueKind.Null && i.GetProperty("ingredienteNome").GetString() == "Farinha");
        Assert.Equal(1400, flourItem.GetProperty("quantidadeNecessaria").GetDecimal());
        Assert.Equal(300, flourItem.GetProperty("quantidadeDescontadaEstoque").GetDecimal());
        Assert.Equal(1100, flourItem.GetProperty("quantidadeComprar").GetDecimal());
        var eggItem = items.First(i => i.GetProperty("ingredienteNome").GetString() == "Ovo");
        Assert.Equal(5, eggItem.GetProperty("quantidadeComprar").GetDecimal());
        Assert.Equal(1, response.GetProperty("ignorados").GetArrayLength());
    }

    [Fact]
    public async Task Recording_purchases_adds_stock_once_even_in_parallel()
    {
        var a = await Env.NewUser(F);
        var rice = await a.NewIngredient("Arroz", "G");
        var beans = await a.NewIngredient("Feijão", "G");
        var recipe = await a.NewRecipe("Prato", [Api.Line("Arroz", rice, 500, "G"), Api.Line("Feijão", beans, 300, "G")]);
        var created = await a.Ok(a.Post("/api/listas/de-receitas", new { nome = "Mercado", receitas = new[] { new { receitaId = recipe, porcoes = 4 } } }));
        var list = created.GetProperty("lista");
        var listId = list.GetProperty("id").GetGuid();
        var riceItem = list.GetProperty("itens").EnumerateArray().First(i => i.GetProperty("ingredienteNome").GetString() == "Arroz");
        var itemId = riceItem.GetProperty("id").GetGuid();

        await a.Ok(a.Patch($"/api/listas/{listId}/itens/{itemId}", new { texto = "Arroz", quantidadeComprar = 600, unidade = "G", comprado = true }));
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            a.Post($"/api/listas/{listId}/lancar-entradas", new { }, i % 2 == 0 ? "lanc" : $"lanc-{i}")));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Equal(600, await a.Total(rice));
        Assert.Equal(0, await a.Total(beans));

        var (edit, body, _) = await a.Patch($"/api/listas/{listId}/itens/{itemId}", new { texto = "Arroz", quantidadeComprar = 100, unidade = "G", comprado = true });
        Assert.Equal(HttpStatusCode.Conflict, edit);
        Assert.Equal("ITEM_JA_LANCADO", body.GetProperty("codigo").GetString());

        var beanItem = list.GetProperty("itens").EnumerateArray().First(i => i.GetProperty("ingredienteNome").GetString() == "Feijão").GetProperty("id").GetGuid();
        var explicitRun = await a.Ok(a.Post($"/api/listas/{listId}/lancar-entradas", new { itens = new[] { new { itemId = beanItem, dataValidade = "2027-05-01" } } }));
        Assert.Single(explicitRun.GetProperty("lancados").EnumerateArray());
        Assert.Equal(300, await a.Total(beans));
        Assert.Equal("comprada", explicitRun.GetProperty("status").GetString());
        var final = await a.Ok(a.Post($"/api/listas/{listId}/lancar-entradas", new { }));
        Assert.Empty(final.GetProperty("lancados").EnumerateArray());
        Assert.Equal(300, await a.Total(beans));
    }

    [Fact]
    public async Task Manual_lists_items_and_permissions()
    {
        var admin = await Env.NewUser(F);
        var reader = await Env.Join(F, admin, "leitor");
        var list = await admin.Ok(admin.Post("/api/listas", new { nome = "Feira" }));
        var id = list.GetProperty("id").GetGuid();
        await admin.Ok(admin.Post($"/api/listas/{id}/itens", new { texto = "Banana", quantidade = 6, unidade = "UN" }));
        var (bad, _, _) = await admin.Post($"/api/listas/{id}/itens", new { texto = "X", quantidade = 1, unidade = "NOPE" });
        Assert.Equal(HttpStatusCode.BadRequest, bad);

        var seen = await reader.Ok(reader.Get($"/api/listas/{id}"));
        Assert.Single(seen.GetProperty("itens").EnumerateArray());
        var (denied, _, _) = await reader.Post($"/api/listas/{id}/itens", new { texto = "Y", quantidade = 1, unidade = "UN" });
        Assert.Equal(HttpStatusCode.Forbidden, denied);
        await admin.Ok(admin.Delete($"/api/listas/{id}"));
    }

    [Fact]
    public async Task Catalogs_personal_vs_shared_visibility_and_duplication()
    {
        var admin = await Env.NewUser(F);
        var reader = await Env.Join(F, admin, "leitor", "Lia");
        var recipe = await admin.NewRecipe("Sopa", [Api.Line("Água", null, 1, "L")]);

        var shared = await admin.Ok(admin.Post("/api/catalogos", new { nome = "Família", visibilidade = "despensa" }));
        var personal = await admin.Ok(admin.Post("/api/catalogos", new { nome = "Só meu", visibilidade = "pessoal" }));
        var sharedId = shared.GetProperty("id").GetGuid();
        var personalId = personal.GetProperty("id").GetGuid();
        await admin.Ok(admin.Put($"/api/catalogos/{sharedId}/receitas/{recipe}"));
        await admin.Ok(admin.Put($"/api/catalogos/{personalId}/receitas/{recipe}"));

        var readerView = await reader.Ok(reader.Get("/api/catalogos"));
        var names = readerView.EnumerateArray().Select(c => c.GetProperty("nome").GetString()).ToList();
        Assert.Contains("Família", names);
        Assert.DoesNotContain("Só meu", names);
        var (hidden, _, _) = await reader.Get($"/api/catalogos/{personalId}");
        Assert.Equal(HttpStatusCode.NotFound, hidden);

        var (readerShared, _, _) = await reader.Post("/api/catalogos", new { nome = "Tentativa", visibilidade = "despensa" });
        Assert.Equal(HttpStatusCode.Forbidden, readerShared);
        var (readerEdit, _, _) = await reader.Put($"/api/catalogos/{sharedId}/receitas/{recipe}");
        Assert.Equal(HttpStatusCode.Forbidden, readerEdit);
        await reader.Ok(reader.Post("/api/catalogos", new { nome = "Da Lia", visibilidade = "pessoal" }));

        var copy = await reader.Ok(reader.Post($"/api/catalogos/{sharedId}/duplicar", new { nome = "Cópia da Lia", incluirReceitas = true }));
        Assert.Equal(1, copy.GetProperty("quantidadeReceitas").GetInt32());
        Assert.Equal("pessoal", copy.GetProperty("visibilidade").GetString());
        var (dup, _, _) = await reader.Post($"/api/catalogos/{sharedId}/duplicar", new { nome = "Cópia da Lia" });
        Assert.Equal(HttpStatusCode.Conflict, dup);
        var empty = await admin.Ok(admin.Post($"/api/catalogos/{personalId}/duplicar", new { visibilidade = "despensa" }));
        Assert.Equal(0, empty.GetProperty("quantidadeReceitas").GetInt32());

        var filtered = await admin.Ok(admin.Get($"/api/receitas?catalogoId={sharedId}"));
        Assert.Single(filtered.GetProperty("itens").EnumerateArray());
        var (foreign, _, _) = await reader.Get($"/api/receitas?catalogoId={personalId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign);

        await admin.Ok(admin.Delete($"/api/receitas/{recipe}"));
        var afterDelete = await admin.Ok(admin.Get($"/api/catalogos/{sharedId}"));
        Assert.Empty(afterDelete.GetProperty("receitas").EnumerateArray());
    }
}
