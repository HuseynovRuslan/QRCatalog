using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QrCatalog.Infrastructure.Identity;
using Testcontainers.PostgreSql;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// M2 "bitmiş sayılır" + fail-closed filtrin sübutu: müəssisəsiz istifadəçi HEÇ NƏ görmür
/// və yaza bilmir. Real Postgres (Testcontainers) — lokalda Docker yoxdursa sakit keçilir.
/// </summary>
public sealed class CategoryFlowTests : IAsyncLifetime
{
    private const string AdminEmail = "admin@test.az";
    private const string AdminPassword = "Passw0rd!23";
    private const string NoCompanyEmail = "kimsesiz@test.az";

    private static bool DockerAvailable =>
        Environment.GetEnvironmentVariable("CI") == "true" ||
        Environment.GetEnvironmentVariable("DOCKER_AVAILABLE") == "true";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable)
            return;

        _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
            builder.UseSetting("Bootstrap:AdminEmail", AdminEmail);
            builder.UseSetting("Bootstrap:AdminPassword", AdminPassword);
            builder.UseSetting("Bootstrap:CompanyName", "Test müəssisəsi");
            builder.UseSetting("Bootstrap:CompanySlug", "test");
            builder.UseEnvironment("Development");
        });

        // Fail-closed sübutu üçün: müəssisəyə bağlı OLMAYAN Admin-rollu istifadəçi
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var orphan = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = NoCompanyEmail,
            Email = NoCompanyEmail,
            EmailConfirmed = true,
            CompanyId = null,
            DisplayName = "Şirkətsiz",
        };
        await userManager.CreateAsync(orphan, AdminPassword);
        await userManager.AddToRoleAsync(orphan, AppRoles.Admin);
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = _factory!.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = (await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery"))!.Token;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, "Test girişi alınmadı.");
        return client;
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var token = (await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery"))!.Token;
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Categories_WithoutLogin_Returns401()
    {
        if (_factory is null) return; // Docker yoxdur

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/categories");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FullFlow_CreateTree_Reorder_DeleteGuard()
    {
        if (_factory is null) return;

        var client = await LoginAsync(AdminEmail, AdminPassword);

        // Kök + iki uşaq
        var rootRes = await SendJsonAsync(client, HttpMethod.Post, "/api/admin/categories",
            new { name = "Şezlonqlar", codePrefix = "SZ" });
        Assert.Equal(HttpStatusCode.Created, rootRes.StatusCode);
        var root = (await rootRes.Content.ReadFromJsonAsync<CategoryResponse>())!;
        Assert.Equal("sezlonqlar", root.Slug);

        var child1 = (await (await SendJsonAsync(client, HttpMethod.Post, "/api/admin/categories",
            new { name = "Plastik", parentId = root.Id })).Content
            .ReadFromJsonAsync<CategoryResponse>())!;
        var child2 = (await (await SendJsonAsync(client, HttpMethod.Post, "/api/admin/categories",
            new { name = "Taxta", parentId = root.Id })).Content
            .ReadFromJsonAsync<CategoryResponse>())!;

        // Siyahı
        var list = (await client.GetFromJsonAsync<List<CategoryResponse>>("/api/admin/categories"))!;
        Assert.Equal(3, list.Count);
        Assert.Equal(2, list.Count(c => c.ParentId == root.Id));

        // Sıralama: taxta əvvələ
        var reorder = await SendJsonAsync(client, HttpMethod.Put, "/api/admin/categories/reorder",
            new { orderedIds = new[] { child2.Id, child1.Id } });
        Assert.Equal(HttpStatusCode.NoContent, reorder.StatusCode);

        list = (await client.GetFromJsonAsync<List<CategoryResponse>>("/api/admin/categories"))!;
        var children = list.Where(c => c.ParentId == root.Id).OrderBy(c => c.SortOrder).ToList();
        Assert.Equal("Taxta", children[0].Name);

        // Dövr qoruması: kökü öz uşağının altına köçürmək olmaz
        var cycle = await SendJsonAsync(client, HttpMethod.Put,
            $"/api/admin/categories/{root.Id}/move", new { parentId = child1.Id });
        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);

        // Silmə qoruması: uşağı olan kateqoriya silinmir
        var deleteRoot = await SendJsonAsync(client, HttpMethod.Delete,
            $"/api/admin/categories/{root.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteRoot.StatusCode);

        // Uşaq silinir
        var deleteChild = await SendJsonAsync(client, HttpMethod.Delete,
            $"/api/admin/categories/{child1.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteChild.StatusCode);

        // Eyni addan ikinci slug: sezlonqlar-2
        var dup = (await (await SendJsonAsync(client, HttpMethod.Post, "/api/admin/categories",
            new { name = "Şezlonqlar" })).Content.ReadFromJsonAsync<CategoryResponse>())!;
        Assert.Equal("sezlonqlar-2", dup.Slug);
    }

    [Fact]
    public async Task FailClosed_UserWithoutCompany_SeesNothing_CannotWrite()
    {
        if (_factory is null) return;

        // Əvvəl admin real kateqoriya yaradır
        var admin = await LoginAsync(AdminEmail, AdminPassword);
        await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/categories",
            new { name = "Görünməməli kateqoriya" });

        // Şirkətsiz istifadəçi (Admin roluna baxmayaraq!) heç nə görmür
        var orphan = await LoginAsync(NoCompanyEmail, AdminPassword);
        var list = (await orphan.GetFromJsonAsync<List<CategoryResponse>>("/api/admin/categories"))!;
        Assert.Empty(list); // fail-closed: filtr default şirkət vermir, boş qaytarır

        // Yaza da bilmir
        var write = await SendJsonAsync(orphan, HttpMethod.Post, "/api/admin/categories",
            new { name = "İcazəsiz" });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    private sealed record AntiforgeryResponse(string Token);

    private sealed record CategoryResponse(
        Guid Id, Guid? ParentId, string Name, string Slug,
        string? Description, string? CodePrefix, int SortOrder);
}
