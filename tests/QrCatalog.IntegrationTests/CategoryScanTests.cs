using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// Kateqoriya QR-ı kataloq səhifəsinə yönləndirir, ona görə beacon işə düşmür və skan
/// server tərəfində qeyd olunur. Bu testlər həmin qolu qoruyur — xüsusən "hər skan sayılır"
/// şərtini: 302 cavabı output-cache-lənməməlidir, əks halda təkrar skanlar itər və panel
/// sahibə yanlış rəqəm göstərər.
/// </summary>
public sealed class CategoryScanTests : IAsyncLifetime
{
    private const string AdminEmail = "admin@test.az";
    private const string AdminPassword = "Passw0rd!23";

    private TestDatabase? _database;
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.StartAsync();
        if (_database is null)
            return; // nə TEST_PG, nə Docker — test erkən çıxır

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

    private static async Task<int> WaitForScansAsync(HttpClient admin, int expected)
    {
        // Yazı asinxrondur (ScanEventWriter partiya ilə yazır) — rəqəm görünənə qədər gözlə
        var scans = 0;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var dashboard = await admin.GetFromJsonAsync<DashboardResponse>("/api/admin/stats/dashboard");
            scans = dashboard!.Scans30d;
            if (scans >= expected) break;
            await Task.Delay(500);
        }
        return scans;
    }

    [Fact]
    public async Task CategoryQr_RedirectsToCatalog_AndCountsEveryScan()
    {
        if (_factory is null) return; // baza yoxdur

        var admin = await LoginAsync();
        var anon = _factory.CreateClient();

        var category = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/categories",
            new { name = "Şezlonqlar", codePrefix = "SZ" })).Content
            .ReadFromJsonAsync<IdResponse>())!;

        var code = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/qrcodes",
            new { targetType = "category", targetId = category.Id })).Content
            .ReadFromJsonAsync<QrCodeResponse>())!;

        // 1. Skan kanonik kataloq ünvanına aparır
        var first = await anon.GetAsync($"/q/{code.Token}");
        Assert.True(first.IsSuccessStatusCode);
        Assert.Equal("/katalog/sezlonqlar", first.RequestMessage?.RequestUri?.AbsolutePath);

        Assert.Equal(1, await WaitForScansAsync(admin, 1));

        // 2. Təkrar skanlar da sayılır — 302 keşlənsəydi handler işləməz və rəqəm 1-də donardı
        for (var i = 0; i < 2; i++)
        {
            var repeat = new HttpRequestMessage(HttpMethod.Get, $"/q/{code.Token}");
            repeat.Headers.Add("User-Agent", "Mozilla/5.0 (iPhone) Mobile/15E148");
            Assert.True((await anon.SendAsync(repeat)).IsSuccessStatusCode);
        }

        Assert.Equal(3, await WaitForScansAsync(admin, 3));

        // 3. Hesabatda kod öz adı ilə görünür və "heç vaxt skan olunmayıb" siyahısında deyil
        var reportRes = await admin.GetAsync("/api/admin/stats/scans?days=7");
        Assert.True(reportRes.IsSuccessStatusCode,
            $"Hesabat: {(int)reportRes.StatusCode} — {await reportRes.Content.ReadAsStringAsync()}");
        var report = (await reportRes.Content.ReadFromJsonAsync<ScanReportResponse>())!;

        var row = Assert.Single(report.ByCode);
        Assert.Equal(code.HumanCode, row.HumanCode);
        Assert.Equal(3, row.Count);
        Assert.Empty(report.Unscanned);
    }

    private sealed record AntiforgeryResponse(string Token);
    private sealed record IdResponse(Guid Id);
    private sealed record QrCodeResponse(Guid Id, string Token, string HumanCode,
        string TargetType, Guid? TargetId, string Status, DateTime CreatedAtUtc, string? TargetName);
    private sealed record TopProductResponse(string Name, int Count);
    private sealed record DashboardResponse(int ProductsTotal, int ProductsPublished,
        int NewInquiries, int Scans30d, List<TopProductResponse> TopProducts, int UnscannedCodes);
    private sealed record DayCountResponse(DateTime Date, int Count);
    private sealed record CodeCountResponse(string HumanCode, string? ProductName, int Count);
    private sealed record UnscannedResponse(string HumanCode, DateTime CreatedAtUtc);
    private sealed record ScanReportResponse(List<DayCountResponse> ByDay,
        List<CodeCountResponse> ByCode, List<UnscannedResponse> Unscanned);
}
