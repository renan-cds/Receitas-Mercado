namespace ReceitasMercado.Api.Infrastructure;

public sealed class BusinessException(int status, string code, string message, object? details = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public object? Details { get; } = details;

    public static BusinessException NotFound(string what = "Recurso") =>
        new(404, "NAO_ENCONTRADO", $"{what} não encontrado.");

    public static BusinessException Forbidden(string message = "Você não tem permissão para esta ação.") =>
        new(403, "SEM_PERMISSAO", message);

    public static BusinessException BadRequest(string code, string message, object? details = null) =>
        new(400, code, message, details);

    public static BusinessException Conflict(string code, string message, object? details = null) =>
        new(409, code, message, details);
}
