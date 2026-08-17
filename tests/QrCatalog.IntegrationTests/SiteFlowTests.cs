using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// Obyekt reyestri: obyekt yaranır, məhsul sətirləri əvəz olunur, xəritə üçün koordinat
/// yoxlanılır. Sətir əvəzləmə xüsusilə vacibdir — mövcud valideynə uşaq əlavə etmək
/// EF-də bir dəfə Added yerinə Modified sayılıb 500 verirdi (bax ValueGeneratedNever).
/// </summary>
public sealed class SiteFlowTests : IAsyncLifetime
{
    private const string AdminEmail = "admin@test.az";
    private const string AdminPassword = "Passw0rd!23";

    private TestDatabase? _database;
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.StartAsync();
        if (_database is null)
            return; // nə TEST_PG, nə Docker

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", _database.ConnectionString);
            builder.UseSetting("Bootstrap:AdminEmail", AdminEmail);
            builder.UseSetting("Bootstrap:AdminPassword", AdminPassword);
            builder.UseEnvironment("Development");
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        if (_database is not null) await _database.DisposeAsync();
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
    public async Task Site_Create_Items_Update_Delete()
    {
        if (_factory is null) return; // baza yoxdur

        var admin = await LoginAsync();

        var category = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/categories",
            new { name = "Skameykalar", codePrefix = "SK" })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        var bench = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Park skamyası, 3 nəfərlik", categoryId = category.Id })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        var lounger = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Akasiya şezlonq", categoryId = category.Id })).Content
            .ReadFromJsonAsync<IdResponse>())!;

        // 1. Obyekt yaranır
        var createRes = await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/sites", new
        {
            name = "Dədə Qorqud parkı",
            kind = "Park",
            latitude = 40.3776,
            longitude = 49.8352,
            address = "Bakı, Nərimanov r.",
            contactName = "Park idarəsi",
            contactPhone = "+994 12 555 44 33",
            note = "İllik baxış aprel ayındadır.",
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var site = (await createRes.Content.ReadFromJsonAsync<IdResponse>())!;

        // 2. Məhsul sətirləri — mövcud obyektə uşaq əlavə etmə yolu
        var itemsRes = await SendJsonAsync(admin, HttpMethod.Put,
            $"/api/admin/sites/{site.Id}/items",
            new
            {
                items = new object[]
                {
                    new { productId = bench.Id, quantity = 12, installedOn = "2026-04-18" },
                    new { productId = lounger.Id, quantity = 4, installedOn = (string?)null },
                },
            });
        Assert.True(itemsRes.StatusCode == HttpStatusCode.NoContent,
            $"Sətirlər: {(int)itemsRes.StatusCode} — {await itemsRes.Content.ReadAsStringAsync()}");

        var list = (await admin.GetFromJsonAsync<List<SiteResponse>>("/api/admin/sites"))!;
        var loaded = Assert.Single(list);
        Assert.Equal("Dədə Qorqud parkı", loaded.Name);
        Assert.Equal("Park", loaded.Kind);
        Assert.Equal(16, loaded.TotalQuantity);
        Assert.Equal(2, loaded.Items.Count);
        Assert.Contains(loaded.Items, i => i.ProductName.StartsWith("Park skamyası") && i.Quantity == 12);
        Assert.Equal(40.3776, loaded.Latitude, precision: 4);

        // 3. Təkrar əvəzləmə köhnə sətirləri saxlamır
        Assert.Equal(HttpStatusCode.NoContent, (await SendJsonAsync(admin, HttpMethod.Put,
            $"/api/admin/sites/{site.Id}/items",
            new { items = new object[] { new { productId = bench.Id, quantity = 20, installedOn = (string?)null } } }))
            .StatusCode);
        var afterReplace = Assert.Single(
            (await admin.GetFromJsonAsync<List<SiteResponse>>("/api/admin/sites"))!);
        Assert.Equal(20, afterReplace.TotalQuantity);
        Assert.Single(afterReplace.Items);

        // 4. Səhv koordinat qəbul olunmur — xəritədə "okeanın ortasında obyekt" olmasın
        var badRes = await SendJsonAsync(admin, HttpMethod.Put, $"/api/admin/sites/{site.Id}",
            new { name = "Dədə Qorqud parkı", kind = "Park", latitude = 120.0, longitude = 49.8 });
        Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);

        // 5. Tanınmayan məhsul rədd edilir
        var unknownRes = await SendJsonAsync(admin, HttpMethod.Put,
            $"/api/admin/sites/{site.Id}/items",
            new { items = new object[] { new { productId = Guid.NewGuid(), quantity = 1, installedOn = (string?)null } } });
        Assert.Equal(HttpStatusCode.BadRequest, unknownRes.StatusCode);

        // 6. Axtarış ünvana da baxır
        Assert.Single((await admin.GetFromJsonAsync<List<SiteResponse>>(
            "/api/admin/sites?search=nərimanov"))!);
        Assert.Empty((await admin.GetFromJsonAsync<List<SiteResponse>>(
            "/api/admin/sites?search=gəncə"))!);

        // 7. Obyekt silinir (məhsuldan fərqli olaraq — səhv ünvan tarixi məlumat deyil)
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendJsonAsync(admin, HttpMethod.Delete, $"/api/admin/sites/{site.Id}")).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<List<SiteResponse>>("/api/admin/sites"))!);

        // 8. Girişsiz qonaq obyektləri görmür — bu daxili məlumatdır
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.CreateClient().GetAsync("/api/admin/sites")).StatusCode);
    }

    private sealed record AntiforgeryResponse(string Token);
    private sealed record IdResponse(Guid Id);
    private sealed record SiteItemResponse(Guid Id, Guid ProductId, string ProductName,
        int Quantity, DateOnly? InstalledOn);
    private sealed record SiteResponse(Guid Id, string Name, string Kind, string? Address,
        double Latitude, double Longitude, string? ContactName, string? ContactPhone,
        string? Note, DateTime UpdatedAtUtc, List<SiteItemResponse> Items, int TotalQuantity);
}
