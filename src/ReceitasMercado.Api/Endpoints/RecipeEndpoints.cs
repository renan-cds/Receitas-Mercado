using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Contracts;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Api.Integrations;
using ReceitasMercado.Api.Services;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Endpoints;

public static class RecipeEndpoints
{
    private static int StateRank(CompatibilityState state) => state switch
    {
        CompatibilityState.PodePreparar => 0,
        CompatibilityState.VerificarIngredientes => 1,
        CompatibilityState.FaltamIngredientes => 2,
        _ => 3,
    };

    private static CompatibilityState? ParseState(string? raw) => (raw ?? "").ToLowerInvariant() switch
    {
        "" => null,
        "pode" or "pode_preparar" => CompatibilityState.PodePreparar,
        "faltam" or "faltam_ingredientes" => CompatibilityState.FaltamIngredientes,
        "verificar" or "verificar_ingredientes" => CompatibilityState.VerificarIngredientes,
        "rascunho" => CompatibilityState.Rascunho,
        _ => throw BusinessException.BadRequest("FILTRO_INVALIDO", "Estado de compatibilidade desconhecido."),
    };

    public static async Task<IQueryable<Receita>> VisibleCatalogFilter(
        IQueryable<Receita> query, Guid catalogId, TenantContext t, AppDbContext db, CancellationToken ct)
    {
        var visible = await db.Catalogos.AsNoTracking().AnyAsync(c => c.Id == catalogId && c.DespensaId == t.DespensaId
            && (c.Visibilidade == VisibilidadeCatalogo.Despensa || c.UsuarioId == t.UsuarioId), ct);
        if (!visible) throw BusinessException.NotFound("Catálogo");
        return query.Where(r => db.CatalogoReceitas.Any(cr => cr.CatalogoId == catalogId && cr.ReceitaId == r.Id));
    }

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("").RequireAuthorization().AddEndpointFilter<ValidationFilter>();

