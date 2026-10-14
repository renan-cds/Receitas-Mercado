using Microsoft.Extensions.Options;
using ReceitasMercado.Api.Integrations;
using ReceitasMercado.Domain;

namespace ReceitasMercado.IntegrationTests;

public class SafeFetcherTests
{
    [Theory]
    [InlineData("https://localhost/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://169.254.169.254/")]
    [InlineData("https://192.168.1.10/")]
    public async Task The_real_fetcher_refuses_to_connect_to_internal_addresses(string url)
    {
        using var fetcher = new SafePageFetcher(Options.Create(new ImportOptions()));
        var error = await Assert.ThrowsAsync<ImportException>(() => fetcher.FetchAsync(new Uri(url), CancellationToken.None));
        Assert.Equal(UrlGuard.BlockedCode, error.Code);
    }
}
