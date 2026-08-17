using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// Obyekt reyestri və FİZİKİ NÜSXƏLƏR. Əsas fərq: obyektdəki say ayrıca saxlanılmır,
/// nüsxə qeydlərindən hesablanır — eyni faktın iki mənbəyi bir gün mütləq ayrılır.
///
/// Ən vacib yoxlama nüsxələrin koordinatının FƏRQLİ olmasıdır: hamısı obyektin tək
/// nöqtəsində olsa xəritə "hansı skamya harada" sualına cavab verməz.
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
    public async Task Sites_Units_Positions_Status_And_Counts()
    {
        if (_factory is null) return; // baza yoxdur

        var admin = await LoginAsync();

        var category = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/categories",
            new { name = "Skameykalar", codePrefix = "SK" })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        var bench = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Park skamyası, 3 nəfərlik", categoryId = category.Id, sku = "SK-PR-3N" }))
            .Content.ReadFromJsonAsync<IdResponse>())!;
        var lounger = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Akasiya şezlonq", categoryId = category.Id, sku = "SZ-AK-KL" }))
            .Content.ReadFromJsonAsync<IdResponse>())!;

        // 1. Obyekt
        var createRes = await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/sites", new
        {
            name = "Dədə Qorqud parkı",
            kind = "Park",
            latitude = 40.3776,
            longitude = 49.8352,
            address = "Bakı, Nərimanov r.",
            contactName = "Park idarəsi",
            contactPhone = "+994 12 555 44 33",
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var site = (await createRes.Content.ReadFromJsonAsync<IdResponse>())!;

        // 2. Toplu nüsxə yaratma — 12 skamya bir əməliyyatla
        var bulkRes = await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/units/bulk", new
        {
            productId = bench.Id, siteId = site.Id, quantity = 12,
            installedOn = "2026-04-18", spreadMeters = 80,
        });
        Assert.True(bulkRes.StatusCode == HttpStatusCode.Created,
            $"Toplu yaratma: {(int)bulkRes.StatusCode} — {await bulkRes.Content.ReadAsStringAsync()}");
        var bulk = (await bulkRes.Content.ReadFromJsonAsync<BulkResponse>())!;
        Assert.Equal(12, bulk.Count);
        Assert.Equal("SK-PR-3N/001", bulk.Codes[0]);
        Assert.Equal("SK-PR-3N/012", bulk.Codes[11]);

        var units = (await admin.GetFromJsonAsync<List<UnitResponse>>("/api/admin/units"))!;
        Assert.Equal(12, units.Count);
        Assert.All(units, unit => Assert.Equal("Installed", unit.Status));
        Assert.All(units, unit => Assert.True(unit.HasOwnPosition, "Nüsxənin öz mövqeyi olmalıdır"));

        // ƏSAS: koordinatlar FƏRQLİDİR — 12 nüsxə xəritədə 12 nöqtədir, bir yığın deyil
        var distinct = units
            .Select(u => (u.Latitude, u.Longitude))
            .Distinct()
            .Count();
        Assert.Equal(12, distinct);

        // Səpələnmə obyektin ətrafındadır — 200 metrdən uzağa düşməməli
        foreach (var unit in units)
        {
            Assert.InRange(unit.Latitude!.Value, 40.3776 - 0.002, 40.3776 + 0.002);
            Assert.InRange(unit.Longitude!.Value, 49.8352 - 0.003, 49.8352 + 0.003);
        }

        // 3. Obyekt siyahısındaki say nüsxələrdən hesablanır
        var sites = (await admin.GetFromJsonAsync<List<SiteResponse>>("/api/admin/sites"))!;
        var loaded = Assert.Single(sites);
        Assert.Equal(12, loaded.TotalQuantity);
        var line = Assert.Single(loaded.Items);
        Assert.Equal(12, line.Quantity);
        Assert.StartsWith("Park skamyası", line.ProductName);

        // 4. Anbara nüsxə — obyekt verilmir, xəritədə görünmür
        Assert.Equal(HttpStatusCode.Created, (await SendJsonAsync(admin, HttpMethod.Post,
            "/api/admin/units/bulk",
            new { productId = lounger.Id, siteId = (Guid?)null, quantity = 3 })).StatusCode);
        var stock = (await admin.GetFromJsonAsync<List<UnitResponse>>(
            "/api/admin/units?status=InStock"))!;
        Assert.Equal(3, stock.Count);
        Assert.All(stock, unit => Assert.Null(unit.Latitude));
        Assert.All(stock, unit => Assert.Null(unit.SiteId));

        // 5. Model üzrə süzgəc — "bu model harada"
        Assert.Equal(12, (await admin.GetFromJsonAsync<List<UnitResponse>>(
            $"/api/admin/units?productId={bench.Id}"))!.Count);
        // Anbardaki şezlonq heç bir obyektdə deyil, ona görə obyekt süzgəci onu göstərmir
        Assert.Empty((await admin.GetFromJsonAsync<List<SiteResponse>>(
            $"/api/admin/sites?productId={lounger.Id}"))!);
        Assert.Single((await admin.GetFromJsonAsync<List<SiteResponse>>(
            $"/api/admin/sites?productId={bench.Id}"))!);

        // 6. Mövqeni dəqiqləşdirmə
        var first = units[0];
        Assert.Equal(HttpStatusCode.NoContent, (await SendJsonAsync(admin, HttpMethod.Put,
            $"/api/admin/units/{first.Id}/position",
            new { latitude = 40.3800, longitude = 49.8400 })).StatusCode);
        var moved = (await admin.GetFromJsonAsync<List<UnitResponse>>(
            $"/api/admin/units?search={first.Code}"))!.Single();
        Assert.Equal(40.38, moved.Latitude!.Value, precision: 4);

        // Səhv koordinat rədd edilir
        Assert.Equal(HttpStatusCode.BadRequest, (await SendJsonAsync(admin, HttpMethod.Put,
            $"/api/admin/units/{first.Id}/position",
            new { latitude = 120.0, longitude = 49.8 })).StatusCode);

        // 7. Status: təmirə göndərilən nüsxə obyekt sayından çıxır, qeydi qalır
        Assert.Equal(HttpStatusCode.NoContent, (await SendJsonAsync(admin, HttpMethod.Put,
            $"/api/admin/units/{first.Id}",
            new { siteId = site.Id, latitude = 40.38, longitude = 49.84,
                  installedOn = "2026-04-18", status = "Removed", note = "Vandalizm" })).StatusCode);
        var afterRemoval = Assert.Single(
            (await admin.GetFromJsonAsync<List<SiteResponse>>("/api/admin/sites"))!);
        Assert.Equal(11, afterRemoval.TotalQuantity);
        Assert.Equal(15, (await admin.GetFromJsonAsync<List<UnitResponse>>("/api/admin/units"))!.Count);

        // 8. Obyekt silinir → nüsxələr SİLİNMİR, anbara qayıdır
        var deleteRes = await SendJsonAsync(admin, HttpMethod.Delete, $"/api/admin/sites/{site.Id}");
        Assert.True(deleteRes.IsSuccessStatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<List<SiteResponse>>("/api/admin/sites"))!);
        var orphaned = (await admin.GetFromJsonAsync<List<UnitResponse>>("/api/admin/units"))!;
        Assert.Equal(15, orphaned.Count);
        Assert.All(orphaned, unit => Assert.Null(unit.SiteId));

        // 9. Girişsiz qonaq nə obyekti, nə nüsxəni görmür — daxili məlumatdır
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/admin/sites")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/admin/units")).StatusCode);
    }

    private sealed record AntiforgeryResponse(string Token);
    private sealed record IdResponse(Guid Id);
    private sealed record BulkResponse(int Count, List<string> Codes);
    private sealed record UnitResponse(Guid Id, string Code, Guid ProductId, string ProductName,
        Guid? SiteId, string? SiteName, double? Latitude, double? Longitude,
        bool HasOwnPosition, string Status, DateOnly? InstalledOn, string? Note,
        DateTime UpdatedAtUtc);
    private sealed record SiteItemResponse(Guid ProductId, string ProductName, int Quantity);
    private sealed record SiteResponse(Guid Id, string Name, string Kind, string? Address,
        double Latitude, double Longitude, string? ContactName, string? ContactPhone,
        string? Note, DateTime UpdatedAtUtc, List<SiteItemResponse> Items, int TotalQuantity);
}