        g.MapGet("/receitas", async (
            TenantContext t, AppDbContext db, RecipeService recipes, StockService stock, CancellationToken ct,
            string? busca, string? tag, string? status, string? origem, bool? favoritas, Guid? catalogoId,
            string? compat, string? ordem, int? limite, string? cursor) =>
        {
            if (!PantryClock.TryParseCursor(cursor, out var offset))
                throw BusinessException.BadRequest("CURSOR_INVALIDO", "Cursor inválido.");
            var take = Math.Clamp(limite ?? 24, 1, 100);
            var stateFilter = ParseState(compat);

            var query = db.Receitas.AsNoTracking().Where(r => r.DespensaId == t.DespensaId);
            if (!string.IsNullOrWhiteSpace(busca))
            {
                foreach (var word in TextNormalizer.Normalize(busca).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(8))
                {
                    var w = word;
                    query = query.Where(r => r.BuscaTexto.Contains(w));
                }
            }
            if (!string.IsNullOrWhiteSpace(tag))
            {
                var normalizedTag = TextNormalizer.Normalize(tag);
                query = query.Where(r => r.Tags.Any(x => x.Tag == normalizedTag));
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<StatusReceita>(status, true, out var st))
                    throw BusinessException.BadRequest("FILTRO_INVALIDO", "Status desconhecido.");
                query = query.Where(r => r.Status == st);
            }
            if (!string.IsNullOrWhiteSpace(origem))
            {
                if (!Enum.TryParse<OrigemReceita>(origem, true, out var og))
                    throw BusinessException.BadRequest("FILTRO_INVALIDO", "Origem desconhecida.");
                query = query.Where(r => r.Origem == og);
            }
            if (favoritas == true)
                query = query.Where(r => db.Favoritos.Any(f => f.UsuarioId == t.UsuarioId && f.ReceitaId == r.Id));
            if (catalogoId is { } catalog)
                query = await VisibleCatalogFilter(query, catalog, t, db, ct);

            var needsCompatFirst = stateFilter is not null || string.Equals(ordem, "compat", StringComparison.OrdinalIgnoreCase);
            var sorted = string.Equals(ordem, "titulo", StringComparison.OrdinalIgnoreCase)
                ? query.OrderBy(r => r.Titulo).ThenBy(r => r.Id)
                : query.OrderByDescending(r => r.AtualizadoEm).ThenBy(r => r.Id);

            var stockEntries = await stock.LoadStock(t.DespensaId, ct);
            var info = await recipes.IngredientInfo(t.DespensaId, ct);

            List<Receita> page;
            bool hasMore;
            Dictionary<Guid, CompatDto> compatById;
            if (needsCompatFirst)
            {
                var all = await sorted.Include(r => r.Ingredientes).Include(r => r.Tags).AsSplitQuery().ToListAsync(ct);
                var analyzed = all.Select(r => (Receita: r, Compat: recipes.Analyze(r, r.Porcoes, stockEntries, info))).ToList();
                if (stateFilter is { } wanted) analyzed = analyzed.Where(x => x.Compat.Estado == wanted).ToList();
                if (string.Equals(ordem, "compat", StringComparison.OrdinalIgnoreCase))
                    analyzed = analyzed.OrderBy(x => StateRank(x.Compat.Estado)).ThenByDescending(x => x.Compat.Percentual)
                        .ThenBy(x => x.Receita.Titulo, StringComparer.CurrentCultureIgnoreCase).ToList();
                var slice = analyzed.Skip(offset).Take(take + 1).ToList();
                hasMore = slice.Count > take;
                page = slice.Take(take).Select(x => x.Receita).ToList();
                compatById = slice.Take(take).ToDictionary(x => x.Receita.Id, x => x.Compat);
            }
            else
            {
                var ids = await sorted.Skip(offset).Take(take + 1).Select(r => r.Id).ToListAsync(ct);
                hasMore = ids.Count > take;
                ids = ids.Take(take).ToList();
                var loaded = await db.Receitas.AsNoTracking().Include(r => r.Ingredientes).Include(r => r.Tags)
                    .AsSplitQuery().Where(r => ids.Contains(r.Id)).ToListAsync(ct);
                page = ids.Select(id => loaded.First(r => r.Id == id)).ToList();
                compatById = page.ToDictionary(r => r.Id, r => recipes.Analyze(r, r.Porcoes, stockEntries, info));
            }

            var pageIds = page.Select(r => r.Id).ToArray();
            var favs = (await db.Favoritos.AsNoTracking().Where(f => f.UsuarioId == t.UsuarioId && pageIds.Contains(f.ReceitaId))
                .Select(f => f.ReceitaId).ToListAsync(ct)).ToHashSet();

            return Results.Ok(new
            {
                itens = page.Select(r =>
                {
                    var c = compatById[r.Id];
                    return new
                    {
                        r.Id, r.Titulo, r.Descricao, r.Porcoes, r.TempoPreparoMin, r.Dificuldade, r.Status, r.Origem,
                        r.ImagemArquivoId, r.ImagemUrl, r.AtualizadoEm,
                        tags = r.Tags.Select(x => x.Tag).OrderBy(x => x).ToList(),
                        favorito = favs.Contains(r.Id),
                        compatibilidade = new { estado = c.Estado, percentual = c.Percentual, obrigatoriosTotal = c.ObrigatoriosTotal, obrigatoriosOk = c.ObrigatoriosOk },
                    };
                }),
                proximoCursor = hasMore ? (offset + take).ToString() : null,
            });
        }).Leitor();

        g.MapGet("/receitas/tags", async (TenantContext t, AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ReceitaTags.AsNoTracking()
                .Where(x => db.Receitas.Any(r => r.Id == x.ReceitaId && r.DespensaId == t.DespensaId))
                .GroupBy(x => x.Tag).Select(x => new { tag = x.Key, quantidade = x.Count() })
                .OrderByDescending(x => x.quantidade).ThenBy(x => x.tag).Take(100).ToListAsync(ct))).Leitor();

        g.MapGet("/receitas/{id:guid}", async (Guid id, TenantContext t, AppDbContext db, RecipeService recipes, CancellationToken ct) =>
        {
            var r = await recipes.Load(t.DespensaId, id, ct);
            var fav = await db.Favoritos.AnyAsync(f => f.UsuarioId == t.UsuarioId && f.ReceitaId == id, ct);
            return Results.Ok(RecipeService.ToDetail(r, fav));
        }).Leitor();

        g.MapGet("/receitas/{id:guid}/compatibilidade", async (
            Guid id, TenantContext t, RecipeService recipes, StockService stock, CancellationToken ct,
            decimal? porcoes, bool? incluirOpcionais) =>
        {
            var r = await recipes.Load(t.DespensaId, id, ct);
            var servings = porcoes ?? r.Porcoes;
            if (servings is < 0.1m or > 500m)
                throw BusinessException.BadRequest("PORCOES_INVALIDAS", "Informe de 0,1 a 500 porções.");
            var entries = await stock.LoadStock(t.DespensaId, ct);
            var info = await recipes.IngredientInfo(t.DespensaId, ct);
            return Results.Ok(recipes.Analyze(r, servings, entries, info, incluirOpcionais ?? true));
        }).Leitor();

        g.MapPost("/receitas", async (ReceitaInput req, TenantContext t, RecipeService recipes, CancellationToken ct) =>
        {
            var r = await recipes.Create(t.DespensaId, t.UsuarioId, req, ct);
            return Results.Created($"/api/receitas/{r.Id}", RecipeService.ToDetail(r, false));
        }).Editor().Transactional();

        g.MapPut("/receitas/{id:guid}", async (Guid id, ReceitaInput req, TenantContext t, AppDbContext db, RecipeService recipes, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var r = await recipes.Update(t.DespensaId, id, req, ct);
            await tx.CommitAsync(ct);
            var fav = await db.Favoritos.AnyAsync(f => f.UsuarioId == t.UsuarioId && f.ReceitaId == id, ct);
            return Results.Ok(RecipeService.ToDetail(r, fav));
        }).Editor();

        g.MapDelete("/receitas/{id:guid}", async (Guid id, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var r = await db.Receitas.FirstOrDefaultAsync(x => x.Id == id && x.DespensaId == t.DespensaId, ct)
                ?? throw BusinessException.NotFound("Receita");
            db.Receitas.Remove(r);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).Editor();

        g.MapPost("/receitas/{id:guid}/duplicar", async (Guid id, TenantContext t, RecipeService recipes, CancellationToken ct) =>
        {
            var source = await recipes.Load(t.DespensaId, id, ct);
            var copy = new ReceitaInput
            {
                Titulo = TruncateTitle(source.Titulo), Descricao = source.Descricao, Porcoes = source.Porcoes,
                TempoPreparoMin = source.TempoPreparoMin, Dificuldade = source.Dificuldade, Status = source.Status,
                Origem = OrigemReceita.Manual, AutorAtribuicao = source.AutorAtribuicao, ImagemArquivoId = source.ImagemArquivoId,
                ImagemUrl = source.ImagemUrl, Observacoes = source.Observacoes, UrlOrigem = source.UrlOrigem,
                Tags = source.Tags.Select(x => x.Tag).ToList(),
                Etapas = source.Etapas.OrderBy(e => e.Ordem).Select(e => e.Instrucao).ToList(),
                Ingredientes = source.Ingredientes.OrderBy(i => i.Ordem).Select(i => new ReceitaIngredienteInput
                {
                    Nome = i.Nome, TextoOriginal = i.TextoOriginal, IngredienteId = i.IngredienteId, Quantidade = i.Quantidade,
                    Unidade = i.UnidadeCodigo, Opcional = i.Opcional, Confianca = i.Confianca,
                }).ToList(),
            };
            var created = await recipes.Create(t.DespensaId, t.UsuarioId, copy, ct);
            return Results.Created($"/api/receitas/{created.Id}", RecipeService.ToDetail(created, false));
        }).Editor();

        g.MapPut("/receitas/{id:guid}/favorito", async (Guid id, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Receitas.AnyAsync(r => r.Id == id && r.DespensaId == t.DespensaId, ct))
                throw BusinessException.NotFound("Receita");
            if (!await db.Favoritos.AnyAsync(f => f.UsuarioId == t.UsuarioId && f.ReceitaId == id, ct))
            {
                db.Favoritos.Add(new Favorito { UsuarioId = t.UsuarioId, ReceitaId = id });
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { db.ChangeTracker.Clear(); }
            }
            return Results.Ok(new { favorito = true });
        }).Leitor();

        g.MapDelete("/receitas/{id:guid}/favorito", async (Guid id, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            await db.Favoritos.Where(f => f.UsuarioId == t.UsuarioId && f.ReceitaId == id
                && db.Receitas.Any(r => r.Id == id && r.DespensaId == t.DespensaId)).ExecuteDeleteAsync(ct);
            return Results.Ok(new { favorito = false });
        }).Leitor();

        MapImport(g);
    }

    private static string TruncateTitle(string title)
    {
        const string suffix = " (cópia)";
        return (title.Length > 200 - suffix.Length ? title[..(200 - suffix.Length)] : title) + suffix;
    }

    private static BusinessException ToBusiness(ImportException ex) => ex.Code switch
    {
        var c when c == UrlGuard.BlockedCode => BusinessException.BadRequest(c, ex.Message),
        "BUSCA_INDISPONIVEL" => new BusinessException(503, ex.Code, ex.Message),
        "TEMPO_ESGOTADO" or "FALHA_DE_REDE" or "PAGINA_INDISPONIVEL" => new BusinessException(502, ex.Code, ex.Message),
        _ => new BusinessException(422, ex.Code, ex.Message),
    };

    private static void MapImport(RouteGroupBuilder g)
    {
        g.MapPost("/receitas/importar-url", async (
            ImportarUrlRequest req, TenantContext t, IPageFetcher fetcher, DraftBuilder drafts, HourlyLimiter limiter, CancellationToken ct) =>
        {
            if (!limiter.TryAcquire($"import:{t.UsuarioId}", 30))
                throw new BusinessException(429, "LIMITE_IMPORTACAO", "Limite de 30 importações por hora atingido. Tente mais tarde.");
            var error = UrlGuard.ValidateUrl(req.Url, out var uri);
            if (error is not null || uri is null)
                throw BusinessException.BadRequest(UrlGuard.BlockedCode, error ?? "Endereço inválido.");

            FetchedPage page;
            try { page = await fetcher.FetchAsync(uri, ct); }
            catch (ImportException ex) { throw ToBusiness(ex); }

            var parsed = JsonLdRecipeParser.Parse(page.Body, page.FinalUri.ToString());
            if (parsed is null)
                throw new BusinessException(422, "RECEITA_NAO_ENCONTRADA",
                    "Não encontramos uma receita estruturada nesta página. Você pode cadastrá-la manualmente.");
            var candidates = await drafts.Candidates(t.DespensaId, ct);
            return Results.Ok(drafts.FromImported(parsed, OrigemReceita.Url, candidates));
        }).Editor();

        g.MapGet("/receitas/busca-externa", async (
            string q, TenantContext t, IRecipeSearchClient search, DraftBuilder drafts, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2 || q.Length > 80)
                throw BusinessException.BadRequest("VALIDACAO", "Informe de 2 a 80 caracteres para buscar.");
            IReadOnlyList<ImportedRecipe> found;
            try { found = await search.SearchAsync(q, ct); }
            catch (ImportException ex) { throw ToBusiness(ex); }
            var candidates = await drafts.Candidates(t.DespensaId, ct);
            return Results.Ok(new
            {
                fonte = new { codigo = "themealdb", nome = "TheMealDB", url = "https://www.themealdb.com" },
                aviso = "Receitas em inglês de uma fonte externa gratuita. Revise ingredientes e quantidades antes de salvar.",
                itens = found.Take(12).Select(r => drafts.FromImported(r, OrigemReceita.Busca, candidates)),
            });
        }).Editor();
    }
}
