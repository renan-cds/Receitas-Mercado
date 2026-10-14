using System.Net;
using System.Net.Sockets;
using System.Text;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Integrations;

public sealed class ImportException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record FetchedPage(string Body, string ContentType, Uri FinalUri);

public interface IPageFetcher
{
    Task<FetchedPage> FetchAsync(Uri uri, CancellationToken ct);
}

public sealed class BlockedAddressException(string message) : HttpRequestException(message);

public sealed class ImportOptions
{
    public int TimeoutSeconds { get; set; } = 5;
    public int MaxBytes { get; set; } = 1_500_000;
    public int MaxRedirects { get; set; } = 2;
}

/// <summary>
/// Busca páginas públicas com proteção contra SSRF: o endereço IP é validado no momento da conexão
/// (resistente a DNS rebinding), redirecionamentos são revalidados a cada salto e o corpo é limitado.
/// </summary>
public sealed class SafePageFetcher : IPageFetcher, IDisposable
{
    private readonly HttpClient _client;
    private readonly ImportOptions _options;

    public SafePageFetcher(Microsoft.Extensions.Options.IOptions<ImportOptions> options)
    {
        _options = options.Value;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(_options.TimeoutSeconds),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = ConnectGuardedAsync,
        };
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static async ValueTask<Stream> ConnectGuardedAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        if (addresses.Length == 0) throw new HttpRequestException("Host não resolvido.");
        if (addresses.Any(UrlGuard.IsBlocked)) throw new BlockedAddressException("Endereço de destino não permitido.");

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                socket.Dispose();
                last = ex;
            }
        }
        throw new HttpRequestException("Não foi possível conectar ao site.", last);
    }

    public async Task<FetchedPage> FetchAsync(Uri uri, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            return await FetchCoreAsync(uri, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ImportException("TEMPO_ESGOTADO", "O site demorou demais para responder.");
        }
        catch (BlockedAddressException)
        {
            throw new ImportException(UrlGuard.BlockedCode, "O endereço informado não é permitido.");
        }
        catch (HttpRequestException ex) when (ex.InnerException is BlockedAddressException)
        {
            throw new ImportException(UrlGuard.BlockedCode, "O endereço informado não é permitido.");
        }
        catch (HttpRequestException)
        {
            throw new ImportException("FALHA_DE_REDE", "Não foi possível acessar o site informado.");
        }
    }

    private async Task<FetchedPage> FetchCoreAsync(Uri start, CancellationToken ct)
    {
        var current = start;
        for (var hop = 0; hop <= _options.MaxRedirects; hop++)
        {
            if (UrlGuard.ValidateUrl(current.ToString(), out _) is { } reason)
                throw new ImportException(UrlGuard.BlockedCode, reason);

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/ld+json;q=0.9,*/*;q=0.1");
            request.Headers.TryAddWithoutValidation("User-Agent", "ReceitasMercado/0.1 (importacao manual iniciada pelo usuario)");
            request.Headers.TryAddWithoutValidation("Accept-Language", "pt-BR,pt;q=0.9,en;q=0.5");

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location
                    ?? throw new ImportException("REDIRECIONAMENTO_INVALIDO", "O site redirecionou sem informar o destino.");
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw new ImportException("PAGINA_INDISPONIVEL", $"O site respondeu com erro {(int)response.StatusCode}.");

            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
            if (mediaType is not ("text/html" or "application/xhtml+xml" or "application/ld+json" or "application/json"))
                throw new ImportException("CONTEUDO_NAO_SUPORTADO", "O endereço não aponta para uma página de receita legível.");

            if (response.Content.Headers.ContentLength > _options.MaxBytes)
                throw new ImportException("RESPOSTA_GRANDE_DEMAIS", "A página é grande demais para ser importada.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > _options.MaxBytes)
                    throw new ImportException("RESPOSTA_GRANDE_DEMAIS", "A página é grande demais para ser importada.");
                buffer.Write(chunk, 0, read);
            }

            Encoding encoding;
            try { encoding = Encoding.GetEncoding(response.Content.Headers.ContentType?.CharSet ?? "utf-8"); }
            catch (ArgumentException) { encoding = Encoding.UTF8; }
            return new FetchedPage(encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), mediaType, current);
        }
        throw new ImportException("REDIRECIONAMENTOS_EXCESSIVOS", "O site redirecionou mais vezes do que o permitido.");
    }

    public void Dispose() => _client.Dispose();
}
