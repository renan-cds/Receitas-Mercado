using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReceitasMercado.Api.Data;

namespace ReceitasMercado.Api.Infrastructure;

public static class ContractTypes
{
    public const string Namespace = "ReceitasMercado.Api.Contracts";

    public static bool IsContract(object? value) =>
        value is not null && value.GetType().Namespace == Namespace;
}

public sealed class ValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var argument in context.Arguments)
        {
            if (ContractTypes.IsContract(argument)) Validate(argument!, string.Empty, errors, 0);
        }
        if (errors.Count > 0)
            throw BusinessException.BadRequest("VALIDACAO", "Alguns campos são inválidos.", errors);
        return await next(context);
    }

    private static void Validate(object value, string path, Dictionary<string, string[]> errors, int depth)
    {
        if (depth > 5) return;
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        foreach (var r in results)
        {
            var members = r.MemberNames.Any() ? r.MemberNames : [string.Empty];
            foreach (var member in members)
            {
                var key = Join(path, Camel(member));
                errors[key] = [.. errors.GetValueOrDefault(key, []), r.ErrorMessage ?? "Valor inválido."];
            }
        }

        foreach (var property in value.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length > 0) continue;
            var child = property.GetValue(value);
            if (child is null || child is string) continue;
            var childPath = Join(path, Camel(property.Name));
            if (child is IEnumerable enumerable)
            {
                var i = 0;
                foreach (var item in enumerable)
                {
                    if (ContractTypes.IsContract(item)) Validate(item!, $"{childPath}[{i}]", errors, depth + 1);
                    i++;
                }
            }
            else if (ContractTypes.IsContract(child))
            {
                Validate(child, childPath, errors, depth + 1);
            }
        }
    }

    private static string Join(string path, string member) =>
        path.Length == 0 ? member : member.Length == 0 ? path : $"{path}.{member}";

    private static string Camel(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}

/// <summary>
/// Executa o handler numa transação. Com o cabeçalho Idempotency-Key, repetições devolvem a resposta
/// original e uma chave reutilizada com outro corpo é rejeitada.
/// </summary>
public sealed class TransactionalIdempotentFilter(
    AppDbContext db,
    IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions,
    TimeProvider clock) : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var ct = http.RequestAborted;
        var key = http.Request.Headers[HeaderName].FirstOrDefault()?.Trim();
        if (key is { Length: > 100 })
            throw BusinessException.BadRequest("IDEMPOTENCY_KEY_INVALIDA", "Idempotency-Key deve ter até 100 caracteres.");
        if (string.IsNullOrEmpty(key)) key = null;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var userId = http.User.RequireUserId();
        var route = $"{http.Request.Method} {http.Request.Path}";
        var options = jsonOptions.Value.SerializerOptions;
        string? hash = null;

        if (key is not null)
        {
            var lockKey = $"{userId}|{route}|{key}";
            await db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);

            var payload = JsonSerializer.Serialize(
                context.Arguments.Where(ContractTypes.IsContract).Cast<object>().ToArray(), options);
            hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

            var existing = await db.Idempotencias.AsNoTracking()
                .FirstOrDefaultAsync(i => i.UsuarioId == userId && i.Rota == route && i.Chave == key, ct);
            if (existing is not null)
            {
                if (existing.RequestHash != hash)
                    throw BusinessException.Conflict("IDEMPOTENCY_KEY_REUTILIZADA",
                        "Esta Idempotency-Key já foi usada com outro conteúdo.");
                http.Response.Headers["Idempotency-Replayed"] = "true";
                return Results.Text(existing.ResponseBody, "application/json", Encoding.UTF8, existing.StatusCode);
            }
        }

        var result = await next(context);
        var (status, value) = Describe(result);

        if (status is < 200 or >= 300)
        {
            await tx.RollbackAsync(ct);
            return result;
        }

        if (key is not null)
        {
            db.Idempotencias.Add(new Idempotencia
            {
                UsuarioId = userId,
                Rota = route,
                Chave = key,
                RequestHash = hash!,
                StatusCode = status,
                ResponseBody = JsonSerializer.Serialize(value, options),
                CriadoEm = clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        return result;
    }

    private static (int Status, object? Value) Describe(object? result) => result switch
    {
        null => (200, null),
        IValueHttpResult { Value: var v } and IStatusCodeHttpResult { StatusCode: { } s } => (s, v),
        IStatusCodeHttpResult { StatusCode: { } s } => (s, null),
        IResult => (200, null),
        _ => (200, result),
    };
}

public static class FilterExtensions
{
    public static RouteHandlerBuilder Transactional(this RouteHandlerBuilder b) =>
        b.AddEndpointFilter<TransactionalIdempotentFilter>();
}

public sealed class HourlyLimiter(TimeProvider clock)
{
    private readonly Dictionary<string, Queue<DateTimeOffset>> _hits = [];
    private readonly object _lock = new();

    public bool TryAcquire(string key, int limitPerHour)
    {
        var now = clock.GetUtcNow();
        lock (_lock)
        {
            if (!_hits.TryGetValue(key, out var queue)) _hits[key] = queue = new Queue<DateTimeOffset>();
            while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromHours(1)) queue.Dequeue();
            if (queue.Count >= limitPerHour) return false;
            queue.Enqueue(now);
            return true;
        }
    }
}
