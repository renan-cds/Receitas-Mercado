using System.Net;
using System.Text;

namespace ReceitasMercado.IntegrationTests;

public class AuthAndAccessTests(FactoryFixture fx) : IClassFixture<FactoryFixture>
{
    private AppFactory F => fx.Factory;

    [Fact]
    public async Task Register_login_me_and_duplicate_email()
    {
        var a = await Env.NewUser(F, "ana@teste.com", "Ana");
        var me = await a.Ok(a.Get("/api/auth/me"));
        Assert.Equal("Ana", me.GetProperty("nome").GetString());
        Assert.Single(me.GetProperty("despensas").EnumerateArray());

        var other = await Env.NewUser(F, name: "Outra");
        var (dupStatus, dup, _) = await other.Send(HttpMethod.Post, "/api/auth/registrar",
            new { email = "ana@teste.com", senha = "senha-segura-123", nome = "Clone" }, tenant: false);
        Assert.Equal(HttpStatusCode.Conflict, dupStatus);
        Assert.Equal("EMAIL_EM_USO", dup.GetProperty("codigo").GetString());

        var fresh = F.CreateClient();
        var wrong = new Api(fresh, F);
        var (badStatus, bad, _) = await wrong.Send(HttpMethod.Post, "/api/auth/entrar", new { email = "ana@teste.com", senha = "errada-errada" }, tenant: false);
        Assert.Equal(HttpStatusCode.Unauthorized, badStatus);
        Assert.Equal("CREDENCIAIS_INVALIDAS", bad.GetProperty("codigo").GetString());
        await wrong.Ok(wrong.Send(HttpMethod.Post, "/api/auth/entrar", new { email = "ana@teste.com", senha = "senha-segura-123" }, tenant: false));
    }

