using System.Net;

namespace ReceitasMercado.IntegrationTests;

public class StockTests(FactoryFixture fx) : IClassFixture<FactoryFixture>
{
    private AppFactory F => fx.Factory;

    [Fact]
    public async Task Entries_convert_units_and_exits_follow_FEFO()
    {
        var a = await Env.NewUser(F);
        var ing = await a.NewIngredient("Arroz", "G");
        await a.Stock(ing, 1, "KG", "2027-06-01");
        await a.Stock(ing, 500, "G", "2027-01-01");
        await a.Stock(ing, 250, "G");
        Assert.Equal(1750, await a.Total(ing));

        var exit = await a.Ok(a.Post("/api/estoque/saidas", new { ingredienteId = ing, quantidade = 700, unidade = "G", motivo = "jantar" }));
        Assert.Equal(1050, exit.GetProperty("quantidadeTotal").GetDecimal());
        Assert.Equal(2, exit.GetProperty("movimentacoes").GetArrayLength());

        var detail = await a.Ok(a.Get($"/api/ingredientes/{ing}"));
        var lots = detail.GetProperty("lotes").EnumerateArray().ToList();
        Assert.Equal(2, lots.Count);
        Assert.Equal("2027-06-01", lots[0].GetProperty("dataValidade").GetString());
        Assert.Equal(800, lots[0].GetProperty("quantidade").GetDecimal());
        Assert.Equal(JsonValueKindNull, lots[1].GetProperty("dataValidade").ValueKind);
    }

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;

