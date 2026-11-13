using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Contracts;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Api.Services;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Endpoints;

public static partial class AiEndpoints
{
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    private static string Clean(string? text, int max)
    {
        var cleaned = Tags().Replace(text ?? "", " ").Replace('\n', ' ').Trim();
        return cleaned.Length > max ? cleaned[..max] : cleaned;
    }

    private static JsonDocument ParseObject(string raw)
    {
        try
        {
            var doc = JsonDocument.Parse(AiRecipeValidator.StripCodeFences(raw), new JsonDocumentOptions { MaxDepth = 16, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                doc.Dispose();
                throw Invalid();
            }
            return doc;
        }
        catch (JsonException)
        {
            throw Invalid();
        }
    }

    private static BusinessException Invalid() => new(422, "IA_RESPOSTA_INVALIDA",
        "A IA devolveu uma resposta fora do formato esperado e ela foi descartada. Tente novamente.");

    private static void ValidateBaseUrl(string raw)
    {
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw BusinessException.BadRequest("URL_INVALIDA", "A URL base deve ser http(s), sem usuário, parâmetros ou fragmento.");
    }

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/ia").RequireAuthorization().AddEndpointFilter<ValidationFilter>();

        g.MapGet("/status", async (TenantContext t, AiService ai, CancellationToken ct) =>
        {
            var c = await ai.GetConfig(t.DespensaId, ct);
            return Results.Ok(new { disponivel = c is { Ativa: true } && !string.IsNullOrWhiteSpace(c.BaseUrl) && !string.IsNullOrWhiteSpace(c.Modelo) });
        }).Leitor();

        g.MapGet("/config", async (TenantContext t, AiService ai, CancellationToken ct) =>
        {
            var c = await ai.GetConfig(t.DespensaId, ct);
            return Results.Ok(c is null
                ? new { configurada = false, ativa = false, baseUrl = "", modelo = "", temChave = false, limiteHora = 20 }
                : new { configurada = true, ativa = c.Ativa, baseUrl = c.BaseUrl, modelo = c.Modelo, temChave = c.ApiKeyCifrada is { Length: > 0 }, limiteHora = c.LimiteHora });
        }).Admin();

        g.MapPut("/config", async (IaConfigRequest req, TenantContext t, AppDbContext db, AiService ai, TimeProvider clock, CancellationToken ct) =>
        {
            ValidateBaseUrl(req.BaseUrl);
            var c = await ai.GetConfig(t.DespensaId, ct);
            if (c is null) db.ConfiguracoesIa.Add(c = new ConfiguracaoIa { DespensaId = t.DespensaId });
            c.BaseUrl = req.BaseUrl.Trim().TrimEnd('/');
            c.Modelo = req.Modelo.Trim();
            c.Ativa = req.Ativa;
            c.LimiteHora = req.LimiteHora;
            if (req.RemoverChave) c.ApiKeyCifrada = null;
            else if (!string.IsNullOrWhiteSpace(req.ApiKey)) c.ApiKeyCifrada = ai.Protect(req.ApiKey.Trim());
            c.AtualizadoPorUsuarioId = t.UsuarioId;
            c.AtualizadoEm = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { configurada = true, ativa = c.Ativa, baseUrl = c.BaseUrl, modelo = c.Modelo, temChave = c.ApiKeyCifrada is { Length: > 0 }, limiteHora = c.LimiteHora });
        }).Admin();

        g.MapDelete("/config", async (TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            await db.ConfiguracoesIa.Where(c => c.DespensaId == t.DespensaId).ExecuteDeleteAsync(ct);
            return Results.Ok(new { ok = true });
        }).Admin();

        g.MapPost("/testar", async (TenantContext t, AiService ai, CancellationToken ct) =>
        {
            var raw = await ai.Run(t, TipoExecucaoIa.Teste, "Responda somente com o JSON {\"ok\":true}.", "teste de conexão", ct, countsTowardLimit: false);
            return Results.Ok(new { ok = true, resposta = Clean(raw, 120) });
        }).Admin();

