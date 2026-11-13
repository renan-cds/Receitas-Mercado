using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Contracts;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Endpoints;

public static class CollectionEndpoints
{
    private static async Task<Catalogo> LoadVisible(AppDbContext db, TenantContext t, Guid id, CancellationToken ct) =>
        await db.Catalogos.FirstOrDefaultAsync(c => c.Id == id && c.DespensaId == t.DespensaId
            && (c.Visibilidade == VisibilidadeCatalogo.Despensa || c.UsuarioId == t.UsuarioId), ct)
        ?? throw BusinessException.NotFound("Catálogo");

    private static void EnsureCanManage(TenantContext t, VisibilidadeCatalogo visibility, Catalogo? existing = null)
    {
        if (visibility == VisibilidadeCatalogo.Despensa && !t.CanEdit)
            throw BusinessException.Forbidden("Somente editores e administradores gerenciam catálogos compartilhados.");
        if (existing is { Visibilidade: VisibilidadeCatalogo.Pessoal } && existing.UsuarioId != t.UsuarioId)
            throw BusinessException.NotFound("Catálogo");
    }

    private static async Task EnsureUniqueName(
        AppDbContext db, TenantContext t, string norm, VisibilidadeCatalogo visibility, Guid? selfId, CancellationToken ct)
    {
        var duplicate = visibility == VisibilidadeCatalogo.Despensa
            ? await db.Catalogos.AnyAsync(c => c.DespensaId == t.DespensaId && c.Visibilidade == VisibilidadeCatalogo.Despensa
                && c.NomeNormalizado == norm && c.Id != selfId, ct)
            : await db.Catalogos.AnyAsync(c => c.DespensaId == t.DespensaId && c.Visibilidade == VisibilidadeCatalogo.Pessoal
                && c.UsuarioId == t.UsuarioId && c.NomeNormalizado == norm && c.Id != selfId, ct);
        if (duplicate) throw BusinessException.Conflict("NOME_DUPLICADO", "Já existe um catálogo com este nome.");
    }

    private static object Dto(Catalogo c, int count, Guid currentUser) => new
    {
        c.Id, c.Nome, c.Descricao, c.Visibilidade, quantidadeReceitas = count, meu = c.UsuarioId == currentUser, c.CriadoEm,
    };

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("").RequireAuthorization().AddEndpointFilter<ValidationFilter>();

        g.MapGet("/catalogos", async (TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Catalogos.AsNoTracking()
                .Where(c => c.DespensaId == t.DespensaId && (c.Visibilidade == VisibilidadeCatalogo.Despensa || c.UsuarioId == t.UsuarioId))
                .OrderBy(c => c.Visibilidade).ThenBy(c => c.Nome)
                .Select(c => new { Catalogo = c, Total = c.Receitas.Count }).ToListAsync(ct);
            return Results.Ok(rows.Select(r => Dto(r.Catalogo, r.Total, t.UsuarioId)));
        }).Leitor();

        g.MapGet("/catalogos/{id:guid}", async (Guid id, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var c = await LoadVisible(db, t, id, ct);
            var recipes = await db.CatalogoReceitas.AsNoTracking().Where(x => x.CatalogoId == id)
                .Join(db.Receitas, x => x.ReceitaId, r => r.Id, (x, r) => new { r.Id, r.Titulo, r.ImagemUrl, r.ImagemArquivoId, x.AdicionadoEm })
                .OrderBy(x => x.Titulo).ToListAsync(ct);
            return Results.Ok(new
            {
                c.Id, c.Nome, c.Descricao, c.Visibilidade, meu = c.UsuarioId == t.UsuarioId, receitas = recipes,
            });
        }).Leitor();

        g.MapPost("/catalogos", async (CatalogoInput req, TenantContext t, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            EnsureCanManage(t, req.Visibilidade);
            var norm = TextNormalizer.Normalize(req.Nome);
            if (norm.Length == 0) throw BusinessException.BadRequest("VALIDACAO", "Informe o nome do catálogo.");
            await EnsureUniqueName(db, t, norm, req.Visibilidade, null, ct);
            var c = new Catalogo
            {
                DespensaId = t.DespensaId, Nome = req.Nome.Trim(), NomeNormalizado = norm, Descricao = req.Descricao?.Trim(),
                Visibilidade = req.Visibilidade, CriadoPorUsuarioId = t.UsuarioId, CriadoEm = clock.GetUtcNow(),
                UsuarioId = req.Visibilidade == VisibilidadeCatalogo.Pessoal ? t.UsuarioId : null,
            };
            db.Catalogos.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/catalogos/{c.Id}", Dto(c, 0, t.UsuarioId));
        }).Leitor();

