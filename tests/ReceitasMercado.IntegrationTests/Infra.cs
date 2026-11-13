using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using ReceitasMercado.Api.Integrations;
using ReceitasMercado.Domain;

namespace ReceitasMercado.IntegrationTests;

public sealed class FakeAi : IAiCompletionClient
{
    public Func<string>? Handler { get; set; }
    public int Calls;

    public Task<string> CompleteAsync(AiEndpoint endpoint, IReadOnlyList<AiMessage> messages, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        if (Handler is null) throw new AiUnavailableException("sem handler");
        return Task.FromResult(Handler());
    }
}

public sealed class FakeFetcher : IPageFetcher
{
    public Func<Uri, FetchedPage>? Handler { get; set; }
    public Task<FetchedPage> FetchAsync(Uri uri, CancellationToken ct) =>
        Task.FromResult(Handler?.Invoke(uri) ?? throw new ImportException("FALHA_DE_REDE", "sem rede"));
}

public sealed class FakeSearch : IRecipeSearchClient
{
    public IReadOnlyList<ImportedRecipe> Results { get; set; } = [];
    public Task<IReadOnlyList<ImportedRecipe>> SearchAsync(string query, CancellationToken ct) => Task.FromResult(Results);
}

public sealed class AppFactory : WebApplicationFactory<Program>
{
    private static readonly string Admin = Environment.GetEnvironmentVariable("RM_TEST_ADMIN_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=postgres;Username=rm;Password=rm_dev_pw";

    public string DatabaseName { get; } = "rm_test_" + Guid.NewGuid().ToString("N")[..12];
    public FakeAi Ai { get; } = new();
    public FakeFetcher Fetcher { get; } = new();
    public FakeSearch Search { get; } = new();

    public AppFactory()
    {
        using var connection = new NpgsqlConnection(Admin);
        connection.Open();
        using var command = new NpgsqlCommand($"create database \"{DatabaseName}\"", connection);
        command.ExecuteNonQuery();
    }

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(Admin) { Database = DatabaseName, MaxPoolSize = 60 }.ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("RateLimit:AuthPerMinute", "100000");
        builder.UseSetting("Ai:AllowPrivateEndpoints", "true");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiCompletionClient>();
            services.AddSingleton<IAiCompletionClient>(Ai);
            services.RemoveAll<IPageFetcher>();
            services.AddSingleton<IPageFetcher>(Fetcher);
            services.RemoveAll<IRecipeSearchClient>();
            services.AddSingleton<IRecipeSearchClient>(Search);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        NpgsqlConnection.ClearAllPools();
        try
        {
            using var connection = new NpgsqlConnection(Admin);
            connection.Open();
            using var command = new NpgsqlCommand($"drop database if exists \"{DatabaseName}\" with (force)", connection);
            command.ExecuteNonQuery();
        }
        catch (NpgsqlException) { }
    }
}

public sealed record Api(HttpClient Http, AppFactory Factory)
{
    public string Email { get; init; } = "";
    public Guid UserId { get; init; }
    public Guid DespensaId { get; set; }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public HttpRequestMessage Build(HttpMethod method, string url, object? body = null, string? key = null, bool tenant = true)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Requested-With", "tests");
        if (tenant && DespensaId != Guid.Empty) request.Headers.Add("X-Despensa-Id", DespensaId.ToString());
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        return request;
    }

    public async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> Send(
        HttpMethod method, string url, object? body = null, string? key = null, bool tenant = true)
    {
        var response = await Http.SendAsync(Build(method, url, body, key, tenant));
        var text = await response.Content.ReadAsStringAsync();
        var element = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (response.StatusCode, element, response);
    }

    public Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> Get(string url) => Send(HttpMethod.Get, url);
    public Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> Post(string url, object? body = null, string? key = null) =>
        Send(HttpMethod.Post, url, body ?? new { }, key);
    public Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> Put(string url, object? body = null) => Send(HttpMethod.Put, url, body ?? new { });
    public Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> Patch(string url, object body) => Send(HttpMethod.Patch, url, body);
    public Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> Delete(string url, object? body = null) => Send(HttpMethod.Delete, url, body);

    public async Task<JsonElement> Ok(Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> call)
    {
        var (status, body, _) = await call;
        Assert.True((int)status is >= 200 and < 300, $"Esperava sucesso, recebeu {(int)status}: {body}");
        return body;
    }

    public async Task<Guid> Category(string? name = null)
    {
        var cats = await Ok(Get("/api/categorias"));
        return cats.EnumerateArray().First(c => name is null || c.GetProperty("nome").GetString() == name).GetProperty("id").GetGuid();
    }

    public async Task<Guid> NewIngredient(string name, string unit = "G", decimal minimum = 0, Guid? groupId = null)
    {
        var body = await Ok(Post("/api/ingredientes", new
        {
            nome = name, categoriaId = await Category(), unidadeReferencia = unit, quantidadeMinima = minimum, grupoEquivalenciaId = groupId,
        }));
        return body.GetProperty("id").GetGuid();
    }

    public Task<JsonElement> Stock(Guid ingredientId, decimal quantity, string? unit = null, string? expiry = null, string? key = null) =>
        Ok(Post("/api/estoque/entradas", new { ingredienteId = ingredientId, quantidade = quantity, unidade = unit, dataValidade = expiry }, key));

    public async Task<decimal> Total(Guid ingredientId) =>
        (await Ok(Get($"/api/ingredientes/{ingredientId}"))).GetProperty("quantidadeTotal").GetDecimal();

    public async Task<Guid> NewRecipe(string title, IEnumerable<object> lines, decimal servings = 4, string status = "completa")
    {
        var body = await Ok(Post("/api/receitas", new
        {
            titulo = title, porcoes = servings, status, etapas = new[] { "Misture tudo." }, ingredientes = lines,
        }));
        return body.GetProperty("id").GetGuid();
    }

    public static object Line(string name, Guid? ingredientId, decimal? quantity, string? unit, bool optional = false, string? confidence = null) =>
        new { nome = name, ingredienteId = ingredientId, quantidade = quantity, unidade = unit, opcional = optional, confianca = confidence };
}

public static class Env
{
    public static async Task<Api> NewUser(AppFactory factory, string? email = null, string name = "Pessoa")
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        email ??= $"u{Guid.NewGuid():N}@teste.com";
        var api = new Api(http, factory) { Email = email };
        var me = await api.Ok(api.Send(HttpMethod.Post, "/api/auth/registrar", new { email, senha = "senha-segura-123", nome = name }, tenant: false));
        return api with { UserId = me.GetProperty("id").GetGuid(), DespensaId = me.GetProperty("despensaAtivaId").GetGuid() };
    }

    public static async Task<Api> Join(AppFactory factory, Api admin, string role, string name = "Convidado")
    {
        var invite = await admin.Ok(admin.Post($"/api/despensas/{admin.DespensaId}/convites", new { papel = role }));
        var guest = await NewUser(factory, name: name);
        await guest.Ok(guest.Send(HttpMethod.Post, $"/api/convites/{invite.GetProperty("token").GetString()}/aceitar", new { }, tenant: false));
        guest.DespensaId = admin.DespensaId;
        return guest;
    }
}
