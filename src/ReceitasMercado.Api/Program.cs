using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Endpoints;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Api.Integrations;
using ReceitasMercado.Api.Services;
using ReceitasMercado.Domain;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

var config = builder.Configuration;

var port = Environment.GetEnvironmentVariable("PORT") ?? Environment.GetEnvironmentVariable("WEBSITES_PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://+:{port}");

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(config.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Configure ConnectionStrings:Default."))
    .UseSnakeCaseNamingConvention());

builder.Services.AddDataProtection()
    .SetApplicationName("ReceitasMercado")
    .PersistKeysToDbContext<AppDbContext>();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddIdentityCore<Usuario>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 4;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "rm.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => Reject(context.HttpContext, 401, "NAO_AUTENTICADO", "Faça login para continuar.");
    options.Events.OnRedirectToAccessDenied = context => Reject(context.HttpContext, 403, "SEM_PERMISSAO", "Você não tem permissão para esta ação.");
});
builder.Services.AddAuthorization();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { codigo = "MUITAS_REQUISICOES", mensagem = "Muitas tentativas. Aguarde um instante e tente novamente.", detalhes = (object?)null }, token);
    };
    var authLimit = config.GetValue("RateLimit:AuthPerMinute", 20);
    options.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<HourlyLimiter>();
builder.Services.Configure<ImportOptions>(config.GetSection("Import"));
builder.Services.AddSingleton<IPageFetcher, SafePageFetcher>();

builder.Services.AddHttpClient<IRecipeSearchClient, TheMealDbClient>(client =>
{
    client.BaseAddress = new Uri(config["Integrations:TheMealDbBaseUrl"] ?? "https://www.themealdb.com/api/json/v1/1/");
    client.Timeout = TimeSpan.FromSeconds(6);
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ReceitasMercado", "1.0"));
});

var allowPrivateAi = config.GetValue("Ai:AllowPrivateEndpoints", false);
builder.Services.AddHttpClient("ai", client => client.Timeout = TimeSpan.FromSeconds(config.GetValue("Ai:TimeoutSeconds", 45)))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
            if (!allowPrivateAi && addresses.Any(UrlGuard.IsBlocked))
                throw new HttpRequestException("Endereço do provedor de IA não permitido.");
            var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(addresses[0], context.DnsEndPoint.Port), ct);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    });
builder.Services.AddSingleton<IAiCompletionClient, OpenAiCompatibleClient>();

builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<RecipeService>();
builder.Services.AddScoped<PreparoService>();
builder.Services.AddScoped<ShoppingService>();
builder.Services.AddScoped<AiService>();
builder.Services.AddScoped<DraftBuilder>();
builder.Services.AddScoped<PantryService>();
builder.Services.AddScoped<TransactionalIdempotentFilter>();
builder.Services.AddHostedService<MaintenanceService>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

if (config.GetValue("Database:MigrateOnStart", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

if (config.GetValue("ForwardedHeaders:Enabled", true)) app.UseForwardedHeaders();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = context =>
    {
        var name = context.File.Name;
        var headers = context.Context.Response.Headers;
        headers.CacheControl = context.Context.Request.Path.StartsWithSegments("/assets")
            ? "public, max-age=31536000, immutable"
            : name is "index.html" or "sw.js" or "manifest.webmanifest" ? "no-cache" : "public, max-age=3600";
    },
});

app.UseRouting();
app.UseRateLimiter();
app.UseMiddleware<CsrfGuardMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ok" })
        : Results.Json(new { status = "indisponivel" }, statusCode: 503));

var api = app.MapGroup("/api");
AuthEndpoints.Map(api);
PantryEndpoints.Map(api);
StockEndpoints.Map(api);
RecipeEndpoints.Map(api);
CollectionEndpoints.Map(api);
PreparoEndpoints.Map(api);
ShoppingEndpoints.Map(api);
AiEndpoints.Map(api);
FileEndpoints.Map(api);
api.MapFallback(() => Results.Json(
    new { codigo = "NAO_ENCONTRADO", mensagem = "Rota não encontrada.", detalhes = (object?)null }, statusCode: 404));

app.MapFallbackToFile("index.html");

await app.RunAsync();

static Task Reject(HttpContext http, int status, string code, string message)
{
    http.Response.StatusCode = status;
    return http.Response.WriteAsJsonAsync(new { codigo = code, mensagem = message, detalhes = (object?)null });
}

public partial class Program;
