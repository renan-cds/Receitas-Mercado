using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;

namespace ReceitasMercado.Api.Endpoints;

public static class FileEndpoints
{
    private const long MaxBytes = 2 * 1024 * 1024;

    private static string? Sniff(ReadOnlySpan<byte> head) =>
        head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF ? "image/jpeg"
        : head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 ? "image/png"
        : head.Length >= 12 && head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46
            && head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50 ? "image/webp"
        : null;

    private static string Extension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        _ => ".webp",
    };

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/arquivos").RequireAuthorization();

        g.MapPost("", async (
            IFormFile arquivo, TenantContext t, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (arquivo.Length == 0) throw BusinessException.BadRequest("ARQUIVO_VAZIO", "O arquivo está vazio.");
            if (arquivo.Length > MaxBytes)
                throw BusinessException.BadRequest("ARQUIVO_GRANDE", "A imagem pode ter no máximo 2 MB.");

            await using var input = arquivo.OpenReadStream();
            var buffer = new byte[MaxBytes + 1];
            var read = 0;
            int n;
            while (read < buffer.Length && (n = await input.ReadAsync(buffer.AsMemory(read), ct)) > 0) read += n;
            if (read > MaxBytes) throw BusinessException.BadRequest("ARQUIVO_GRANDE", "A imagem pode ter no máximo 2 MB.");
            var contentType = Sniff(buffer.AsSpan(0, Math.Min(read, 16)))
                ?? throw BusinessException.BadRequest("TIPO_NAO_SUPORTADO", "Envie uma imagem JPEG, PNG ou WebP.");

            var id = Guid.NewGuid();
            var relative = $"{t.DespensaId}/{id}{Extension(contentType)}";
            var bytes = buffer.AsSpan(0, read).ToArray();
            var name = Path.GetFileName(arquivo.FileName);
            db.Arquivos.Add(new Arquivo
            {
                Id = id, DespensaId = t.DespensaId, NomeOriginal = name.Length > 120 ? name[..120] : name, ContentType = contentType,
                TamanhoBytes = read, CaminhoRelativo = relative, Conteudo = bytes, CriadoPorUsuarioId = t.UsuarioId, CriadoEm = clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/arquivos/{id}", new { id, contentType, tamanhoBytes = read });
        }).Editor().DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(MaxBytes + 64 * 1024));

        api.MapGroup("").RequireAuthorization().MapGet("/despensas/{despensaId:guid}/arquivos/{id:guid}", async (
            Guid id, TenantContext t, AppDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var file = await db.Arquivos.AsNoTracking()
                .Where(a => a.Id == id && a.DespensaId == t.DespensaId)
                .Select(a => new { a.Conteudo, a.ContentType })
                .FirstOrDefaultAsync(ct) ?? throw BusinessException.NotFound("Arquivo");
            if (file.Conteudo.Length == 0) throw BusinessException.NotFound("Arquivo");
            http.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.File(file.Conteudo, file.ContentType, enableRangeProcessing: false);
        }).Leitor();
    }
}
