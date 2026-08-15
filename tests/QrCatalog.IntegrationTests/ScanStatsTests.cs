using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// M7 "bitmiş sayılır" axını: beacon skanı qeyd edir (asinxron), dashboard və hesabat
/// real rəqəmləri göstərir, skan olunmayan kodlar siyahıya düşür.
/// </summary>
public sealed class ScanStatsTests : IAsyncLifetime
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
    public async Task Beacon_Records_Dashboard_Report_Unscanned()
    {
        if (_factory is null) return; // Docker yoxdur

        var admin = await LoginAsync();
        var anon = _factory.CreateClient();

        // Məhsul + iki QR: biri skan olunacaq, biri yox
        var category = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/categories",
            new { name = "Şezlonqlar", codePrefix = "SZ" })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        var product = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Bahama şezlonq", categoryId = category.Id })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        await SendJsonAsync(admin, HttpMethod.Post, $"/api/admin/products/{product.Id}/publish");

        var scanned = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/qrcodes",
            new { targetType = "product", targetId = product.Id })).Content
            .ReadFromJsonAsync<QrCodeResponse>())!;
        var untouched = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/qrcodes",
            new { targetType = "product", targetId = product.Id })).Content
            .ReadFromJsonAsync<QrCodeResponse>())!;

        // Q səhifəsi beacon skriptini daşıyır
        var page = await anon.GetStringAsync($"/q/{scanned.Token}");
        Assert.Contains("/api/public/scans", page);

        // 3 skan beacon-u (mobil UA ilə)
        for (var i = 0; i < 3; i++)
        {
            var beacon = new HttpRequestMessage(HttpMethod.Post, "/api/public/scans")
            {
                Content = JsonContent.Create(new { token = scanned.Token }),
            };
            beacon.Headers.Add("User-Agent", "Mozilla/5.0 (iPhone) Mobile/15E148");
            beacon.Headers.Add("Accept-Language", "az,ru;q=0.8");
            Assert.Equal(HttpStatusCode.NoContent, (await anon.SendAsync(beacon)).StatusCode);
        }

        // Tanınmayan token — eyni cavab, heç nə sızmır
        Assert.Equal(HttpStatusCode.NoContent,
            (await anon.PostAsJsonAsync("/api/public/scans", new { token = "yalanToken12" })).StatusCode);

        // Yazı asinxrondur — dashboard rəqəmi görünənə qədər gözlə (maks ~10s)
        DashboardResponse? dashboard = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            dashboard = await admin.GetFromJsonAsync<DashboardResponse>("/api/admin/stats/dashboard");
            if (dashboard!.Scans30d >= 3) break;
            await Task.Delay(500);
        }

        Assert.NotNull(dashboard);
        Assert.Equal(3, dashboard.Scans30d);
        var top = Assert.Single(dashboard.TopProducts);
        Assert.Equal("Bahama şezlonq", top.Name);
        Assert.Equal(3, top.Count);
        Assert.Equal(1, dashboard.UnscannedCodes); // untouched hələ skan olunmayıb

        // Hesabat: günlük seriya + kod üzrə + skan olunmayanlar
        var report = (await admin.GetFromJsonAsync<ScanReportResponse>(
            "/api/admin/stats/scans?days=7"))!;
        Assert.Equal(3, report.ByDay.Sum(d => d.Count));
        var codeRow = Assert.Single(report.ByCode);
        Assert.Equal(scanned.HumanCode, codeRow.HumanCode);
        var unscannedRow = Assert.Single(report.Unscanned);
        Assert.Equal(untouched.HumanCode, unscannedRow.HumanCode);

        // Stats girişsiz bağlıdır
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync("/api/admin/stats/dashboard")).StatusCode);
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