    [Fact]
    public async Task Exits_never_make_the_balance_negative_and_failures_change_nothing()
    {
        var a = await Env.NewUser(F);
        var ing = await a.NewIngredient("Leite", "ML");
        await a.Stock(ing, 1, "L");
        var (status, body, _) = await a.Post("/api/estoque/saidas", new { ingredienteId = ing, quantidade = 1.5, unidade = "L", motivo = "x" });
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("ESTOQUE_INSUFICIENTE", body.GetProperty("codigo").GetString());
        Assert.Equal(1000, await a.Total(ing));

        var (incompatible, inc, _) = await a.Post("/api/estoque/saidas", new { ingredienteId = ing, quantidade = 1, unidade = "KG", motivo = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, incompatible);
        Assert.Equal("UNIDADE_INCOMPATIVEL", inc.GetProperty("codigo").GetString());

        var (zero, _, _) = await a.Post("/api/estoque/saidas", new { ingredienteId = ing, quantidade = 0, unidade = "L", motivo = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, zero);
        var (noReason, _, _) = await a.Post("/api/estoque/saidas", new { ingredienteId = ing, quantidade = 1, unidade = "ML" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
    }

    [Fact]
    public async Task Parallel_exits_never_overdraw_the_stock()
    {
        var a = await Env.NewUser(F);
        var ing = await a.NewIngredient("Ovos", "UN");
        await a.Stock(ing, 6, "UN", "2027-03-01");
        await a.Stock(ing, 4, "UN", "2027-04-01");

        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ =>
            a.Post("/api/estoque/saidas", new { ingredienteId = ing, quantidade = 1, unidade = "UN", motivo = "teste" })));
        Assert.Equal(10, results.Count(r => r.Status == HttpStatusCode.OK));
        Assert.Equal(15, results.Count(r => r.Status == HttpStatusCode.Conflict));
        Assert.Equal(0, await a.Total(ing));

        var movements = await a.Ok(a.Get($"/api/estoque/movimentacoes?ingredienteId={ing}&limite=100"));
        var consumed = movements.GetProperty("itens").EnumerateArray().Where(m => m.GetProperty("tipo").GetString() == "consumo")
            .Sum(m => m.GetProperty("quantidade").GetDecimal());
        Assert.Equal(10, consumed);
    }

    [Fact]
    public async Task Idempotency_key_replays_the_same_response_without_duplicating_the_entry()
    {
        var a = await Env.NewUser(F);
        var ing = await a.NewIngredient("Açúcar", "G");
        var first = await a.Post("/api/estoque/entradas", new { ingredienteId = ing, quantidade = 100, unidade = "G" }, "chave-1");
        var second = await a.Post("/api/estoque/entradas", new { ingredienteId = ing, quantidade = 100, unidade = "G" }, "chave-1");
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(first.Body.ToString(), second.Body.ToString());
        Assert.Equal("true", second.Response.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(100, await a.Total(ing));

        var (conflict, body, _) = await a.Post("/api/estoque/entradas", new { ingredienteId = ing, quantidade = 999, unidade = "G" }, "chave-1");
        Assert.Equal(HttpStatusCode.Conflict, conflict);
        Assert.Equal("IDEMPOTENCY_KEY_REUTILIZADA", body.GetProperty("codigo").GetString());
        Assert.Equal(100, await a.Total(ing));
    }

    [Fact]
    public async Task Concurrent_requests_with_the_same_key_apply_once()
    {
        var a = await Env.NewUser(F);
        var ing = await a.NewIngredient("Sal", "G");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            a.Post("/api/estoque/entradas", new { ingredienteId = ing, quantidade = 50, unidade = "G" }, "mesma-chave")));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Equal(50, await a.Total(ing));
    }

    [Fact]
    public async Task Failed_requests_are_not_stored_so_the_same_key_can_be_retried()
    {
        var a = await Env.NewUser(F);
        var ing = await a.NewIngredient("Café", "G");
        var fail = await a.Post("/api/estoque/saidas", new { ingredienteId = ing, quantidade = 10, unidade = "G", motivo = "x" }, "retry");
        Assert.Equal(HttpStatusCode.Conflict, fail.Status);
        await a.Stock(ing, 100);
        var ok = await a.Post("/api/estoque/saidas", new { ingredienteId = ing, quantidade = 10, unidade = "G", motivo = "x" }, "retry");
        Assert.Equal(HttpStatusCode.OK, ok.Status);
        Assert.Equal(90, await a.Total(ing));
    }

    [Fact]
    public async Task Corrections_discarding_and_history_are_recorded()
    {
        var a = await Env.NewUser(F);
        var ing = await a.NewIngredient("Macarrão", "G");
        await a.Stock(ing, 500, expiry: "2027-02-01");
        await a.Ok(a.Post("/api/estoque/correcoes", new { ingredienteId = ing, delta = -100, motivo = "contagem" }));
        Assert.Equal(400, await a.Total(ing));
        await a.Ok(a.Post("/api/estoque/correcoes", new { ingredienteId = ing, delta = 50, motivo = "achei mais" }));
        Assert.Equal(450, await a.Total(ing));

        var lots = (await a.Ok(a.Get($"/api/ingredientes/{ing}"))).GetProperty("lotes").EnumerateArray().ToList();
        var main = lots.First(l => l.GetProperty("dataValidade").ValueKind != System.Text.Json.JsonValueKind.Null);
        await a.Ok(a.Post($"/api/lotes/{main.GetProperty("id").GetGuid()}/descartar", new { motivo = "mofou" }));
        Assert.Equal(50, await a.Total(ing));

        var history = await a.Ok(a.Get($"/api/estoque/movimentacoes?ingredienteId={ing}"));
        var types = history.GetProperty("itens").EnumerateArray().Select(m => m.GetProperty("tipo").GetString()).ToList();
        Assert.Contains("perda", types);
        Assert.Contains("correcao", types);
        Assert.Equal(4, types.Count);
    }

    [Fact]
    public async Task Movement_history_paginates_with_a_stable_cursor()
    {
        var a = await Env.NewUser(F);
        var ing = await a.NewIngredient("Farinha", "G");
        for (var i = 0; i < 7; i++) await a.Stock(ing, 10);
        var seen = new HashSet<Guid>();
        string? cursor = null;
        do
        {
            var page = await a.Ok(a.Get($"/api/estoque/movimentacoes?limite=3{(cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor))}"));
            foreach (var m in page.GetProperty("itens").EnumerateArray()) Assert.True(seen.Add(m.GetProperty("id").GetGuid()));
            cursor = page.GetProperty("proximoCursor").GetString();
        } while (cursor is not null);
        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public async Task Ingredient_rules_unique_names_archive_unit_lock_and_delete()
    {
        var a = await Env.NewUser(F);
        var ing = await a.NewIngredient("Tomate", "UN", 3);
        var cat = await a.Category();
        var (dup, body, _) = await a.Post("/api/ingredientes", new { nome = "  tomate ", categoriaId = cat, unidadeReferencia = "UN" });
        Assert.Equal(HttpStatusCode.Conflict, dup);
        Assert.Equal("NOME_DUPLICADO", body.GetProperty("codigo").GetString());

        await a.Stock(ing, 2);
        var list = await a.Ok(a.Get("/api/ingredientes?situacao=baixo"));
        Assert.Single(list.GetProperty("itens").EnumerateArray());

        var (unit, unitBody, _) = await a.Put($"/api/ingredientes/{ing}", new { nome = "Tomate", categoriaId = cat, unidadeReferencia = "G", quantidadeMinima = 3 });
        Assert.Equal(HttpStatusCode.Conflict, unit);
        Assert.Equal("UNIDADE_BLOQUEADA", unitBody.GetProperty("codigo").GetString());

        var (delete, deleteBody, _) = await a.Delete($"/api/ingredientes/{ing}");
        Assert.Equal(HttpStatusCode.Conflict, delete);
        Assert.Equal("INGREDIENTE_EM_USO", deleteBody.GetProperty("codigo").GetString());

        await a.Ok(a.Post($"/api/ingredientes/{ing}/arquivar"));
        var active = await a.Ok(a.Get("/api/ingredientes"));
        Assert.Empty(active.GetProperty("itens").EnumerateArray());
        var (blocked, _, _) = await a.Post("/api/estoque/entradas", new { ingredienteId = ing, quantidade = 1, unidade = "UN" });
        Assert.Equal(HttpStatusCode.Conflict, blocked);
        await a.Ok(a.Post($"/api/ingredientes/{ing}/reativar"));
        await a.Stock(ing, 1);

        var unused = await a.NewIngredient("Pimenta", "G");
        await a.Ok(a.Delete($"/api/ingredientes/{unused}"));
    }

    [Fact]
    public async Task Summary_flags_expired_and_expiring_lots_using_the_pantry_timezone()
    {
        var a = await Env.NewUser(F);
        var ing = await a.NewIngredient("Iogurte", "UN", 5);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await a.Stock(ing, 1, expiry: today.AddDays(-3).ToString("yyyy-MM-dd"));
        await a.Stock(ing, 1, expiry: today.AddDays(2).ToString("yyyy-MM-dd"));
        await a.Stock(ing, 1, expiry: today.AddDays(60).ToString("yyyy-MM-dd"));

        var summary = await a.Ok(a.Get("/api/estoque/resumo"));
        Assert.Equal(1, summary.GetProperty("lotesVencidos").GetInt32());
        Assert.Equal(1, summary.GetProperty("lotesVencendo").GetInt32());
        Assert.Equal(1, summary.GetProperty("abaixoDoMinimo").GetInt32());
        Assert.Equal(2, summary.GetProperty("proximosVencimentos").GetArrayLength());

        var expired = await a.Ok(a.Get("/api/ingredientes?situacao=vencido"));
        Assert.Single(expired.GetProperty("itens").EnumerateArray());
    }

    [Fact]
    public async Task Categories_and_equivalence_groups_are_managed_per_pantry()
    {
        var a = await Env.NewUser(F);
        var created = await a.Ok(a.Post("/api/categorias", new { nome = "Padaria" }));
        var (dup, _, _) = await a.Post("/api/categorias", new { nome = "padaria" });
        Assert.Equal(HttpStatusCode.Conflict, dup);

        var group = await a.Ok(a.Post("/api/grupos-equivalencia", new { nome = "Açúcares" }));
        var sugar = await a.NewIngredient("Açúcar refinado", "G", groupId: group.GetProperty("id").GetGuid());
        var groups = await a.Ok(a.Get("/api/grupos-equivalencia"));
        Assert.Equal(sugar, groups.EnumerateArray().First(g => g.GetProperty("nome").GetString() == "Açúcares")
            .GetProperty("ingredientes")[0].GetProperty("id").GetGuid());

        var inUse = await a.Category("Grãos e cereais");
        await a.Ok(a.Put($"/api/ingredientes/{sugar}", new { nome = "Açúcar refinado", categoriaId = inUse, unidadeReferencia = "G" }));
        var (blocked, _, _) = await a.Delete($"/api/categorias/{inUse}");
        Assert.Equal(HttpStatusCode.Conflict, blocked);
        await a.Ok(a.Delete($"/api/categorias/{created.GetProperty("id").GetGuid()}"));
    }
}
