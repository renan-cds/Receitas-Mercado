using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Contracts;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Api.Services;

namespace ReceitasMercado.Api.Endpoints;

public static class PantryEndpoints
{
    public static byte[] HashToken(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("").RequireAuthorization().AddEndpointFilter<ValidationFilter>();

        g.MapGet("/despensas", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.RequireUserId();
            return Results.Ok(await db.Membros.AsNoTracking()
                .Where(m => m.UsuarioId == userId && m.Status == StatusMembro.Ativo)
                .Select(m => new { id = m.DespensaId, nome = m.Despensa!.Nome, papel = m.Papel })
                .ToListAsync(ct));
        });

        g.MapGet("/despensas/{despensaId:guid}", async (TenantContext t, AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Despensas.AsNoTracking().Where(d => d.Id == t.DespensaId)
                .Select(d => new { d.Id, d.Nome, d.Fuso, d.AlertaValidadeDias, papel = t.Papel }).FirstAsync(ct))).Leitor();

        g.MapPatch("/despensas/{despensaId:guid}", async (DespensaAtualizarRequest req, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            try { TimeZoneInfo.FindSystemTimeZoneById(req.Fuso); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw BusinessException.BadRequest("FUSO_INVALIDO", "Fuso horário desconhecido.");
            }
            var d = await db.Despensas.FirstAsync(x => x.Id == t.DespensaId, ct);
            d.Nome = req.Nome.Trim();
            d.Fuso = req.Fuso;
            d.AlertaValidadeDias = req.AlertaValidadeDias;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { d.Id, d.Nome, d.Fuso, d.AlertaValidadeDias });
        }).Admin();

        g.MapDelete("/despensas/{despensaId:guid}", async (ExcluirDespensaRequest req, TenantContext t, AppDbContext db, PantryService pantry, CancellationToken ct) =>
        {
            var d = await db.Despensas.AsNoTracking().FirstAsync(x => x.Id == t.DespensaId, ct);
            if (!string.Equals(d.Nome, req.ConfirmacaoNome.Trim(), StringComparison.Ordinal))
                throw BusinessException.BadRequest("CONFIRMACAO_INVALIDA", "Digite o nome exato da despensa para confirmar a exclusão.");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await pantry.DeletePantry(t.DespensaId, ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { ok = true });
        }).Admin();

        g.MapPost("/despensas/{despensaId:guid}/convites", async (ConviteCriarRequest req, TenantContext t, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (req.Papel == Papel.Admin)
                throw BusinessException.BadRequest("PAPEL_INVALIDO", "Convites por link só podem conceder o papel de editor ou leitor.");
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var invite = new ConviteDespensa
            {
                DespensaId = t.DespensaId, Papel = req.Papel, TokenHash = HashToken(token), CriadoPorUsuarioId = t.UsuarioId,
                ExpiraEm = clock.GetUtcNow().AddDays(req.ValidadeDias),
            };
            db.Convites.Add(invite);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = invite.Id, token, caminho = $"/convite/{token}", papel = invite.Papel, expiraEm = invite.ExpiraEm });
        }).Admin();

        g.MapGet("/despensas/{despensaId:guid}/convites", async (TenantContext t, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            return Results.Ok(await db.Convites.AsNoTracking()
                .Where(c => c.DespensaId == t.DespensaId && c.UsadoEm == null && c.RevogadoEm == null && c.ExpiraEm > now)
                .OrderByDescending(c => c.CriadoEm)
                .Select(c => new { c.Id, c.Papel, c.ExpiraEm, c.CriadoEm }).ToListAsync(ct));
        }).Admin();

        g.MapDelete("/despensas/{despensaId:guid}/convites/{conviteId:guid}", async (Guid conviteId, TenantContext t, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var invite = await db.Convites.FirstOrDefaultAsync(c => c.Id == conviteId && c.DespensaId == t.DespensaId, ct)
                ?? throw BusinessException.NotFound("Convite");
            invite.RevogadoEm ??= clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).Admin();

        g.MapGet("/convites/{token}", async (string token, HttpContext http, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var userId = http.User.RequireUserId();
            var invite = await FindInvite(db, token, ct);
            var already = await db.Membros.AnyAsync(m => m.DespensaId == invite.DespensaId && m.UsuarioId == userId && m.Status == StatusMembro.Ativo, ct);
            var nome = await db.Despensas.Where(d => d.Id == invite.DespensaId).Select(d => d.Nome).FirstAsync(ct);
            return Results.Ok(new
            {
                despensaNome = nome, papel = invite.Papel, expiraEm = invite.ExpiraEm, jaMembro = already,
                valido = invite.UsadoEm is null && invite.RevogadoEm is null && invite.ExpiraEm > clock.GetUtcNow(),
            });
        });

        g.MapPost("/convites/{token}/aceitar", async (string token, HttpContext http, AppDbContext db, PantryService pantry, TimeProvider clock, CancellationToken ct) =>
        {
            var userId = http.User.RequireUserId();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var hash = HashToken(token);
            await db.Database.ExecuteSqlInterpolatedAsync($"select 1 from convites_despensa where token_hash = {hash} for update", ct);
            var invite = await FindInvite(db, token, ct);

            var existing = await db.Membros.FirstOrDefaultAsync(m => m.DespensaId == invite.DespensaId && m.UsuarioId == userId, ct);
            if (existing is { Status: StatusMembro.Ativo })
                return Results.Ok(new { despensaId = invite.DespensaId, papel = existing.Papel, jaMembro = true });

            if (invite.RevogadoEm is not null) throw new BusinessException(410, "CONVITE_REVOGADO", "Este convite foi revogado.");
            if (invite.UsadoEm is not null) throw new BusinessException(410, "CONVITE_USADO", "Este convite já foi utilizado.");
            if (invite.ExpiraEm <= clock.GetUtcNow()) throw new BusinessException(410, "CONVITE_EXPIRADO", "Este convite expirou.");
            if (await pantry.ActiveMemberCount(invite.DespensaId, ct) >= PantryService.MaxMembers)
                throw BusinessException.Conflict("LIMITE_MEMBROS", $"A despensa já tem o máximo de {PantryService.MaxMembers} membros.");

            if (existing is null)
                db.Membros.Add(new MembroDespensa { DespensaId = invite.DespensaId, UsuarioId = userId, Papel = invite.Papel });
            else
            {
                existing.Status = StatusMembro.Ativo;
                existing.Papel = invite.Papel;
                existing.EntrouEm = clock.GetUtcNow();
                existing.SaiuEm = null;
            }
            invite.UsadoEm = clock.GetUtcNow();
            invite.UsadoPorUsuarioId = userId;
            var user = await db.Usuarios.FirstAsync(u => u.Id == userId, ct);
            user.DespensaAtivaId ??= invite.DespensaId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { despensaId = invite.DespensaId, papel = invite.Papel, jaMembro = false });
        });

        g.MapGet("/despensas/{despensaId:guid}/membros", async (TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var members = await db.Membros.AsNoTracking()
                .Where(m => m.DespensaId == t.DespensaId && m.Status == StatusMembro.Ativo)
                .OrderBy(m => m.EntrouEm)
                .Select(m => new { usuarioId = m.UsuarioId, nome = m.Usuario!.NomeExibicao, email = m.Usuario.Email, papel = m.Papel, entrouEm = m.EntrouEm })
                .ToListAsync(ct);
            return Results.Ok(members.Select(m => new
            {
                m.usuarioId, m.nome, email = t.Papel == Papel.Admin ? m.email : null, m.papel, m.entrouEm,
            }));
        }).Leitor();

        g.MapPatch("/despensas/{despensaId:guid}/membros/{usuarioId:guid}", async (Guid usuarioId, MembroAtualizarRequest req, TenantContext t, AppDbContext db, PantryService pantry, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var m = await db.Membros.FirstOrDefaultAsync(x => x.DespensaId == t.DespensaId && x.UsuarioId == usuarioId && x.Status == StatusMembro.Ativo, ct)
                ?? throw BusinessException.NotFound("Membro");
            if (m.Papel == Papel.Admin && req.Papel != Papel.Admin && await pantry.ActiveAdminCount(t.DespensaId, ct) <= 1)
                throw BusinessException.Conflict("ULTIMO_ADMIN", "A despensa precisa de pelo menos um administrador.");
            m.Papel = req.Papel;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { usuarioId, papel = m.Papel });
        }).Admin();

        g.MapDelete("/despensas/{despensaId:guid}/membros/{usuarioId:guid}", async (Guid usuarioId, TenantContext t, AppDbContext db, PantryService pantry, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await pantry.RemoveMember(t.DespensaId, usuarioId, ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { ok = true });
        }).Admin();

        g.MapPost("/despensas/{despensaId:guid}/sair", async (TenantContext t, AppDbContext db, PantryService pantry, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await pantry.RemoveMember(t.DespensaId, t.UsuarioId, ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { ok = true });
        }).Leitor();
    }

    private static async Task<ConviteDespensa> FindInvite(AppDbContext db, string token, CancellationToken ct)
    {
        var hash = HashToken(token);
        return await db.Convites.FirstOrDefaultAsync(c => c.TokenHash == hash, ct)
            ?? throw new BusinessException(404, "CONVITE_INVALIDO", "Convite inválido.");
    }
}