        g.MapPost("/sugerir-receitas", async (IaSugerirRequest req, TenantContext t, AppDbContext db, AiService ai, DraftBuilder drafts, TimeProvider clock, CancellationToken ct) =>
        {
            var settings = await db.Despensas.AsNoTracking().Where(d => d.Id == t.DespensaId).Select(d => new { d.Fuso }).FirstAsync(ct);
            var stock = await db.ItensEstoque.AsNoTracking()
                .Where(i => i.DespensaId == t.DespensaId && i.QuantidadeTotal > 0 && i.Ingrediente!.ArquivadoEm == null)
                .Select(i => new
                {
                    nome = i.Ingrediente!.Nome, quantidade = i.QuantidadeTotal, unidade = i.Ingrediente.UnidadeReferenciaCodigo,
                    validade = i.Lotes.Where(l => l.Status == StatusLote.Aberto && l.DataValidade != null).Min(l => (DateOnly?)l.DataValidade),
                }).ToListAsync(ct);
            if (stock.Count == 0)
                throw BusinessException.Conflict("ESTOQUE_VAZIO", "Cadastre ao menos um ingrediente em estoque para receber sugestões.");
            var items = (req.PriorizarValidade
                    ? stock.OrderBy(s => s.validade == null).ThenBy(s => s.validade)
                    : stock.OrderBy(s => s.nome))
                .Take(40).ToList();
            var payload = AiService.UserPayload(new
            {
                tarefa = $"Sugira {req.Quantidade} receitas usando principalmente estes itens em estoque. Priorize os que vencem antes.",
                hoje = PantryClock.Today(settings.Fuso, clock.GetUtcNow()).ToString("yyyy-MM-dd"),
                observacoes = Clean(req.Observacoes, 300),
                estoque = items,
            });
            var result = await ai.RunRecipes(t, TipoExecucaoIa.SugerirReceitas, payload, req.Quantidade, ct);
            var candidates = await drafts.Candidates(t.DespensaId, ct);
            return Results.Ok(new { itens = result.Recipes.Select(r => drafts.FromAi(r, candidates)) });
        }).Editor();

        g.MapPost("/gerar-receita", async (IaGerarRequest req, TenantContext t, AiService ai, DraftBuilder drafts, CancellationToken ct) =>
        {
            var names = req.Ingredientes.Select(i => Clean(i, 80)).Where(i => i.Length > 0).Distinct().ToList();
            if (names.Count == 0) throw BusinessException.BadRequest("VALIDACAO", "Informe ao menos um ingrediente.");
            var payload = AiService.UserPayload(new
            {
                tarefa = "Crie uma receita completa usando os ingredientes informados (pode incluir básicos como sal e água).",
                ingredientes = names, porcoes = req.Porcoes, observacoes = Clean(req.Observacoes, 300),
            });
            var result = await ai.RunRecipes(t, TipoExecucaoIa.GerarReceita, payload, 1, ct);
            var candidates = await drafts.Candidates(t.DespensaId, ct);
            return Results.Ok(drafts.FromAi(result.Recipes[0], candidates));
        }).Editor();

        g.MapPost("/normalizar-ingredientes", async (IaNormalizarRequest req, TenantContext t, AiService ai, DraftBuilder drafts, CancellationToken ct) =>
        {
            var lines = req.Linhas.Select(l => Clean(l, 200)).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) throw BusinessException.BadRequest("VALIDACAO", "Informe ao menos uma linha.");
            var system = AiService.SystemPromptBase +
                " Formato: {\"linhas\":[{\"nome\":string,\"quantidade\":number|null,\"unidade\":string|null,\"opcional\":boolean}]} na mesma ordem das linhas recebidas.";
            var raw = await ai.Run(t, TipoExecucaoIa.NormalizarIngredientes, system, AiService.UserPayload(new { linhas = lines }), ct);
            using var doc = ParseObject(raw);
            if (!doc.RootElement.TryGetProperty("linhas", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() != lines.Count)
            {
                await ai.MarkInvalid(t.DespensaId, "normalizar: contagem de linhas divergente");
                throw Invalid();
            }
            var candidates = await drafts.Candidates(t.DespensaId, ct);
            var result = new List<object>();
            var i = 0;
            foreach (var el in arr.EnumerateArray())
            {
                var name = el.ValueKind == JsonValueKind.Object && el.TryGetProperty("nome", out var n) && n.ValueKind == JsonValueKind.String
                    ? Clean(n.GetString(), 120) : "";
                decimal? qty = null;
                if (el.TryGetProperty("quantidade", out var q) && q.ValueKind == JsonValueKind.Number && q.TryGetDecimal(out var dq))
                {
                    if (dq < 0 || dq > 100_000)
                    {
                        await ai.MarkInvalid(t.DespensaId, "normalizar: quantidade implausível");
                        throw Invalid();
                    }
                    qty = dq;
                }
                string? unit = null;
                if (el.TryGetProperty("unidade", out var u) && u.ValueKind == JsonValueKind.String)
                {
                    var resolved = IngredientTextParser.ResolveUnit(u.GetString() ?? "");
                    unit = resolved is not null && UnitCatalog.Exists(resolved) ? resolved : null;
                }
                var optional = el.TryGetProperty("opcional", out var o) && o.ValueKind == JsonValueKind.True;
                if (name.Length == 0)
                {
                    await ai.MarkInvalid(t.DespensaId, "normalizar: nome vazio");
                    throw Invalid();
                }
                result.Add(DraftBuilder.Line(name, lines[i], qty, qty is null ? null : unit ?? "UN", optional, candidates, capAtInferred: true));
                i++;
            }
            return Results.Ok(new { linhas = result, aviso = "Interpretação por IA: confira quantidades, unidades e vínculos antes de salvar." });
        }).Editor();

