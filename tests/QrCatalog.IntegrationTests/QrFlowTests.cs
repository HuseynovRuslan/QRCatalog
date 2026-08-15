using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// M4 "bitmiş sayılır" axını: kod yaranır (SZ-0001 ardıcıllığı), şəkillər və PDF gəlir,
/// /q/{token} həll olunur, retire və retarget işləyir.
/// </summary>
public sealed class QrFlowTests : IAsyncLifetime
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
            builder.UseSetting("Qr:PublicBaseUrl", "https://qr.test.az");
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
    public async Task FullFlow_Create_Sequence_Images_Resolve_Retire_Retarget()
    {
        if (_factory is null) return; // Docker yoxdur

        var client = await LoginAsync();

        // Prefiksli kateqoriya
        var category = (await (await SendJsonAsync(client, HttpMethod.Post, "/api/admin/categories",
            new { name = "Şezlonqlar", codePrefix = "SZ" })).Content
            .ReadFromJsonAsync<CategoryResponse>())!;

        // 1. İki kod — ardıcıllıq SZ-0001, SZ-0002
        var qr1 = (await (await SendJsonAsync(client, HttpMethod.Post, "/api/admin/qrcodes",
            new { targetType = "category", targetId = category.Id })).Content
            .ReadFromJsonAsync<QrCodeResponse>())!;
        var qr2 = (await (await SendJsonAsync(client, HttpMethod.Post, "/api/admin/qrcodes",
            new { targetType = "category", targetId = category.Id })).Content
            .ReadFromJsonAsync<QrCodeResponse>())!;

        Assert.Equal("SZ-0001", qr1.HumanCode);
        Assert.Equal("SZ-0002", qr2.HumanCode);
        Assert.Equal(11, qr1.Token.Length);
        Assert.NotEqual(qr1.Token, qr2.Token);

        // 2. SVG və PNG
        var svg = await client.GetAsync($"/api/admin/qrcodes/{qr1.Id}/image.svg");
        Assert.Equal("image/svg+xml", svg.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<svg", await svg.Content.ReadAsStringAsync());

        var png = await client.GetAsync($"/api/admin/qrcodes/{qr1.Id}/image.png");
        Assert.Equal("image/png", png.Content.Headers.ContentType?.MediaType);

        // 3. A4 çap vərəqi — PDF imzası
        var sheet = await SendJsonAsync(client, HttpMethod.Post, "/api/admin/qrcodes/sheet",
            new { ids = new[] { qr1.Id, qr2.Id } });
        Assert.Equal("application/pdf", sheet.Content.Headers.ContentType?.MediaType);
        var pdfBytes = await sheet.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdfBytes[..4]));

        // 4. Public həll — girişsiz client
        var anon = _factory.CreateClient();
        var resolved = await anon.GetAsync($"/q/{qr1.Token}");
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var html = await resolved.Content.ReadAsStringAsync();
        Assert.Contains("Şezlonqlar", html);
        Assert.Contains("SZ-0001", html);

        // 5. Tanınmayan token → 404
        var missing = await anon.GetAsync("/q/yoxdur12345");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // 6. Retire → səhifə "istifadədə deyil" deyir
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendJsonAsync(client, HttpMethod.Post, $"/api/admin/qrcodes/{qr1.Id}/retire"))
            .StatusCode);
        var retired = await anon.GetAsync($"/q/{qr1.Token}");
        Assert.Contains("istifadədə deyil", await retired.Content.ReadAsStringAsync());

        // 7. Retarget → arxiv
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendJsonAsync(client, HttpMethod.Put, $"/api/admin/qrcodes/{qr2.Id}/retarget",
                new { targetType = "archive" })).StatusCode);
        var archived = await anon.GetAsync($"/q/{qr2.Token}");
        Assert.Contains("istehsal olunmur", await archived.Content.ReadAsStringAsync());

        // 8. Siyahı — hədəf adı ilə
        var list = (await client.GetFromJsonAsync<PagedResponse>("/api/admin/qrcodes"))!;
        Assert.Equal(2, list.Total);
        Assert.Contains(list.Items, i => i.TargetName == "Şezlonqlar");
    }

    [Fact]
    public async Task Create_WithoutPrefix_UsesFallback()
    {
        if (_factory is null) return;

        var client = await LoginAsync();
        var category = (await (await SendJsonAsync(client, HttpMethod.Post, "/api/admin/categories",
            new { name = "Prefikssiz" })).Content.ReadFromJsonAsync<CategoryResponse>())!;

        var qr = (await (await SendJsonAsync(client, HttpMethod.Post, "/api/admin/qrcodes",
            new { targetType = "category", targetId = category.Id })).Content
            .ReadFromJsonAsync<QrCodeResponse>())!;

        Assert.StartsWith("QR-", qr.HumanCode);
    }

    private sealed record AntiforgeryResponse(string Token);
    private sealed record CategoryResponse(Guid Id, Guid? ParentId, string Name, string Slug,
        string? Description, string? CodePrefix, int SortOrder);
    private sealed record QrCodeResponse(Guid Id, string Token, string HumanCode,
        string TargetType, Guid? TargetId, string Status, DateTime CreatedAtUtc, string? TargetName);
    private sealed record PagedResponse(List<QrCodeResponse> Items, int Total, int Page, int PageSize);
}
