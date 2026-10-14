using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Contracts;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Api.Services;

namespace ReceitasMercado.Api.Endpoints;

public static class AuthEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        var anon = api.MapGroup("/auth").AddEndpointFilter<ValidationFilter>().RequireRateLimiting("auth");
        anon.MapPost("/registrar", Registrar);
        anon.MapPost("/entrar", Entrar);

        var auth = api.MapGroup("/auth").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        auth.MapPost("/sair", Sair);
        auth.MapGet("/me", async (HttpContext http, UserManager<Usuario> users, AppDbContext db, CancellationToken ct) =>
            Results.Ok(await Me(await users.GetUserAsync(http.User) ?? throw BusinessException.NotFound("Usuário"), db, ct)));
        auth.MapPut("/me", AtualizarPerfil);
        auth.MapPut("/senha", AlterarSenha);
        auth.MapDelete("/me", ExcluirConta);
    }

    private static string Describe(IdentityError error) => error.Code switch
    {
        "DuplicateUserName" or "DuplicateEmail" => "Este e-mail já está em uso.",
        "InvalidEmail" or "InvalidUserName" => "E-mail inválido.",
        "PasswordTooShort" => "A senha deve ter pelo menos 10 caracteres.",
        "PasswordRequiresUniqueChars" => "A senha é muito repetitiva.",
        "PasswordMismatch" => "Senha atual incorreta.",
        _ => error.Description,
    };

    private static async Task<object> Me(Usuario user, AppDbContext db, CancellationToken ct)
    {
        var pantries = await db.Membros.AsNoTracking()
            .Where(m => m.UsuarioId == user.Id && m.Status == StatusMembro.Ativo)
            .OrderBy(m => m.EntrouEm)
            .Select(m => new { id = m.DespensaId, nome = m.Despensa!.Nome, papel = m.Papel })
            .ToListAsync(ct);
        var active = pantries.Any(p => p.id == user.DespensaAtivaId) ? user.DespensaAtivaId : pantries.FirstOrDefault()?.id;
        return new { id = user.Id, email = user.Email, nome = user.NomeExibicao, despensaAtivaId = active, despensas = pantries };
    }

    private static async Task<IResult> Registrar(
        RegistrarRequest req, UserManager<Usuario> users, SignInManager<Usuario> signIn, AppDbContext db,
        PantryService pantry, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var email = req.Email.Trim();
        var user = new Usuario { UserName = email, Email = email, NomeExibicao = req.Nome.Trim() };
        var created = await users.CreateAsync(user, req.Senha);
        if (!created.Succeeded)
        {
            var duplicate = created.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
            throw new BusinessException(duplicate ? 409 : 400, duplicate ? "EMAIL_EM_USO" : "SENHA_INVALIDA",
                string.Join(" ", created.Errors.Select(Describe).Distinct()));
        }

        var despensa = pantry.CreateWithAdmin(user.Id, $"Despensa de {user.NomeExibicao}");
        user.DespensaAtivaId = despensa.Id;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await signIn.SignInAsync(user, isPersistent: true);
        return Results.Ok(await Me(user, db, ct));
    }

    private static async Task<IResult> Entrar(
        EntrarRequest req, UserManager<Usuario> users, SignInManager<Usuario> signIn, AppDbContext db, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email.Trim());
        if (user is null)
            throw new BusinessException(401, "CREDENCIAIS_INVALIDAS", "E-mail ou senha incorretos.");

        var result = await signIn.PasswordSignInAsync(user, req.Senha, isPersistent: true, lockoutOnFailure: true);
        if (result.IsLockedOut)
            throw new BusinessException(429, "CONTA_BLOQUEADA", "Muitas tentativas de login. Aguarde alguns minutos e tente novamente.");
        if (!result.Succeeded)
            throw new BusinessException(401, "CREDENCIAIS_INVALIDAS", "E-mail ou senha incorretos.");
        return Results.Ok(await Me(user, db, ct));
    }

    private static async Task<IResult> Sair(SignInManager<Usuario> signIn)
    {
        await signIn.SignOutAsync();
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> AtualizarPerfil(
        AtualizarPerfilRequest req, HttpContext http, UserManager<Usuario> users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.GetUserAsync(http.User) ?? throw BusinessException.NotFound("Usuário");
        if (req.DespensaAtivaId is { } id)
        {
            var member = await db.Membros.AnyAsync(m => m.DespensaId == id && m.UsuarioId == user.Id && m.Status == StatusMembro.Ativo, ct);
            if (!member) throw BusinessException.NotFound("Despensa");
            user.DespensaAtivaId = id;
        }
        user.NomeExibicao = req.Nome.Trim();
        await users.UpdateAsync(user);
        return Results.Ok(await Me(user, db, ct));
    }

    private static async Task<IResult> AlterarSenha(
        AlterarSenhaRequest req, HttpContext http, UserManager<Usuario> users, SignInManager<Usuario> signIn)
    {
        var user = await users.GetUserAsync(http.User) ?? throw BusinessException.NotFound("Usuário");
        var result = await users.ChangePasswordAsync(user, req.Atual, req.Nova);
        if (!result.Succeeded)
            throw BusinessException.BadRequest("SENHA_INVALIDA", string.Join(" ", result.Errors.Select(Describe).Distinct()));
        await signIn.RefreshSignInAsync(user);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> ExcluirConta(
        [Microsoft.AspNetCore.Mvc.FromBody] ExcluirContaRequest req, HttpContext http, UserManager<Usuario> users, SignInManager<Usuario> signIn,
        AppDbContext db, PantryService pantry, CancellationToken ct)
    {
        var user = await users.GetUserAsync(http.User) ?? throw BusinessException.NotFound("Usuário");
        if (!await users.CheckPasswordAsync(user, req.Senha))
            throw BusinessException.BadRequest("SENHA_INVALIDA", "Senha incorreta.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var memberships = await db.Membros.Where(m => m.UsuarioId == user.Id && m.Status == StatusMembro.Ativo).ToListAsync(ct);
        foreach (var membership in memberships)
        {
            var others = await db.Membros.CountAsync(m => m.DespensaId == membership.DespensaId && m.Status == StatusMembro.Ativo && m.UsuarioId != user.Id, ct);
            if (others == 0)
            {
                await pantry.DeletePantry(membership.DespensaId, ct);
                continue;
            }
            await pantry.RemoveMember(membership.DespensaId, user.Id, ct);
        }

        var id = user.Id;
        await db.Receitas.Where(r => r.CriadoPorUsuarioId == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.CriadoPorUsuarioId, (Guid?)null), ct);
        await db.ListasCompra.Where(l => l.CriadoPorUsuarioId == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.CriadoPorUsuarioId, (Guid?)null), ct);
        await db.Arquivos.Where(a => a.CriadoPorUsuarioId == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.CriadoPorUsuarioId, (Guid?)null), ct);
        await db.Despensas.Where(d => d.CriadoPorUsuarioId == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.CriadoPorUsuarioId, (Guid?)null), ct);
        await db.Catalogos.Where(c => c.CriadoPorUsuarioId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.CriadoPorUsuarioId, (Guid?)null), ct);
        await db.Convites.Where(c => c.CriadoPorUsuarioId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.CriadoPorUsuarioId, (Guid?)null), ct);
        await db.ExecucoesIa.Where(e => e.UsuarioId == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.UsuarioId, (Guid?)null), ct);
        await db.Idempotencias.Where(i => i.UsuarioId == id).ExecuteDeleteAsync(ct);

        db.ChangeTracker.Clear();
        var fresh = await users.FindByIdAsync(id.ToString()) ?? throw BusinessException.NotFound("Usuário");
        var deleted = await users.DeleteAsync(fresh);
        if (!deleted.Succeeded) throw BusinessException.Conflict("EXCLUSAO_FALHOU", "Não foi possível excluir a conta.");
        await tx.CommitAsync(ct);
        await signIn.SignOutAsync();
        return Results.Ok(new { ok = true });
    }
}
