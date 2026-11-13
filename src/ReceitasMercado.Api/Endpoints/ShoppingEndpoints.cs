using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Contracts;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Api.Services;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Endpoints;

public static class ShoppingEndpoints
{
    private static async Task<Dictionary<Guid, string>> Names(AppDbContext db, Guid despensaId, CancellationToken ct) =>
        await db.Ingredientes.AsNoTracking().Where(i => i.DespensaId == despensaId).ToDictionaryAsync(i => i.Id, i => i.Nome, ct);

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/listas").RequireAuthorization().AddEndpointFilter<ValidationFilter>();

        g.MapGet("", async (TenantContext t, AppDbContext db, string? status, CancellationToken ct) =>
        {
            var query = db.ListasCompra.AsNoTracking().Where(l => l.DespensaId == t.DespensaId);
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<StatusLista>(status, true, out var st))
                    throw BusinessException.BadRequest("FILTRO_INVALIDO", "Status desconhecido.");
                query = query.Where(l => l.Status == st);
            }
            return Results.Ok(await query.OrderByDescending(l => l.CriadoEm).Take(100).Select(l => new
            {
                l.Id, l.Nome, l.Status, l.Origem, l.CriadoEm,
                itens = l.Itens.Count, comprados = l.Itens.Count(i => i.Comprado),
            }).ToListAsync(ct));
        }).Leitor();

        g.MapPost("", async (ListaCriarRequest req, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var l = new ListaCompra { DespensaId = t.DespensaId, Nome = req.Nome.Trim(), CriadoPorUsuarioId = t.UsuarioId };
            db.ListasCompra.Add(l);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/listas/{l.Id}", ShoppingService.ToDto(l));
        }).Editor();

        g.MapPost("/de-receitas", async (ListaDeReceitasRequest req, TenantContext t, ShoppingService service, AppDbContext db, CancellationToken ct) =>
        {
            var (lista, ignorados) = await service.FromRecipes(t, req, ct);
            var dto = ShoppingService.ToDto(lista, await Names(db, t.DespensaId, ct));
            return Results.Created($"/api/listas/{lista.Id}", new { lista = dto, ignorados });
        }).Editor();

        g.MapGet("/{id:guid}", async (Guid id, TenantContext t, AppDbContext db, ShoppingService service, CancellationToken ct) =>
            Results.Ok(ShoppingService.ToDto(await service.Load(t.DespensaId, id, ct), await Names(db, t.DespensaId, ct)))).Leitor();

        g.MapPatch("/{id:guid}", async (Guid id, ListaAtualizarRequest req, TenantContext t, AppDbContext db, ShoppingService service, CancellationToken ct) =>
        {
            var l = await service.Load(t.DespensaId, id, ct);
            l.Nome = req.Nome.Trim();
            l.Status = req.Status;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ShoppingService.ToDto(l, await Names(db, t.DespensaId, ct)));
        }).Editor();

        g.MapDelete("/{id:guid}", async (Guid id, TenantContext t, AppDbContext db, ShoppingService service, CancellationToken ct) =>
        {
            var l = await service.Load(t.DespensaId, id, ct);
            db.ListasCompra.Remove(l);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).Editor();

        g.MapPost("/{id:guid}/itens", async (Guid id, ItemListaInput req, TenantContext t, AppDbContext db, ShoppingService service, CancellationToken ct) =>
        {
            var l = await service.Load(t.DespensaId, id, ct);
            var unit = req.Unidade.Trim().ToUpperInvariant();
            if (!UnitCatalog.Exists(unit)) throw BusinessException.BadRequest("UNIDADE_INVALIDA", $"Unidade desconhecida: {req.Unidade}.");
            if (req.IngredienteId is { } ing && !await db.Ingredientes.AnyAsync(i => i.Id == ing && i.DespensaId == t.DespensaId, ct))
                throw BusinessException.BadRequest("INGREDIENTE_INVALIDO", "Ingrediente inexistente nesta despensa.");
            if (l.Itens.Count >= 300) throw BusinessException.Conflict("LISTA_CHEIA", "A lista chegou ao limite de 300 itens.");
            var item = new ItemListaCompra
            {
                ListaId = l.Id, Ordem = (l.Itens.Count == 0 ? -1 : l.Itens.Max(i => i.Ordem)) + 1, IngredienteId = req.IngredienteId,
                Texto = req.Texto.Trim(), QuantidadeNecessaria = UnitCatalog.Round(req.Quantidade),
                QuantidadeComprar = UnitCatalog.Round(req.Quantidade), UnidadeCodigo = unit, AjusteManual = true,
            };
            db.Entry(item).State = EntityState.Added;
            if (l.Status != StatusLista.Aberta) l.Status = StatusLista.Aberta;
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/listas/{id}/itens/{item.Id}", new { item.Id });
        }).Editor();

        g.MapPatch("/{id:guid}/itens/{itemId:guid}", async (Guid id, Guid itemId, ItemListaAtualizarRequest req, TenantContext t, AppDbContext db, ShoppingService service, CancellationToken ct) =>
        {
            var l = await service.Load(t.DespensaId, id, ct);
            var item = l.Itens.FirstOrDefault(i => i.Id == itemId) ?? throw BusinessException.NotFound("Item da lista");
            var unit = req.Unidade.Trim().ToUpperInvariant();
            if (!UnitCatalog.Exists(unit)) throw BusinessException.BadRequest("UNIDADE_INVALIDA", $"Unidade desconhecida: {req.Unidade}.");
            var quantity = UnitCatalog.Round(req.QuantidadeComprar);
            if (item.EntradaRegistrada && (quantity != item.QuantidadeComprar || unit != item.UnidadeCodigo || !req.Comprado))
                throw BusinessException.Conflict("ITEM_JA_LANCADO", "Este item já entrou no estoque; a quantidade e a marcação não podem mais ser alteradas.");
            if (quantity != item.QuantidadeComprar || unit != item.UnidadeCodigo) item.AjusteManual = true;
            item.Texto = req.Texto.Trim();
            item.QuantidadeComprar = quantity;
            item.UnidadeCodigo = unit;
            item.Comprado = req.Comprado;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ShoppingService.ToDto(l, await Names(db, t.DespensaId, ct)));
        }).Editor();

        g.MapDelete("/{id:guid}/itens/{itemId:guid}", async (Guid id, Guid itemId, TenantContext t, AppDbContext db, ShoppingService service, CancellationToken ct) =>
        {
            var l = await service.Load(t.DespensaId, id, ct);
            var item = l.Itens.FirstOrDefault(i => i.Id == itemId) ?? throw BusinessException.NotFound("Item da lista");
            db.ItensListaCompra.Remove(item);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).Editor();

        g.MapPost("/{id:guid}/lancar-entradas", async (Guid id, LancarEntradasRequest? req, TenantContext t, ShoppingService service, HttpContext http, CancellationToken ct) =>
        {
            var key = http.Request.Headers[TransactionalIdempotentFilter.HeaderName].FirstOrDefault()?.Trim();
            return Results.Ok(await service.LancarEntradas(t, id, req ?? new LancarEntradasRequest(), string.IsNullOrEmpty(key) ? null : key, ct));
        }).Editor().Transactional();
    }
}
