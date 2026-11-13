using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ReceitasMercado.Api.Infrastructure;

public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http)
    {
        try
        {
            await next(http);
        }
        catch (BusinessException ex)
        {
            await Write(http, ex.Status, ex.Code, ex.Message, ex.Details);
        }
        catch (BadHttpRequestException)
        {
            await Write(http, 400, "REQUISICAO_INVALIDA", "Requisição inválida ou malformada.");
        }
        catch (JsonException)
        {
            await Write(http, 400, "REQUISICAO_INVALIDA", "Corpo da requisição inválido.");
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            http.Response.StatusCode = 499;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)
        {
            var (status, code, message) = pg.SqlState switch
            {
                "23505" => (409, "CONFLITO", "Já existe um registro com estes dados."),
                "23514" => (409, "VIOLACAO_REGRA", "A operação violaria uma regra de integridade (por exemplo, estoque negativo)."),
                "23503" => (409, "REFERENCIA_EM_USO", "O registro está em uso e não pode ser alterado desta forma."),
                _ => (500, "ERRO_INTERNO", "Erro interno inesperado."),
            };
            if (status == 500) logger.LogError(ex, "Falha de banco ao processar {Path}", http.Request.Path);
            await Write(http, status, code, message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro não tratado em {Method} {Path}", http.Request.Method, http.Request.Path);
            await Write(http, 500, "ERRO_INTERNO", "Erro interno inesperado.", new { requestId = http.TraceIdentifier });
        }
    }

    private static async Task Write(HttpContext http, int status, string code, string message, object? details = null)
    {
        if (http.Response.HasStarted) return;
        http.Response.Clear();
        http.Response.StatusCode = status;
        var options = http.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        await http.Response.WriteAsJsonAsync(new { codigo = code, mensagem = message, detalhes = details }, options);
    }
}

public sealed class CsrfGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        var method = http.Request.Method;
        var unsafeMethod = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));
        if (unsafeMethod && http.Request.Path.StartsWithSegments("/api") &&
            string.IsNullOrEmpty(http.Request.Headers["X-Requested-With"].FirstOrDefault()))
            throw BusinessException.BadRequest("CABECALHO_AUSENTE", "Cabeçalho X-Requested-With obrigatório.");
        await next(http);
    }
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        http.Response.OnStarting(() =>
        {
            var h = http.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "same-origin";
            h["X-Frame-Options"] = "DENY";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            h["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; " +
                "connect-src 'self'; font-src 'self'; manifest-src 'self'; worker-src 'self'; " +
                "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
            if (http.Request.Path.StartsWithSegments("/api") && !h.ContainsKey("Cache-Control")) h["Cache-Control"] = "no-store";
            return Task.CompletedTask;
        });
        return next(http);
    }
}
