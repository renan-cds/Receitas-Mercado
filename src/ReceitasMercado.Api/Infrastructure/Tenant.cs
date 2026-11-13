using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Data;

namespace ReceitasMercado.Api.Infrastructure;

public sealed record TenantRequirement(Papel Minimo);

public sealed record TenantContext(Guid DespensaId, Guid UsuarioId, Papel Papel)
{
    public const string ItemKey = "rm.tenant";

    public static ValueTask<TenantContext?> BindAsync(HttpContext context) =>
        ValueTask.FromResult(context.Items[ItemKey] as TenantContext);

    public bool CanEdit => Papel >= Papel.Editor;
}

public static class UserExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static Guid RequireUserId(this ClaimsPrincipal user) =>
        user.GetUserId() ?? throw new BusinessException(401, "NAO_AUTENTICADO", "Faça login para continuar.");
}

public static class TenantEndpointExtensions
{
    public static TBuilder Leitor<TBuilder>(this TBuilder b) where TBuilder : IEndpointConventionBuilder =>
        b.WithMetadata(new TenantRequirement(Papel.Leitor));

    public static TBuilder Editor<TBuilder>(this TBuilder b) where TBuilder : IEndpointConventionBuilder =>
        b.WithMetadata(new TenantRequirement(Papel.Editor));

    public static TBuilder Admin<TBuilder>(this TBuilder b) where TBuilder : IEndpointConventionBuilder =>
        b.WithMetadata(new TenantRequirement(Papel.Admin));
}

/// <summary>
/// Resolve a despensa e o papel do usuário. Quem não é membro recebe 404 (não vaza existência);
/// quem é membro sem papel suficiente recebe 403.
/// </summary>
public sealed class TenantMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Despensa-Id";

    public async Task InvokeAsync(HttpContext http, AppDbContext db)
    {
        var requirement = http.GetEndpoint()?.Metadata.GetMetadata<TenantRequirement>();
        if (requirement is null)
        {
            await next(http);
            return;
        }

        var userId = http.User.RequireUserId();
        Guid despensaId;
        if (http.Request.RouteValues.TryGetValue("despensaId", out var routeValue) &&
            Guid.TryParse(routeValue?.ToString(), out var fromRoute))
            despensaId = fromRoute;
        else if (Guid.TryParse(http.Request.Headers[HeaderName].FirstOrDefault(), out var fromHeader))
            despensaId = fromHeader;
        else
            throw BusinessException.BadRequest("DESPENSA_OBRIGATORIA", $"Informe a despensa no cabeçalho {HeaderName}.");

        var papel = await db.Membros.AsNoTracking()
            .Where(m => m.DespensaId == despensaId && m.UsuarioId == userId && m.Status == StatusMembro.Ativo)
            .Select(m => (Papel?)m.Papel)
            .FirstOrDefaultAsync(http.RequestAborted);

        if (papel is null) throw BusinessException.NotFound("Despensa");
        if (papel < requirement.Minimo) throw BusinessException.Forbidden();

        http.Items[TenantContext.ItemKey] = new TenantContext(despensaId, userId, papel.Value);
        await next(http);
    }
}