        g.MapPatch("/catalogos/{id:guid}", async (Guid id, CatalogoInput req, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var c = await LoadVisible(db, t, id, ct);
            EnsureCanManage(t, c.Visibilidade, c);
            if (req.Visibilidade != c.Visibilidade)
                throw BusinessException.BadRequest("VISIBILIDADE_FIXA", "Para mudar a visibilidade, duplique o catálogo com a nova visibilidade.");
            var norm = TextNormalizer.Normalize(req.Nome);
            await EnsureUniqueName(db, t, norm, c.Visibilidade, id, ct);
            c.Nome = req.Nome.Trim();
            c.NomeNormalizado = norm;
            c.Descricao = req.Descricao?.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(Dto(c, await db.CatalogoReceitas.CountAsync(x => x.CatalogoId == id, ct), t.UsuarioId));
        }).Leitor();

        g.MapDelete("/catalogos/{id:guid}", async (Guid id, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var c = await LoadVisible(db, t, id, ct);
            EnsureCanManage(t, c.Visibilidade, c);
            db.Catalogos.Remove(c);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).Leitor();

        g.MapPut("/catalogos/{id:guid}/receitas/{receitaId:guid}", async (Guid id, Guid receitaId, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var c = await LoadVisible(db, t, id, ct);
            EnsureCanManage(t, c.Visibilidade, c);
            if (!await db.Receitas.AnyAsync(r => r.Id == receitaId && r.DespensaId == t.DespensaId, ct))
                throw BusinessException.NotFound("Receita");
            if (!await db.CatalogoReceitas.AnyAsync(x => x.CatalogoId == id && x.ReceitaId == receitaId, ct))
            {
                db.CatalogoReceitas.Add(new CatalogoReceita { CatalogoId = id, ReceitaId = receitaId });
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { db.ChangeTracker.Clear(); }
            }
            return Results.Ok(new { ok = true });
        }).Leitor();

        g.MapDelete("/catalogos/{id:guid}/receitas/{receitaId:guid}", async (Guid id, Guid receitaId, TenantContext t, AppDbContext db, CancellationToken ct) =>
        {
            var c = await LoadVisible(db, t, id, ct);
            EnsureCanManage(t, c.Visibilidade, c);
            await db.CatalogoReceitas.Where(x => x.CatalogoId == id && x.ReceitaId == receitaId).ExecuteDeleteAsync(ct);
            return Results.Ok(new { ok = true });
        }).Leitor();

        g.MapPost("/catalogos/{id:guid}/duplicar", async (Guid id, CatalogoDuplicarRequest? req, TenantContext t, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            req ??= new CatalogoDuplicarRequest();
            var source = await LoadVisible(db, t, id, ct);
            var visibility = req.Visibilidade ?? VisibilidadeCatalogo.Pessoal;
            EnsureCanManage(t, visibility);
            var name = string.IsNullOrWhiteSpace(req.Nome) ? $"{source.Nome} (cópia)" : req.Nome.Trim();
            if (name.Length > 80) name = name[..80];
            var norm = TextNormalizer.Normalize(name);
            await EnsureUniqueName(db, t, norm, visibility, null, ct);
            var copy = new Catalogo
            {
                DespensaId = t.DespensaId, Nome = name, NomeNormalizado = norm, Descricao = source.Descricao, Visibilidade = visibility,
                CriadoPorUsuarioId = t.UsuarioId, CriadoEm = clock.GetUtcNow(),
                UsuarioId = visibility == VisibilidadeCatalogo.Pessoal ? t.UsuarioId : null,
            };
            db.Catalogos.Add(copy);
            var count = 0;
            if (req.IncluirReceitas)
            {
                var recipeIds = await db.CatalogoReceitas.AsNoTracking().Where(x => x.CatalogoId == id).Select(x => x.ReceitaId).ToListAsync(ct);
                foreach (var rid in recipeIds) db.CatalogoReceitas.Add(new CatalogoReceita { CatalogoId = copy.Id, ReceitaId = rid });
                count = recipeIds.Count;
            }
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/catalogos/{copy.Id}", Dto(copy, count, t.UsuarioId));
        }).Leitor();
    }
}
