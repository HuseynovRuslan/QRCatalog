using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// M8 bərkitmə: təhlükəsizlik başlıqları hər cavabda, audit jurnalı dəyişiklikləri
/// kim/nə/hansı-dəyərdən formatında tutur, jurnal yalnız Admin-ə açıqdır.
/// </summary>
public sealed class HardeningTests : IAsyncLifetime
{
    private const string AdminEmail = "admin@test.az";
    private const string AdminPassword = "Passw0rd!23";

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
            builder.UseEnvironment("Development");
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private async Task<HttpClient> LoginAsync()
    {
        var client = _factory!.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = (await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery"))!.Token;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = AdminEmail, password = AdminPassword }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        Assert.True((await client.SendAsync(request)).IsSuccessStatusCode);
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
    public async Task SecurityHeaders_PresentOnPublicPages()
    {
        if (_factory is null) return; // Docker yoxdur

        var anon = _factory.CreateClient();
        var response = await anon.GetAsync("/");

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.True(response.Headers.Contains("Permissions-Policy"));
    }

    [Fact]
    public async Task AuditLog_CapturesWhoWhatAndDiff_AdminOnly()
    {
        if (_factory is null) return;

        var admin = await LoginAsync();

        // Dəyişiklik yaradan əməliyyatlar
        var category = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/categories",
            new { name = "Audit kateqoriyası" })).Content.ReadFromJsonAsync<IdResponse>())!;
        var update = await SendJsonAsync(admin, HttpMethod.Put,
            $"/api/admin/categories/{category.Id}",
            new { name = "Audit kateqoriyası YENİ" });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        // Jurnal: added + modified sətirlər, istifadəçi və diff ilə
        var audit = (await admin.GetFromJsonAsync<PagedResponse>("/api/admin/audit"))!;
        Assert.True(audit.Total >= 2);

        var added = audit.Items.FirstOrDefault(a =>
            a.EntityType == "Category" && a.Action == "added");
        Assert.NotNull(added);
        Assert.Equal(AdminEmail, added.UserEmail);

        var modified = audit.Items.FirstOrDefault(a =>
            a.EntityType == "Category" && a.Action == "modified");
        Assert.NotNull(modified);
        Assert.Contains("Audit kateqoriyası YENİ", modified.Changes); // köhnə→yeni diff

        // Girişsiz 401
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync("/api/admin/audit")).StatusCode);
    }

    private sealed record AntiforgeryResponse(string Token);
    private sealed record IdResponse(Guid Id);
    private sealed record AuditResponse(DateTime OccurredAtUtc, string UserEmail,
        string EntityType, string EntityId, string Action, string? Changes);
    private sealed record PagedResponse(List<AuditResponse> Items, int Total, int Page, int PageSize);
}
