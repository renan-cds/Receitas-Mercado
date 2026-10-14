using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ReceitasMercado.Api.Integrations;

public sealed record AiEndpoint(string BaseUrl, string Model, string? ApiKey);

public sealed record AiMessage(string Role, string Content);

public sealed class AiUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public interface IAiCompletionClient
{
    Task<string> CompleteAsync(AiEndpoint endpoint, IReadOnlyList<AiMessage> messages, CancellationToken ct);
}

/// <summary>Cliente para qualquer endpoint compatível com POST /chat/completions (Ollama, Groq, Gemini, etc.).</summary>
public sealed class OpenAiCompatibleClient(IHttpClientFactory factory) : IAiCompletionClient
{
    public async Task<string> CompleteAsync(AiEndpoint endpoint, IReadOnlyList<AiMessage> messages, CancellationToken ct)
    {
        var client = factory.CreateClient("ai");
        var url = endpoint.BaseUrl.TrimEnd('/') + "/chat/completions";

        async Task<HttpResponseMessage> Send(bool jsonMode)
        {
            var body = new Dictionary<string, object?>
            {
                ["model"] = endpoint.Model,
                ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }),
                ["temperature"] = 0.4,
            };
            if (jsonMode) body["response_format"] = new { type = "json_object" };
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(endpoint.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
            return await client.SendAsync(request, ct);
        }

        try
        {
            using var response = await Send(jsonMode: true);
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                response.Dispose();
                using var retry = await Send(jsonMode: false);
                return await Read(retry, ct);
            }
            return await Read(response, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException("Não foi possível contatar o provedor de IA.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AiUnavailableException("O provedor de IA demorou demais para responder.", ex);
        }
    }

    private static async Task<string> Read(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            throw new AiUnavailableException($"O provedor de IA respondeu com erro {(int)response.StatusCode}.");
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return string.IsNullOrWhiteSpace(content) ? throw new AiUnavailableException("O provedor de IA devolveu uma resposta vazia.") : content;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new AiUnavailableException("Resposta inesperada do provedor de IA.", ex);
        }
    }
}
