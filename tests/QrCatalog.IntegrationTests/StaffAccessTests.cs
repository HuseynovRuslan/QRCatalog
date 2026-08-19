using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// Eyni QR iki auditoriyaya iki ekran açır: qonaq məhsul səhifəsini (200, keşlənir),
/// işçi admin yönləndirməsini (302) görür. Ən kritik yoxlama keş qarışmasıdır:
/// qonaq üçün keşlənmiş 200 girişli işçiyə VERİLMƏMƏLİDİR — verilsə funksiya
/// gah işləyən, gah işləməyən görünər və səbəbi heç vaxt aydın olmaz.
///
/// Üstəgəl istifadəçi idarəetməsi: hər işçinin öz hesabı, rol sərhədləri,
/// deaktivasiya və müvəqqəti paroldan daimi parola keçid.
/// </summary>
public sealed class StaffAccessTests : IAsyncLifetime
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

    private async Task<HttpClient> LoginAsync(
        string email = AdminEmail, string password = AdminPassword, bool rememberMe = false)
    {
        // AllowAutoRedirect qapalıdır — 302-ni cavab kimi görmək lazımdır
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        var token = (await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery"))!.Token;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password, rememberMe }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode,
            $"Giriş: {(int)response.StatusCode} — {await response.Content.ReadAsStringAsync()}");
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

    private async Task<int> Scans30dAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<DashboardResponse>("/api/admin/stats/dashboard"))!.Scans30d;

    [Fact]
    public async Task StaffScan_RedirectsToAdmin_GuestKeepsCustomerPage()
    {
        if (_factory is null) return; // baza yoxdur

        var admin = await LoginAsync();
        var guest = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var category = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/categories",
            new { name = "Skameykalar", codePrefix = "SK" })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        var product = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Park skamyası", categoryId = category.Id })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        await SendJsonAsync(admin, HttpMethod.Post, $"/api/admin/products/{product.Id}/publish");
        var qr = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/qrcodes",
            new { targetType = "product", targetId = product.Id })).Content
            .ReadFromJsonAsync<QrCodeResponse>())!;

        // 1. Qonaq: müştəri səhifəsi (200) — bu sorğu səhifəni KEŞƏ salır
        var guestFirst = await guest.GetAsync($"/q/{qr.Token}");
        Assert.Equal(HttpStatusCode.OK, guestFirst.StatusCode);
        Assert.Contains("Park skamyası", await guestFirst.Content.ReadAsStringAsync());

        // 2. İşçi: keşlənmiş 200 YOX, admin yönləndirməsi gəlməlidir.
        //    Bu assert sınırsa keş girişli sorğuya cavab verib — ən təhlükəli reqressiya.
        var staff = await admin.GetAsync($"/q/{qr.Token}");
        Assert.Equal(HttpStatusCode.Redirect, staff.StatusCode);
        Assert.Equal($"/admin/mehsullar/{product.Id}", staff.Headers.Location?.ToString());

        // 3. Qonaq yenidən: keş qonaqlar üçün sağdır, işçi cavabı ora sızmayıb
        var guestSecond = await guest.GetAsync($"/q/{qr.Token}");
        Assert.Equal(HttpStatusCode.OK, guestSecond.StatusCode);
        Assert.Contains("Park skamyası", await guestSecond.Content.ReadAsStringAsync());

        // 4. Kateqoriya kodu: işçi skanı admin-ə gedir və STATİSTİKAYA DÜŞMÜR —
        //    əvvəl qonaq bir skan edir (sayğac 1 olsun), sonra işçi iki dəfə skan edir,
        //    sayğac 1-də qalmalıdır. İşçi skanları sayılsaydı statistika briqadanın
        //    marşrutunu göstərərdi.
        var catQr = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/qrcodes",
            new { targetType = "category", targetId = category.Id })).Content
            .ReadFromJsonAsync<QrCodeResponse>())!;

        Assert.Equal(HttpStatusCode.Redirect, (await guest.GetAsync($"/q/{catQr.Token}")).StatusCode);
        var baseline = 0;
        for (var attempt = 0; attempt < 20 && baseline < 1; attempt++)
        {
            baseline = await Scans30dAsync(admin);
            if (baseline < 1) await Task.Delay(500);
        }
        Assert.Equal(1, baseline);

        var staffCat = await admin.GetAsync($"/q/{catQr.Token}");
        Assert.Equal(HttpStatusCode.Redirect, staffCat.StatusCode);
        Assert.Equal("/admin/kateqoriyalar", staffCat.Headers.Location?.ToString());
        await admin.GetAsync($"/q/{catQr.Token}");

        await Task.Delay(2000); // asinxron yazıcıya vaxt — dərhal yoxlamaq yalançı yaşıl verər
        Assert.Equal(1, await Scans30dAsync(admin));
    }

    [Fact]
    public async Task Users_Create_RoleLimits_ChangePassword_Deactivate()
    {
        if (_factory is null) return; // baza yoxdur

        var admin = await LoginAsync();

        // 1. Viewer yaradılır — müvəqqəti parol cavabda BİR DƏFƏ gəlir
        var createRes = await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/users",
            new { email = "sahe@test.az", displayName = "Sahə işçisi", role = "Viewer" });
        Assert.True(createRes.StatusCode == HttpStatusCode.Created,
            $"Yaratma: {(int)createRes.StatusCode} — {await createRes.Content.ReadAsStringAsync()}");
        var created = (await createRes.Content.ReadFromJsonAsync<CreatedUserResponse>())!;
        Assert.False(string.IsNullOrWhiteSpace(created.TempPassword));

        // Təkrar eyni e-poçt → anlaşılan xəta
        var dupRes = await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/users",
            new { email = "sahe@test.az", displayName = "", role = "Viewer" });
        Assert.Equal(HttpStatusCode.BadRequest, dupRes.StatusCode);

        // 2. Yeni işçi girir: oxuya bilir, yaza BİLMİR (Viewer)
        var worker = await LoginAsync("sahe@test.az", created.TempPassword);
        Assert.True((await worker.GetAsync("/api/admin/products")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendJsonAsync(worker, HttpMethod.Post, "/api/admin/categories",
                new { name = "İcazəsiz" })).StatusCode);
        // İstifadəçi idarəetməsi yalnız Admin-ə açıqdır
        Assert.Equal(HttpStatusCode.Forbidden,
            (await worker.GetAsync("/api/admin/users")).StatusCode);

        // 3. İşçi müvəqqəti parolu daimi ilə əvəz edir
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendJsonAsync(worker, HttpMethod.Post, "/api/auth/change-password",
                new { currentPassword = created.TempPassword, newPassword = "YeniParol27" })).StatusCode);

        // Köhnə parol artıq işləmir, yenisi işləyir
        var stale = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var staleToken = (await stale.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery"))!.Token;
        var staleLogin = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "sahe@test.az", password = created.TempPassword }),
        };
        staleLogin.Headers.Add("X-XSRF-TOKEN", staleToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.SendAsync(staleLogin)).StatusCode);
        await LoginAsync("sahe@test.az", "YeniParol27");

        // 4. Rol dəyişimi: Viewer → Editor, indi yaza bilir
        var userId = created.Id;
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendJsonAsync(admin, HttpMethod.Put, $"/api/admin/users/{userId}/role",
                new { role = "Editor" })).StatusCode);
        var editor = await LoginAsync("sahe@test.az", "YeniParol27");
        Assert.Equal(HttpStatusCode.Created,
            (await SendJsonAsync(editor, HttpMethod.Post, "/api/admin/categories",
                new { name = "Editor kateqoriyası", codePrefix = "ED" })).StatusCode);

        // 5. Özünü qorumaq: admin özünü deaktiv edə və rolunu sala bilməz
        var adminId = (await admin.GetFromJsonAsync<List<UserRowResponse>>("/api/admin/users"))!
            .Single(u => u.Email == AdminEmail).Id;
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendJsonAsync(admin, HttpMethod.Post, $"/api/admin/users/{adminId}/deactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendJsonAsync(admin, HttpMethod.Put, $"/api/admin/users/{adminId}/role",
                new { role = "Viewer" })).StatusCode);

        // 6. Deaktivasiya: giriş dərhal bağlanır və mesaj yalan demir
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendJsonAsync(admin, HttpMethod.Post, $"/api/admin/users/{userId}/deactivate")).StatusCode);
        var blocked = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var blockedToken = (await blocked.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery"))!.Token;
        var blockedLogin = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "sahe@test.az", password = "YeniParol27" }),
        };
        blockedLogin.Headers.Add("X-XSRF-TOKEN", blockedToken);
        var blockedRes = await blocked.SendAsync(blockedLogin);
        Assert.Equal(HttpStatusCode.Forbidden, blockedRes.StatusCode);
        Assert.Contains("deaktiv", await blockedRes.Content.ReadAsStringAsync());

        // 7. Yenidən aktivləşdirilir — parol yerindədir
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendJsonAsync(admin, HttpMethod.Post, $"/api/admin/users/{userId}/activate")).StatusCode);
        await LoginAsync("sahe@test.az", "YeniParol27");

        // 8. Parol sıfırlama: müvəqqəti parol işləyir
        var resetRes = await SendJsonAsync(admin, HttpMethod.Post, $"/api/admin/users/{userId}/reset-password");
        var reset = (await resetRes.Content.ReadFromJsonAsync<TempPasswordResponse>())!;
        await LoginAsync("sahe@test.az", reset.TempPassword);
    }

    [Fact]
    public async Task RememberMe_ControlsCookiePersistence()
    {
        if (_factory is null) return; // baza yoxdur

        // Cookie başlığına baxmaq üçün konteynersiz müştəri — antiforgery əl ilə daşınır
        async Task<string> LoginCookieAsync(bool rememberMe)
        {
            var client = _factory!.CreateClient(
                new WebApplicationFactoryClientOptions { HandleCookies = false });
            var anti = await client.GetAsync("/api/auth/antiforgery");
            var token = (await anti.Content.ReadFromJsonAsync<AntiforgeryResponse>())!.Token;
            var antiCookie = anti.Headers.GetValues("Set-Cookie")
                .First(c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal))
                .Split(';')[0];

            var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new
                {
                    email = AdminEmail,
                    password = AdminPassword,
                    rememberMe,
                }),
            };
            login.Headers.Add("X-XSRF-TOKEN", token);
            login.Headers.Add("Cookie", antiCookie);
            var response = await client.SendAsync(login);
            Assert.True(response.IsSuccessStatusCode);
            return response.Headers.GetValues("Set-Cookie")
                .Single(c => c.StartsWith("qrcatalog.auth", StringComparison.Ordinal));
        }

        // "Məni xatırla" = qalıcı cookie (expires var, ~30 gün); onsuz = sessiya cookie-si
        Assert.Contains("expires=", await LoginCookieAsync(rememberMe: true),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", await LoginCookieAsync(rememberMe: false),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AntiforgeryResponse(string Token);
    private sealed record IdResponse(Guid Id);
    private sealed record QrCodeResponse(Guid Id, string Token, string HumanCode,
        string TargetType, Guid? TargetId, string Status, DateTime CreatedAtUtc, string? TargetName);
    private sealed record CreatedUserResponse(Guid Id, string Email, string TempPassword);
    private sealed record TempPasswordResponse(string TempPassword);
    private sealed record UserRowResponse(Guid Id, string Email, string DisplayName,
        string Role, bool Deactivated);
    private sealed record TopProductResponse(string Name, int Count);
    private sealed record DashboardResponse(int ProductsTotal, int ProductsPublished,
        int NewInquiries, int Scans30d, List<TopProductResponse> TopProducts, int UnscannedCodes);
}