    [Fact]
    public async Task Creating_pantries_makes_the_user_admin_and_is_capped_at_five()
    {
        var a = await Env.NewUser(F);
        var created = await a.Ok(a.Send(HttpMethod.Post, "/api/despensas", new { nome = "Casa de praia" }, tenant: false));
        Assert.Equal("admin", created.GetProperty("papel").GetString());
        var list = await a.Ok(a.Send(HttpMethod.Get, "/api/despensas", tenant: false));
        Assert.Equal(2, list.GetArrayLength());

        for (var i = 0; i < 3; i++) await a.Ok(a.Send(HttpMethod.Post, "/api/despensas", new { nome = $"Extra {i}" }, tenant: false));
        var (status, body, _) = await a.Send(HttpMethod.Post, "/api/despensas", new { nome = "Demais" }, tenant: false);
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("LIMITE_DESPENSAS", body.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Weak_password_and_invalid_body_use_the_error_contract()
    {
        var api = new Api(F.CreateClient(), F);
        var (status, body, _) = await api.Send(HttpMethod.Post, "/api/auth/registrar", new { email = "nao-e-email", senha = "curta", nome = "" }, tenant: false);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("VALIDACAO", body.GetProperty("codigo").GetString());
        var details = body.GetProperty("detalhes");
        Assert.True(details.TryGetProperty("senha", out _));
        Assert.True(details.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Unauthenticated_requests_get_401_json()
    {
        var api = new Api(F.CreateClient(), F);
        var (status, body, _) = await api.Get("/api/despensas");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("NAO_AUTENTICADO", body.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Unsafe_requests_require_the_csrf_header()
    {
        var a = await Env.NewUser(F);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/categorias")
        {
            Content = new StringContent("{\"nome\":\"X\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Despensa-Id", a.DespensaId.ToString());
        var response = await a.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Non_members_get_404_and_cannot_see_other_pantries()
    {
        var a = await Env.NewUser(F);
        var b = await Env.NewUser(F);
        var id = await a.NewIngredient("Segredo");

        b.DespensaId = a.DespensaId;
        var (status, _, _) = await b.Get("/api/ingredientes");
        Assert.Equal(HttpStatusCode.NotFound, status);
        var (detailStatus, _, _) = await b.Get($"/api/ingredientes/{id}");
        Assert.Equal(HttpStatusCode.NotFound, detailStatus);
        var (despensaStatus, _, _) = await b.Get($"/api/despensas/{a.DespensaId}");
        Assert.Equal(HttpStatusCode.NotFound, despensaStatus);

        var own = await b.Ok(new Api(b.Http, F) { DespensaId = b.DespensaId }.Get("/api/despensas"));
        Assert.DoesNotContain(own.EnumerateArray(), p => p.GetProperty("id").GetGuid() == a.DespensaId);
    }

    [Fact]
    public async Task Resource_ids_from_another_pantry_are_not_reachable()
    {
        var a = await Env.NewUser(F);
        var b = await Env.NewUser(F);
        var ing = await a.NewIngredient("Farinha");
        var (status, _, _) = await b.Post("/api/estoque/entradas", new { ingredienteId = ing, quantidade = 1, unidade = "G" });
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, await a.Total(ing));
    }

    [Fact]
    public async Task Roles_are_enforced_on_the_server()
    {
        var admin = await Env.NewUser(F);
        var editor = await Env.Join(F, admin, "editor", "Edu");
        var reader = await Env.Join(F, admin, "leitor", "Lia");
        var ing = await admin.NewIngredient("Feijão");

        await reader.Ok(reader.Get("/api/ingredientes"));
        var (readerPost, body, _) = await reader.Post("/api/estoque/entradas", new { ingredienteId = ing, quantidade = 1, unidade = "KG" });
        Assert.Equal(HttpStatusCode.Forbidden, readerPost);
        Assert.Equal("SEM_PERMISSAO", body.GetProperty("codigo").GetString());

        await editor.Stock(ing, 1, "KG");
        var (editorInvite, _, _) = await editor.Post($"/api/despensas/{admin.DespensaId}/convites", new { papel = "leitor" });
        Assert.Equal(HttpStatusCode.Forbidden, editorInvite);
        var (editorConfig, _, _) = await editor.Get("/api/ia/config");
        Assert.Equal(HttpStatusCode.Forbidden, editorConfig);

        var members = await admin.Ok(admin.Get($"/api/despensas/{admin.DespensaId}/membros"));
        Assert.Equal(3, members.GetArrayLength());
        var editorsView = await editor.Ok(editor.Get($"/api/despensas/{admin.DespensaId}/membros"));
        Assert.All(editorsView.EnumerateArray(), m => Assert.Equal(System.Text.Json.JsonValueKind.Null, m.GetProperty("email").ValueKind));
    }

    [Fact]
    public async Task Invites_are_single_use_revocable_and_expire_with_410()
    {
        var admin = await Env.NewUser(F);
        var invite = await admin.Ok(admin.Post($"/api/despensas/{admin.DespensaId}/convites", new { papel = "editor" }));
        var token = invite.GetProperty("token").GetString();

        var first = await Env.NewUser(F);
        var accepted = await first.Ok(first.Send(HttpMethod.Post, $"/api/convites/{token}/aceitar", new { }, tenant: false));
        Assert.Equal("editor", accepted.GetProperty("papel").GetString());

        var second = await Env.NewUser(F);
        var (status, body, _) = await second.Send(HttpMethod.Post, $"/api/convites/{token}/aceitar", new { }, tenant: false);
        Assert.Equal(HttpStatusCode.Gone, status);
        Assert.Equal("CONVITE_USADO", body.GetProperty("codigo").GetString());

        var revoked = await admin.Ok(admin.Post($"/api/despensas/{admin.DespensaId}/convites", new { papel = "leitor" }));
        await admin.Ok(admin.Delete($"/api/despensas/{admin.DespensaId}/convites/{revoked.GetProperty("id").GetGuid()}"));
        var (revStatus, rev, _) = await second.Send(HttpMethod.Post, $"/api/convites/{revoked.GetProperty("token").GetString()}/aceitar", new { }, tenant: false);
        Assert.Equal(HttpStatusCode.Gone, revStatus);
        Assert.Equal("CONVITE_REVOGADO", rev.GetProperty("codigo").GetString());

        var (unknownStatus, _, _) = await second.Send(HttpMethod.Get, "/api/convites/token-que-nao-existe", tenant: false);
        Assert.Equal(HttpStatusCode.NotFound, unknownStatus);

        var adminInviteForAdmin = await admin.Post($"/api/despensas/{admin.DespensaId}/convites", new { papel = "admin" });
        Assert.Equal(HttpStatusCode.BadRequest, adminInviteForAdmin.Status);
    }

    [Fact]
    public async Task Concurrent_acceptance_of_one_invite_admits_only_one_person()
    {
        var admin = await Env.NewUser(F);
        var invite = await admin.Ok(admin.Post($"/api/despensas/{admin.DespensaId}/convites", new { papel = "leitor" }));
        var token = invite.GetProperty("token").GetString();
        var users = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Env.NewUser(F)));
        var results = await Task.WhenAll(users.Select(u => u.Send(HttpMethod.Post, $"/api/convites/{token}/aceitar", new { }, tenant: false)));
        Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK));
        Assert.Equal(5, results.Count(r => r.Status == HttpStatusCode.Gone));
    }

    [Fact]
    public async Task Last_admin_cannot_leave_or_be_demoted_but_can_after_promoting_someone()
    {
        var admin = await Env.NewUser(F);
        var editor = await Env.Join(F, admin, "editor");

        var (leave, leaveBody, _) = await admin.Post($"/api/despensas/{admin.DespensaId}/sair");
        Assert.Equal(HttpStatusCode.Conflict, leave);
        Assert.Equal("ULTIMO_ADMIN", leaveBody.GetProperty("codigo").GetString());
        var (demote, _, _) = await admin.Patch($"/api/despensas/{admin.DespensaId}/membros/{admin.UserId}", new { papel = "editor" });
        Assert.Equal(HttpStatusCode.Conflict, demote);

        await admin.Ok(admin.Patch($"/api/despensas/{admin.DespensaId}/membros/{editor.UserId}", new { papel = "admin" }));
        await admin.Ok(admin.Post($"/api/despensas/{admin.DespensaId}/sair"));
        var (after, _, _) = await admin.Get("/api/ingredientes");
        Assert.Equal(HttpStatusCode.NotFound, after);
        await editor.Ok(editor.Get("/api/ingredientes"));
    }

    [Fact]
    public async Task Removing_a_member_keeps_shared_data_and_deletes_personal_catalogs()
    {
        var admin = await Env.NewUser(F);
        var guest = await Env.Join(F, admin, "editor");
        var ing = await guest.NewIngredient("Aveia");
        await guest.Stock(ing, 500);
        await guest.Ok(guest.Post("/api/catalogos", new { nome = "Meus segredos", visibilidade = "pessoal" }));
        var recipe = await guest.NewRecipe("Mingau", [Api.Line("Aveia", ing, 50, "G")]);
        await guest.Ok(guest.Put($"/api/receitas/{recipe}/favorito"));

        await admin.Ok(admin.Delete($"/api/despensas/{admin.DespensaId}/membros/{guest.UserId}"));

        Assert.Equal(500, await admin.Total(ing));
        await admin.Ok(admin.Get($"/api/receitas/{recipe}"));
        var catalogs = await admin.Ok(admin.Get("/api/catalogos"));
        Assert.DoesNotContain(catalogs.EnumerateArray(), c => c.GetProperty("nome").GetString() == "Meus segredos");
        var (status, _, _) = await guest.Get("/api/ingredientes");
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Pantry_deletion_needs_the_exact_name_and_removes_everything()
    {
        var admin = await Env.NewUser(F, name: "Rita");
        var ing = await admin.NewIngredient("Sal");
        var pantry = await admin.Ok(admin.Get($"/api/despensas/{admin.DespensaId}"));
        var name = pantry.GetProperty("nome").GetString();

        var (wrong, _, _) = await admin.Delete($"/api/despensas/{admin.DespensaId}", new { confirmacaoNome = "outro nome" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong);
        await admin.Ok(admin.Delete($"/api/despensas/{admin.DespensaId}", new { confirmacaoNome = name }));
        var (gone, _, _) = await admin.Get($"/api/ingredientes/{ing}");
        Assert.Equal(HttpStatusCode.NotFound, gone);
    }

    [Fact]
    public async Task Security_headers_are_present()
    {
        var a = await Env.NewUser(F);
        var (_, _, response) = await a.Get("/api/auth/me");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }
}
