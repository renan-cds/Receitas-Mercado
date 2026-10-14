using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Contracts;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Api.Services;

namespace ReceitasMercado.Api.Endpoints;

public static class PreparoEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/preparos").RequireAuthorization().AddEndpointFilter<ValidationFilter>();

        g.MapPost("", async (PreparoIniciarRequest req, TenantContext t, PreparoService service, CancellationToken ct) =>
            Results.Ok(await service.Iniciar(t, req, ct))).Editor();

        g.MapGet("", async (TenantContext t, AppDbContext db, int? limite, string? cursor, CancellationToken ct) =>
        {
            if (!PantryClock.TryParseCursor(cursor, out var offset))
                throw BusinessException.BadRequest("CURSOR_INVALIDO", "Cursor inválido.");
            var take = Math.Clamp(limite ?? 30, 1, 100);
            var rows = await db.Preparos.AsNoTracking()
                .Where(p => p.DespensaId == t.DespensaId && p.Status == StatusPreparo.Confirmado)
                .OrderByDescending(p => p.ConfirmadoEm).ThenBy(p => p.Id)
                .Skip(offset).Take(take + 1)
                .Select(p => new
                {
                    preparoId = p.Id, p.ReceitaId, p.ReceitaTitulo, p.Porcoes, p.ConfirmadoEm, p.ConsumirDisponivel,
                    usuarioNome = db.Users.Where(u => u.Id == p.UsuarioId).Select(u => u.NomeExibicao).FirstOrDefault(),
                }).ToListAsync(ct);
            return Results.Ok(new { itens = rows.Take(take), proximoCursor = rows.Count > take ? (offset + take).ToString() : null });
        }).Leitor();

        g.MapGet("/{id:guid}", async (Guid id, TenantContext t, AppDbContext db, PreparoService service, CancellationToken ct) =>
        {
            var p = await db.Preparos.AsNoTracking().Include(x => x.Itens)
                .FirstOrDefaultAsync(x => x.Id == id && x.DespensaId == t.DespensaId, ct)
                ?? throw BusinessException.NotFound("Preparo");
            return Results.Ok(p.Status == StatusPreparo.Iniciado ? await service.Preview(p, ct) : PreparoService.Result(p));
        }).Leitor();

        g.MapPost("/{id:guid}/confirmar", async (Guid id, PreparoConfirmarRequest? req, TenantContext t, PreparoService service, HttpContext http, CancellationToken ct) =>
        {
            var key = http.Request.Headers[TransactionalIdempotentFilter.HeaderName].FirstOrDefault()?.Trim();
            return Results.Ok(await service.Confirmar(t, id, req ?? new PreparoConfirmarRequest(), string.IsNullOrEmpty(key) ? null : key, ct));
        }).Editor().Transactional();

        g.MapPost("/{id:guid}/cancelar", async (Guid id, TenantContext t, AppDbContext db, PreparoService service, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await service.Cancelar(t, id, ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { ok = true });
        }).Editor();
    }
}
