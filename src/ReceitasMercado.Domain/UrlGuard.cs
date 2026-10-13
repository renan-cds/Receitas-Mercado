using System.Net;
using System.Net.Sockets;

namespace ReceitasMercado.Domain;

public static class UrlGuard
{
    public const string BlockedCode = "URL_BLOQUEADA";

    /// <summary>Retorna null se a URL é aceitável; senão uma mensagem de motivo.</summary>
    public static string? ValidateUrl(string? raw, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 2048) return "URL inválida.";
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var parsed)) return "URL inválida.";
        if (parsed.Scheme != Uri.UriSchemeHttps) return "Apenas URLs https são aceitas.";
        if (!string.IsNullOrEmpty(parsed.UserInfo)) return "URLs com credenciais não são aceitas.";
        if (parsed.Port != 443) return "Apenas a porta padrão (443) é aceita.";
        if (string.IsNullOrWhiteSpace(parsed.Host)) return "URL inválida.";
        if (IPAddress.TryParse(parsed.Host.Trim('[', ']'), out var ip) && IsBlocked(ip))
            return "O endereço informado não é permitido.";
        var host = parsed.Host.ToLowerInvariant();
        if (host == "localhost" || host.EndsWith(".localhost") || host.EndsWith(".local") || host.EndsWith(".internal"))
            return "O endereço informado não é permitido.";
        uri = parsed;
        return null;
    }

    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0
                || b[0] == 10
                || (b[0] == 100 && b[1] is >= 64 and <= 127)
                || b[0] == 127
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 198 && b[1] is 18 or 19)
                || b[0] >= 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any))
                return true;
            var b = address.GetAddressBytes();
            if (b[0] == 0xfe && (b[1] & 0xc0) == 0x80) return true;
            if ((b[0] & 0xfe) == 0xfc) return true;
            if (b[0] == 0xff) return true;
            if (b[0] == 0x20 && b[1] == 0x02) return IsBlocked(new IPAddress(b[2..6]));
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b) return IsBlocked(new IPAddress(b[12..16]));
            return false;
        }

        return true;
    }
}