        g.MapPost("/substituicoes", async (IaSubstituicoesRequest req, TenantContext t, AppDbContext db, AiService ai, RecipeService recipes, CancellationToken ct) =>
        {
            string? context = null;
            if (req.ReceitaId is { } rid) context = (await recipes.Load(t.DespensaId, rid, ct)).Titulo;
            var system = AiService.SystemPromptBase +
                " Formato: {\"substituicoes\":[{\"nome\":string,\"proporcao\":string,\"observacao\":string}]} com até 5 itens.";
            var raw = await ai.Run(t, TipoExecucaoIa.Substituicoes, system,
                AiService.UserPayload(new { ingrediente = Clean(req.Ingrediente, 120), receita = context }), ct);
            using var doc = ParseObject(raw);
            if (!doc.RootElement.TryGetProperty("substituicoes", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                await ai.MarkInvalid(t.DespensaId, "substituicoes: formato");
                throw Invalid();
            }
            var inStock = await db.ItensEstoque.AsNoTracking()
                .Where(i => i.DespensaId == t.DespensaId && i.QuantidadeTotal > 0)
                .Select(i => i.Ingrediente!.Nome).ToListAsync(ct);
            var normalizedStock = inStock.Select(TextNormalizer.Normalize).ToHashSet();
            var items = new List<object>();
            foreach (var el in arr.EnumerateArray().Take(5))
            {
                if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("nome", out var n) || n.ValueKind != JsonValueKind.String) continue;
                var name = Clean(n.GetString(), 120);
                if (name.Length == 0) continue;
                items.Add(new
                {
                    nome = name,
                    proporcao = el.TryGetProperty("proporcao", out var p) && p.ValueKind == JsonValueKind.String ? Clean(p.GetString(), 120) : null,
                    observacao = el.TryGetProperty("observacao", out var ob) && ob.ValueKind == JsonValueKind.String ? Clean(ob.GetString(), 300) : null,
                    temEmEstoque = normalizedStock.Contains(TextNormalizer.Normalize(name)),
                });
            }
            if (items.Count == 0)
            {
                await ai.MarkInvalid(t.DespensaId, "substituicoes: vazio");
                throw Invalid();
            }
            return Results.Ok(new { itens = items, aviso = "Sugestões de IA: o sabor e a textura podem mudar. Elas não alteram a receita automaticamente." });
        }).Editor();

        g.MapPost("/adaptar", async (IaAdaptarRequest req, TenantContext t, AiService ai, RecipeService recipes, CancellationToken ct) =>
        {
            var r = await recipes.Load(t.DespensaId, req.ReceitaId, ct);
            var scaled = r.Ingredientes.OrderBy(i => i.Ordem).Select(i => new
            {
                nome = i.Nome,
                quantidade = i.Quantidade.HasValue ? RecipeScaler.Scale(i.Quantidade.Value, r.Porcoes, req.Porcoes) : (decimal?)null,
                unidade = i.UnidadeCodigo,
            }).ToList();
            var steps = r.Etapas.OrderBy(e => e.Ordem).Select(e => e.Instrucao).ToList();
            var system = AiService.SystemPromptBase + " Formato: {\"etapas\":[string],\"observacoes\":[string]}. Não invente ingredientes novos.";
            var raw = await ai.Run(t, TipoExecucaoIa.AdaptarEtapas, system, AiService.UserPayload(new
            {
                tarefa = $"Ajuste as etapas e tempos para {req.Porcoes} porções (original: {r.Porcoes}).",
                titulo = r.Titulo, ingredientes = scaled, etapas = req.ReescreverEtapas ? steps : steps.Take(30).ToList(),
            }), ct);
            using var doc = ParseObject(raw);
            if (!doc.RootElement.TryGetProperty("etapas", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() is 0 or > 40)
            {
                await ai.MarkInvalid(t.DespensaId, "adaptar: formato");
                throw Invalid();
            }
            var etapas = arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => Clean(e.GetString(), 1000)).Where(e => e.Length > 0).ToList();
            if (etapas.Count == 0)
            {
                await ai.MarkInvalid(t.DespensaId, "adaptar: vazio");
                throw Invalid();
            }
            var notes = doc.RootElement.TryGetProperty("observacoes", out var obs) && obs.ValueKind == JsonValueKind.Array
                ? obs.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => Clean(e.GetString(), 300)).Where(e => e.Length > 0).Take(5).ToList()
                : [];
            return Results.Ok(new
            {
                porcoes = req.Porcoes, ingredientes = scaled, etapasSugeridas = etapas, observacoes = notes,
                aviso = "Sugestão de IA: revise antes de usar. A receita original não foi alterada.",
            });
        }).Editor();
    }
}
