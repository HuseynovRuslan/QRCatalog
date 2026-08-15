using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// M5 "bitmiş sayılır" axını: kataloq, kateqoriya, məhsul səhifəsi, axtarış, arxiv halı,
/// əlaqə düymələri və keş invalidasiyası.
/// </summary>
public sealed class PublicSiteTests : IAsyncLifetime
{
    private const string AdminEmail = "admin@test.az";
    private const string AdminPassword = "Passw0rd!23";

    private TestDatabase? _database;
    private WebApplicationFactory<Program>? _factory;
    private string? _storageRoot;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.StartAsync();
        if (_database is null)
            return; // nə TEST_PG, nə Docker — test erkən çıxır

        _storageRoot = Path.Combine(Path.GetTempPath(), "qrcatalog-tests", Guid.NewGuid().ToString("N"));

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", _database.ConnectionString);
            builder.UseSetting("Bootstrap:AdminEmail", AdminEmail);
            builder.UseSetting("Bootstrap:AdminPassword", AdminPassword);
            builder.UseSetting("Storage:Local:Root", _storageRoot);
            builder.UseEnvironment("Development");
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        if (_database is not null) await _database.DisposeAsync();
        if (_storageRoot is not null && Directory.Exists(_storageRoot))
            Directory.Delete(_storageRoot, recursive: true);
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
    public async Task PublicSite_Catalog_Search_Product_Archive_Contact_CacheEviction()
    {
        if (_factory is null) return; // Docker yoxdur

        var admin = await LoginAsync();
        var anon = _factory.CreateClient();

        // Məzmun: kateqoriya + 2 məhsul (biri dərc olunur)
        var category = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/categories",
            new { name = "Şezlonqlar", codePrefix = "SZ" })).Content
            .ReadFromJsonAsync<IdResponse>())!;

        var bahama = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Bahama şezlonq", description = "UV-davamlı", categoryId = category.Id }))
            .Content.ReadFromJsonAsync<IdResponse>())!;
        var gizli = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Gizli qaralama", categoryId = category.Id }))
            .Content.ReadFromJsonAsync<IdResponse>())!;

        var publishRes = await SendJsonAsync(admin, HttpMethod.Post,
            $"/api/admin/products/{bahama.Id}/publish");
        Assert.True(publishRes.StatusCode == HttpStatusCode.NoContent,
            $"Publish: {(int)publishRes.StatusCode} — {await publishRes.Content.ReadAsStringAsync()}");
        var specsRes = await SendJsonAsync(admin, HttpMethod.Put, $"/api/admin/products/{bahama.Id}/specs",
            new { specs = new[] { new { label = "Ölçü", value = "190×60 sm" } } });
        Assert.True(specsRes.StatusCode == HttpStatusCode.NoContent,
            $"Specs: {(int)specsRes.StatusCode} — {await specsRes.Content.ReadAsStringAsync()}");

        // Əlaqə məlumatı
        var settingsRes = await SendJsonAsync(admin, HttpMethod.Put, "/api/admin/settings",
            new { name = "Test müəssisəsi", phone = "+994501234567", whatsappNumber = "994501234567" });
        Assert.Equal(HttpStatusCode.NoContent, settingsRes.StatusCode);

        // 1. Kataloq: dərc olunan görünür, qaralama görünmür
        var catalog = await anon.GetStringAsync("/katalog");
        Assert.Contains("Bahama şezlonq", catalog);
        Assert.DoesNotContain("Gizli qaralama", catalog);
        Assert.Contains("Şezlonqlar", catalog);

        // 2. Kateqoriya səhifəsi + breadcrumb
        var categoryPage = await anon.GetStringAsync("/katalog/sezlonqlar");
        Assert.Contains("Bahama şezlonq", categoryPage);
        Assert.Contains("Kataloq", categoryPage);

        // 3. Məhsul səhifəsi: spec + əlaqə düymələri
        var productPage = await anon.GetStringAsync("/p/bahama-sezlonq");
        Assert.Contains("190×60 sm", productPage);
        Assert.Contains("wa.me/994501234567", productPage);
        // Razor "+" işarəsini &#x2B; kimi kodlayır (brauzer onu düzgün açır) — testin
        // niyyəti linkin özüdür, ona görə səhifə dekod olunub yoxlanılır
        Assert.Contains("tel:+994501234567", System.Net.WebUtility.HtmlDecode(productPage));

        // 4. Qaralama məhsul qonaq üçün 404
        Assert.Equal(HttpStatusCode.NotFound,
            (await anon.GetAsync("/p/gizli-qaralama")).StatusCode);

        // 5. Axtarış: tapır və tapmır
        var found = await anon.GetStringAsync("/katalog?axtar=bahama");
        Assert.Contains("Bahama şezlonq", found);
        var notFound = await anon.GetStringAsync("/katalog?axtar=stolustu-tennis");
        Assert.DoesNotContain("Bahama şezlonq", notFound);

        // 6. Keş invalidasiyası: səhifə keşləndi → ad dəyişdi → dərhal yeni ad görünür
        _ = await anon.GetStringAsync("/p/bahama-sezlonq"); // keşə düşdü
        await SendJsonAsync(admin, HttpMethod.Put, $"/api/admin/products/{bahama.Id}",
            new { name = "Bahama şezlonq PRO", categoryId = category.Id });
        var refreshed = await anon.GetStringAsync("/p/bahama-sezlonq");
        Assert.Contains("Bahama şezlonq PRO", refreshed);

        // 7. Arxiv: səhifə izahlı qalır, oxşarlar təklif olunur
        var oxsar = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Oxşar şezlonq", categoryId = category.Id }))
            .Content.ReadFromJsonAsync<IdResponse>())!;
        await SendJsonAsync(admin, HttpMethod.Post, $"/api/admin/products/{oxsar.Id}/publish");
        await SendJsonAsync(admin, HttpMethod.Post, $"/api/admin/products/{bahama.Id}/archive");

        var archived = await anon.GetStringAsync("/p/bahama-sezlonq");
        Assert.Contains("istehsal olunmur", archived);
        Assert.Contains("Oxşar şezlonq", archived);

        // 8. Public səhifədə daxili məlumat sızmır
        Assert.DoesNotContain("Draft", archived);
        Assert.DoesNotContain("companyId", catalog, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AntiforgeryResponse(string Token);
    private sealed record IdResponse(Guid Id);
}
