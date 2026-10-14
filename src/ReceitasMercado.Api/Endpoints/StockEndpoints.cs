using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Contracts;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Api.Services;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Endpoints;

public static class StockEndpoints
{
    private static string Norm(string s) => TextNormalizer.Normalize(s);

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("").RequireAuthorization().AddEndpointFilter<ValidationFilter>();

        g.MapGet("/unidades", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Unidades.AsNoTracking().OrderBy(u => u.Dimensao).ThenBy(u => u.Codigo)
                .Select(u => new { u.Codigo, u.Nome, u.Abreviacao, dimensao = u.Dimensao, u.Aproximada }).ToListAsync(ct)));

        MapCategories(g);
        MapGroups(g);
        MapIngredients(g);
        MapMovements(g);
        MapLots(g);
    }

    private static void MapCategories(RouteGroupBuilder g)
    {
        g.MapGet("/categorias", async (TenantContext t, AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Categorias.AsNoTracking().Where(c => c.DespensaId == t.DespensaId)
                .OrderBy(c => c.Ordem).ThenBy(c => c.Nome)
                .Select(c => new { c.Id, c.Nome, c.Ordem, ingredientes = db.Ingredientes.Count(i => i.CategoriaId == c.Id && i.ArquivadoEm == null) })
                .ToListAsync(ct))).Leitor();

        g.MapPost("/categorias", async (NomeRequest req, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var norm = Norm(req.Nome);
            if (await db.Categorias.AnyAsync(c => c.DespensaId == t.DespensaId && c.NomeNormalizado == norm, ct))
                throw BusinessException.Conflict("NOME_DUPLICADO", "Já existe uma categoria com este nome.");
            var order = (await db.Categorias.Where(c => c.DespensaId == t.DespensaId).MaxAsync(c => (int?)c.Ordem, ct) ?? -1) + 1;
            var c = new CategoriaIngrediente { DespensaId = t.DespensaId, Nome = req.Nome.Trim(), NomeNormalizado = norm, Ordem = order };
            db.Categorias.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/categorias/{c.Id}", new { c.Id, c.Nome, c.Ordem });
        }).Editor();

        g.MapPatch("/categorias/{id:guid}", async (Guid id, NomeRequest req, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var c = await db.Categorias.FirstOrDefaultAsync(x => x.Id == id && x.DespensaId == t.DespensaId, ct)
                ?? throw BusinessException.NotFound("Categoria");
            var norm = Norm(req.Nome);
            if (await db.Categorias.AnyAsync(x => x.DespensaId == t.DespensaId && x.NomeNormalizado == norm && x.Id != id, ct))
                throw BusinessException.Conflict("NOME_DUPLICADO", "Já existe uma categoria com este nome.");
            c.Nome = req.Nome.Trim();
            c.NomeNormalizado = norm;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.Id, c.Nome, c.Ordem });
        }).Editor();

        g.MapDelete("/categorias/{id:guid}", async (Guid id, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var c = await db.Categorias.FirstOrDefaultAsync(x => x.Id == id && x.DespensaId == t.DespensaId, ct)
                ?? throw BusinessException.NotFound("Categoria");
            if (await db.Ingredientes.AnyAsync(i => i.CategoriaId == id, ct))
                throw BusinessException.Conflict("CATEGORIA_EM_USO", "Mova ou arquive os ingredientes desta categoria antes de excluí-la.");
            db.Categorias.Remove(c);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).Editor();
    }

    private static void MapGroups(RouteGroupBuilder g)
    {
        g.MapGet("/grupos-equivalencia", async (TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var groups = await db.GruposEquivalencia.AsNoTracking().Where(x => x.DespensaId == t.DespensaId)
                .OrderBy(x => x.Nome).ToListAsync(ct);
            var members = await db.Ingredientes.AsNoTracking()
                .Where(i => i.DespensaId == t.DespensaId && i.GrupoEquivalenciaId != null && i.ArquivadoEm == null)
                .Select(i => new { i.Id, i.Nome, Grupo = i.GrupoEquivalenciaId!.Value }).ToListAsync(ct);
            return Results.Ok(groups.Select(x => new
            {
                x.Id, x.Nome,
                ingredientes = members.Where(m => m.Grupo == x.Id).Select(m => new { m.Id, m.Nome }),
            }));
        }).Leitor();

        g.MapPost("/grupos-equivalencia", async (NomeRequest req, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var norm = Norm(req.Nome);
            if (await db.GruposEquivalencia.AnyAsync(x => x.DespensaId == t.DespensaId && x.NomeNormalizado == norm, ct))
                throw BusinessException.Conflict("NOME_DUPLICADO", "Já existe um grupo com este nome.");
            var group = new GrupoEquivalencia { DespensaId = t.DespensaId, Nome = req.Nome.Trim(), NomeNormalizado = norm };
            db.GruposEquivalencia.Add(group);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/grupos-equivalencia/{group.Id}", new { group.Id, group.Nome });
        }).Editor();

        g.MapPatch("/grupos-equivalencia/{id:guid}", async (Guid id, NomeRequest req, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var group = await db.GruposEquivalencia.FirstOrDefaultAsync(x => x.Id == id && x.DespensaId == t.DespensaId, ct)
                ?? throw BusinessException.NotFound("Grupo de equivalência");
            var norm = Norm(req.Nome);
            if (await db.GruposEquivalencia.AnyAsync(x => x.DespensaId == t.DespensaId && x.NomeNormalizado == norm && x.Id != id, ct))
                throw BusinessException.Conflict("NOME_DUPLICADO", "Já existe um grupo com este nome.");
            group.Nome = req.Nome.Trim();
            group.NomeNormalizado = norm;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { group.Id, group.Nome });
        }).Editor();

        g.MapDelete("/grupos-equivalencia/{id:guid}", async (Guid id, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var group = await db.GruposEquivalencia.FirstOrDefaultAsync(x => x.Id == id && x.DespensaId == t.DespensaId, ct)
                ?? throw BusinessException.NotFound("Grupo de equivalência");
            db.GruposEquivalencia.Remove(group);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).Editor();
    }

    private sealed record PantrySettings(string Fuso, int AlertaValidadeDias);

    private static async Task<PantrySettings> Settings(AppDbContext db, Guid despensaId, CancellationToken ct) =>
        await db.Despensas.AsNoTracking().Where(d => d.Id == despensaId)
            .Select(d => new PantrySettings(d.Fuso, d.AlertaValidadeDias)).FirstAsync(ct);

    private static string ExpiryState(DateOnly? next, DateOnly today, int alertDays) =>
        next is null ? "sem_validade"
        : next < today ? "vencido"
        : next <= today.AddDays(alertDays) ? "vencendo"
        : "ok";

    private static async Task ValidateIngredient(
        IngredienteInput input, Guid despensaId, AppDbContext db, Guid? selfId, CancellationToken ct)
    {
        var unit = input.UnidadeReferencia.Trim().ToUpperInvariant();
        if (!UnitCatalog.Exists(unit))
            throw BusinessException.BadRequest("UNIDADE_INVALIDA", $"Unidade desconhecida: {input.UnidadeReferencia}.");
        if (!await db.Categorias.AnyAsync(c => c.Id == input.CategoriaId && c.DespensaId == despensaId, ct))
            throw BusinessException.BadRequest("CATEGORIA_INVALIDA", "Escolha uma categoria desta despensa.");
        if (input.GrupoEquivalenciaId is { } groupId &&
            !await db.GruposEquivalencia.AnyAsync(x => x.Id == groupId && x.DespensaId == despensaId, ct))
            throw BusinessException.BadRequest("GRUPO_INVALIDO", "Grupo de equivalência inexistente nesta despensa.");
        var norm = Norm(input.Nome);
        if (norm.Length == 0) throw BusinessException.BadRequest("VALIDACAO", "Informe o nome do ingrediente.");
        var duplicate = await db.Ingredientes.Where(i => i.DespensaId == despensaId && i.NomeNormalizado == norm && i.Id != selfId)
            .Select(i => new { i.ArquivadoEm }).FirstOrDefaultAsync(ct);
        if (duplicate is not null)
            throw BusinessException.Conflict("NOME_DUPLICADO", duplicate.ArquivadoEm is null
                ? "Já existe um ingrediente com este nome."
                : "Já existe um ingrediente arquivado com este nome. Reative-o em vez de criar outro.");
    }

    private static void MapIngredients(RouteGroupBuilder g)
    {
        g.MapGet("/ingredientes", async (
            TenantContext t, AppDbContext db, TimeProvider clock, CancellationToken ct,
            string? busca, Guid? categoriaId, string? situacao, bool? arquivados, int? limite, string? cursor) =>
        {
            if (!PantryClock.TryParseCursor(cursor, out var offset))
                throw BusinessException.BadRequest("CURSOR_INVALIDO", "Cursor inválido.");
            var take = Math.Clamp(limite ?? 50, 1, 200);
            var settings = await Settings(db, t.DespensaId, ct);
            var today = PantryClock.Today(settings.Fuso, clock.GetUtcNow());
            var limit = today.AddDays(settings.AlertaValidadeDias);

            var query = db.Ingredientes.AsNoTracking().Where(i => i.DespensaId == t.DespensaId);
            query = arquivados == true ? query.Where(i => i.ArquivadoEm != null) : query.Where(i => i.ArquivadoEm == null);
            if (categoriaId is { } cat) query = query.Where(i => i.CategoriaId == cat);
            if (!string.IsNullOrWhiteSpace(busca))
            {
                var term = Norm(busca);
                query = query.Where(i => i.NomeNormalizado.Contains(term));
            }

            var projected = query.Select(i => new
            {
                i.Id, i.Nome, i.CategoriaId, CategoriaNome = i.Categoria!.Nome, i.UnidadeReferenciaCodigo, i.GrupoEquivalenciaId,
                i.QuantidadeMinima, i.LocalArmazenamento, i.Observacoes, i.ArquivadoEm,
                Total = i.ItemEstoque!.QuantidadeTotal,
                Next = i.ItemEstoque.Lotes.Where(l => l.Status == StatusLote.Aberto && l.DataValidade != null)
                    .Min(l => (DateOnly?)l.DataValidade),
            });

            projected = (situacao ?? "").ToLowerInvariant() switch
            {
                "vencido" => projected.Where(x => x.Next != null && x.Next < today),
                "vencendo" => projected.Where(x => x.Next != null && x.Next >= today && x.Next <= limit),
                "baixo" => projected.Where(x => x.QuantidadeMinima > 0 && x.Total < x.QuantidadeMinima),
                "zerado" => projected.Where(x => x.Total <= 0),
                "em_estoque" => projected.Where(x => x.Total > 0),
                "" => projected,
                _ => throw BusinessException.BadRequest("FILTRO_INVALIDO", "Situação desconhecida."),
            };

            var rows = await projected.OrderBy(x => x.Nome).Skip(offset).Take(take + 1).ToListAsync(ct);
            var hasMore = rows.Count > take;
            return Results.Ok(new
            {
                itens = rows.Take(take).Select(x => new
                {
                    x.Id, x.Nome, x.CategoriaId, categoriaNome = x.CategoriaNome, unidadeReferencia = x.UnidadeReferenciaCodigo,
                    x.GrupoEquivalenciaId, x.QuantidadeMinima, x.LocalArmazenamento, x.Observacoes,
                    arquivado = x.ArquivadoEm != null, quantidadeTotal = x.Total, proximaValidade = x.Next,
                    situacaoValidade = ExpiryState(x.Next, today, settings.AlertaValidadeDias),
                    abaixoDoMinimo = x.QuantidadeMinima > 0 && x.Total < x.QuantidadeMinima,
                }),
                proximoCursor = hasMore ? (offset + take).ToString() : null,
            });
        }).Leitor();

        g.MapGet("/ingredientes/{id:guid}", async (Guid id, TenantContext t, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var settings = await Settings(db, t.DespensaId, ct);
            var today = PantryClock.Today(settings.Fuso, clock.GetUtcNow());
            var ing = await db.Ingredientes.AsNoTracking().Include(i => i.Categoria).Include(i => i.ItemEstoque)
                .FirstOrDefaultAsync(i => i.Id == id && i.DespensaId == t.DespensaId, ct)
                ?? throw BusinessException.NotFound("Ingrediente");
            var lots = await db.Lotes.AsNoTracking()
                .Where(l => l.ItemEstoqueId == ing.ItemEstoque!.Id && l.Status == StatusLote.Aberto)
                .OrderBy(l => l.DataValidade == null).ThenBy(l => l.DataValidade).ThenBy(l => l.DataEntrada).ToListAsync(ct);
            return Results.Ok(new
            {
                ing.Id, ing.Nome, ing.CategoriaId, categoriaNome = ing.Categoria!.Nome,
                unidadeReferencia = ing.UnidadeReferenciaCodigo, ing.GrupoEquivalenciaId, ing.QuantidadeMinima,
                ing.LocalArmazenamento, ing.Observacoes, arquivado = ing.ArquivadoEm != null,
                quantidadeTotal = ing.ItemEstoque!.QuantidadeTotal,
                lotes = lots.Select(l => new
                {
                    l.Id, l.Quantidade, l.QuantidadeInicial, l.DataValidade, l.DataEntrada, l.Origem, l.Observacao,
                    situacaoValidade = ExpiryState(l.DataValidade, today, settings.AlertaValidadeDias),
                }),
            });
        }).Leitor();

        g.MapPost("/ingredientes", async (IngredienteInput req, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            await ValidateIngredient(req, t.DespensaId, db, null, ct);
            var ing = new Ingrediente
            {
                DespensaId = t.DespensaId, Nome = req.Nome.Trim(), NomeNormalizado = Norm(req.Nome), CategoriaId = req.CategoriaId,
                UnidadeReferenciaCodigo = req.UnidadeReferencia.Trim().ToUpperInvariant(), GrupoEquivalenciaId = req.GrupoEquivalenciaId,
                QuantidadeMinima = UnitCatalog.Round(req.QuantidadeMinima), LocalArmazenamento = req.LocalArmazenamento?.Trim(),
                Observacoes = req.Observacoes?.Trim(),
            };
            db.Ingredientes.Add(ing);
            db.ItensEstoque.Add(new ItemEstoque { DespensaId = t.DespensaId, IngredienteId = ing.Id });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/ingredientes/{ing.Id}", new { ing.Id, ing.Nome });
        }).Editor();

        g.MapPut("/ingredientes/{id:guid}", async (Guid id, IngredienteInput req, TenantContext t, AppDbContext db, StockService stock, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await ValidateIngredient(req, t.DespensaId, db, id, ct);
            var ing = await stock.RequireIngrediente(t.DespensaId, id, ct);
            var item = (await stock.LockItems(t.DespensaId, [id], ct)).Single();
            var unit = req.UnidadeReferencia.Trim().ToUpperInvariant();
            if (unit != ing.UnidadeReferenciaCodigo && item.QuantidadeTotal > 0)
                throw BusinessException.Conflict("UNIDADE_BLOQUEADA",
                    "A unidade de referência só pode mudar quando o estoque do ingrediente estiver zerado.");
            ing.Nome = req.Nome.Trim();
            ing.NomeNormalizado = Norm(req.Nome);
            ing.CategoriaId = req.CategoriaId;
            ing.UnidadeReferenciaCodigo = unit;
            ing.GrupoEquivalenciaId = req.GrupoEquivalenciaId;
            ing.QuantidadeMinima = UnitCatalog.Round(req.QuantidadeMinima);
            ing.LocalArmazenamento = req.LocalArmazenamento?.Trim();
            ing.Observacoes = req.Observacoes?.Trim();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { ing.Id, ing.Nome });
        }).Editor();

        g.MapPost("/ingredientes/{id:guid}/arquivar", async (Guid id, TenantContext t, AppDbContext db, TimeProvider clock, StockService stock, CancellationToken ct) =>
        {
            var ing = await stock.RequireIngrediente(t.DespensaId, id, ct);
            ing.ArquivadoEm ??= clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ing.Id, arquivado = true });
        }).Editor();

        g.MapPost("/ingredientes/{id:guid}/reativar", async (Guid id, TenantContext t, AppDbContext db, StockService stock, CancellationToken ct) =>
        {
            var ing = await stock.RequireIngrediente(t.DespensaId, id, ct);
            ing.ArquivadoEm = null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ing.Id, arquivado = false });
        }).Editor();

        g.MapDelete("/ingredientes/{id:guid}", async (Guid id, TenantContext t, AppDbContext db, StockService stock, CancellationToken ct) =>
        {
            var ing = await stock.RequireIngrediente(t.DespensaId, id, ct);
            var used = await db.Movimentacoes.AnyAsync(m => db.ItensEstoque.Any(i => i.Id == m.ItemEstoqueId && i.IngredienteId == id), ct)
                || await db.ReceitaIngredientes.AnyAsync(r => r.IngredienteId == id, ct)
                || await db.ItensListaCompra.AnyAsync(r => r.IngredienteId == id, ct);
            if (used)
                throw BusinessException.Conflict("INGREDIENTE_EM_USO",
                    "Este ingrediente tem histórico ou é usado em receitas e listas. Arquive-o em vez de excluir.");
            db.Ingredientes.Remove(ing);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).Editor();

        g.MapGet("/estoque/resumo", async (TenantContext t, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var settings = await Settings(db, t.DespensaId, ct);
            var today = PantryClock.Today(settings.Fuso, clock.GetUtcNow());
            var limit = today.AddDays(settings.AlertaValidadeDias);
            var active = db.Ingredientes.AsNoTracking().Where(i => i.DespensaId == t.DespensaId && i.ArquivadoEm == null);
            var openLots = db.Lotes.AsNoTracking().Where(l => l.Status == StatusLote.Aberto && l.DataValidade != null
                && db.ItensEstoque.Any(i => i.Id == l.ItemEstoqueId && i.DespensaId == t.DespensaId
                    && db.Ingredientes.Any(g => g.Id == i.IngredienteId && g.ArquivadoEm == null)));

            var upcoming = await openLots.Where(l => l.DataValidade <= limit).OrderBy(l => l.DataValidade).Take(15)
                .Join(db.ItensEstoque, l => l.ItemEstoqueId, i => i.Id, (l, i) => new { l, i.IngredienteId })
                .Join(db.Ingredientes, x => x.IngredienteId, g => g.Id, (x, g) => new
                {
                    x.l.Id, ingredienteId = g.Id, g.Nome, x.l.Quantidade, unidade = g.UnidadeReferenciaCodigo, x.l.DataValidade,
                }).ToListAsync(ct);

            return Results.Ok(new
            {
                ingredientes = await active.CountAsync(ct),
                comEstoque = await active.CountAsync(i => i.ItemEstoque!.QuantidadeTotal > 0, ct),
                semEstoque = await active.CountAsync(i => i.ItemEstoque!.QuantidadeTotal <= 0, ct),
                abaixoDoMinimo = await active.CountAsync(i => i.QuantidadeMinima > 0 && i.ItemEstoque!.QuantidadeTotal < i.QuantidadeMinima, ct),
                lotesVencidos = await openLots.CountAsync(l => l.DataValidade < today, ct),
                lotesVencendo = await openLots.CountAsync(l => l.DataValidade >= today && l.DataValidade <= limit, ct),
                alertaValidadeDias = settings.AlertaValidadeDias,
                proximosVencimentos = upcoming.Select(x => new
                {
                    x.Id, x.ingredienteId, x.Nome, x.Quantidade, x.unidade, x.DataValidade,
                    situacaoValidade = ExpiryState(x.DataValidade, today, settings.AlertaValidadeDias),
                }),
            });
        }).Leitor();
    }

    private static object Movement(MovementResult r) =>
        new { ingredienteId = r.IngredienteId, quantidadeTotal = r.QuantidadeTotal, unidade = r.UnidadeCodigo, movimentacoes = r.MovimentacaoIds };

    private static void MapMovements(RouteGroupBuilder g)
    {
        string? Key(HttpContext http) => http.Request.Headers[TransactionalIdempotentFilter.HeaderName].FirstOrDefault()?.Trim() is { Length: > 0 } k ? k : null;

        g.MapPost("/estoque/entradas", async (EntradaRequest req, TenantContext t, StockService stock, HttpContext http, CancellationToken ct) =>
        {
            if (req.DataValidade is { } v && v < new DateOnly(2000, 1, 1))
                throw BusinessException.BadRequest("VALIDADE_INVALIDA", "Data de validade inválida.");
            var result = await stock.Entrada(t.DespensaId, t.UsuarioId, req.IngredienteId, req.Quantidade, req.Unidade, req.DataValidade,
                req.Origem == OrigemLote.Lista ? OrigemLote.Outro : req.Origem, req.Motivo ?? "Entrada de estoque", req.Observacao,
                TipoMovimentacao.Entrada, null, Key(http), ct);
            return Results.Ok(Movement(result));
        }).Editor().Transactional();

        g.MapPost("/estoque/saidas", async (SaidaRequest req, TenantContext t, StockService stock, HttpContext http, CancellationToken ct) =>
        {
            var result = await stock.Saida(t.DespensaId, t.UsuarioId, req.IngredienteId, req.Quantidade, req.Unidade, req.LoteId,
                TipoMovimentacao.Consumo, req.Motivo.Trim(), Key(http), ct);
            return Results.Ok(Movement(result));
        }).Editor().Transactional();

        g.MapPost("/estoque/perdas", async (SaidaRequest req, TenantContext t, StockService stock, HttpContext http, CancellationToken ct) =>
        {
            var result = await stock.Saida(t.DespensaId, t.UsuarioId, req.IngredienteId, req.Quantidade, req.Unidade, req.LoteId,
                TipoMovimentacao.Perda, req.Motivo.Trim(), Key(http), ct);
            return Results.Ok(Movement(result));
        }).Editor().Transactional();

        g.MapPost("/estoque/correcoes", async (CorrecaoRequest req, TenantContext t, StockService stock, HttpContext http, CancellationToken ct) =>
        {
            var result = await stock.Correcao(t.DespensaId, t.UsuarioId, req.IngredienteId, req.LoteId, req.Delta, req.Unidade,
                req.Motivo.Trim(), Key(http), ct);
            return Results.Ok(Movement(result));
        }).Editor().Transactional();

        g.MapGet("/estoque/movimentacoes", async (
            TenantContext t, AppDbContext db, CancellationToken ct,
            Guid? ingredienteId, string? tipo, int? limite, string? cursor) =>
        {
            var take = Math.Clamp(limite ?? 50, 1, 200);
            var query = db.Movimentacoes.AsNoTracking().Where(m => m.DespensaId == t.DespensaId);
            if (ingredienteId is { } ing)
                query = query.Where(m => db.ItensEstoque.Any(i => i.Id == m.ItemEstoqueId && i.IngredienteId == ing));
            if (!string.IsNullOrWhiteSpace(tipo))
            {
                if (!Enum.TryParse<TipoMovimentacao>(tipo.Replace("_", ""), true, out var parsed))
                    throw BusinessException.BadRequest("FILTRO_INVALIDO", "Tipo de movimentação desconhecido.");
                query = query.Where(m => m.Tipo == parsed);
            }
            if (!string.IsNullOrEmpty(cursor))
            {
                var parts = cursor.Split('_');
                if (parts.Length != 2 || !DateTimeOffset.TryParse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) || !Guid.TryParse(parts[1], out var lastId))
                    throw BusinessException.BadRequest("CURSOR_INVALIDO", "Cursor inválido.");
                query = query.Where(m => m.CriadoEm < at || (m.CriadoEm == at && m.Id.CompareTo(lastId) < 0));
            }

            var rows = await query.OrderByDescending(m => m.CriadoEm).ThenByDescending(m => m.Id).Take(take + 1)
                .Select(m => new
                {
                    m.Id, m.Tipo, m.Sentido, m.Quantidade, unidade = m.UnidadeCodigo, m.Motivo, m.CriadoEm, loteId = m.LoteEstoqueId, m.PreparoId,
                    IngredienteId = db.ItensEstoque.Where(i => i.Id == m.ItemEstoqueId).Select(i => i.IngredienteId).First(),
                    IngredienteNome = db.ItensEstoque.Where(i => i.Id == m.ItemEstoqueId).Select(i => i.Ingrediente!.Nome).First(),
                    UsuarioNome = db.Users.Where(u => u.Id == m.UsuarioId).Select(u => u.NomeExibicao).FirstOrDefault(),
                }).ToListAsync(ct);
            var hasMore = rows.Count > take;
            var page = rows.Take(take).ToList();
            return Results.Ok(new
            {
                itens = page,
                proximoCursor = hasMore ? $"{page[^1].CriadoEm:O}_{page[^1].Id}" : null,
            });
        }).Leitor();
    }

    private static void MapLots(RouteGroupBuilder g)
    {
        g.MapPatch("/lotes/{id:guid}", async (Guid id, LoteAtualizarRequest req, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var lot = await db.Lotes.FirstOrDefaultAsync(l => l.Id == id && l.Status == StatusLote.Aberto
                    && db.ItensEstoque.Any(i => i.Id == l.ItemEstoqueId && i.DespensaId == t.DespensaId), ct)
                ?? throw BusinessException.NotFound("Lote");
            lot.DataValidade = req.DataValidade;
            lot.Observacao = req.Observacao?.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { lot.Id, lot.DataValidade, lot.Observacao });
        }).Editor();

        g.MapPost("/lotes/{id:guid}/descartar", async (Guid id, LoteDescartarRequest req, TenantContext t, AppDbContext db, StockService stock, HttpContext http, CancellationToken ct) =>
        {
            var info = await db.Lotes.AsNoTracking()
                .Where(l => l.Id == id && l.Status == StatusLote.Aberto)
                .Join(db.ItensEstoque.Where(i => i.DespensaId == t.DespensaId), l => l.ItemEstoqueId, i => i.Id,
                    (l, i) => new { l.Quantidade, i.IngredienteId })
                .FirstOrDefaultAsync(ct) ?? throw BusinessException.NotFound("Lote");
            var key = http.Request.Headers[TransactionalIdempotentFilter.HeaderName].FirstOrDefault()?.Trim();
            var result = await stock.Saida(t.DespensaId, t.UsuarioId, info.IngredienteId, info.Quantidade, null, id,
                TipoMovimentacao.Perda, string.IsNullOrWhiteSpace(req.Motivo) ? "Lote descartado" : req.Motivo.Trim(),
                string.IsNullOrEmpty(key) ? null : key, ct);
            var lot = await db.Lotes.FirstAsync(l => l.Id == id, ct);
            lot.Status = StatusLote.Descartado;
            await db.SaveChangesAsync(ct);
            return Results.Ok(Movement(result));
        }).Editor().Transactional();
    }
}
