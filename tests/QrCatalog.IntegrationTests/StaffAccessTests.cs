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
        Assert.Equal($"/i/{qr.Token}", staff.Headers.Location?.ToString());

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
        Assert.Equal($"/i/{catQr.Token}", staffCat.Headers.Location?.ToString());
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
    public async Task UnitQr_AnswersWhichBenchAndWhere()
    {
        if (_factory is null) return; // baza yoxdur

        var admin = await LoginAsync();

        var category = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/categories",
            new { name = "Skamyalar", codePrefix = "SK" })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        var product = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Park skamyası", categoryId = category.Id, sku = "SK-PR-3N" })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        await SendJsonAsync(admin, HttpMethod.Post, $"/api/admin/products/{product.Id}/publish");

        var site = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/sites",
            new
            {
                name = "Gəncə Xan bağı", kind = "park",
                latitude = 40.6828, longitude = 46.3606,
                address = "Gəncə, Nizami r.",
                contactName = "Gəncə İH", contactPhone = "+994222567712",
            })).Content.ReadFromJsonAsync<IdResponse>())!;

        await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/units/bulk",
            new { productId = product.Id, siteId = site.Id, quantity = 3, installedOn = "2025-05-04" });

        // Toplu kod: hər nüsxəyə öz QR-ı
        var bulkRes = await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/qrcodes/units/bulk",
            new { productId = product.Id });
        Assert.True(bulkRes.IsSuccessStatusCode,
            $"Toplu kod: {(int)bulkRes.StatusCode} - {await bulkRes.Content.ReadAsStringAsync()}");
        var bulk = (await bulkRes.Content.ReadFromJsonAsync<BulkCodesResponse>())!;
        Assert.Equal(3, bulk.Created);

        // Təkrar çağırış YENİ kod yaratmamalıdır — əməliyyat təkrarlana bilən olmalıdır
        var again = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/qrcodes/units/bulk",
            new { productId = product.Id })).Content.ReadFromJsonAsync<BulkCodesResponse>())!;
        Assert.Equal(0, again.Created);

        var codes = await admin.GetFromJsonAsync<PagedQrResponse>("/api/admin/qrcodes?pageSize=50");
        var unitCodes = codes!.Items.Where(c => c.TargetType == "Unit").ToList();
        Assert.Equal(3, unitCodes.Count);
        // Etiketdə çap olunan ad NÜSXƏ kodudur — sahədə işə yarayan identifikator budur
        Assert.All(unitCodes, c => Assert.StartsWith("SK-PR-3N/", c.TargetName));

        var first = unitCodes.OrderBy(c => c.TargetName).First();

        // 1. İŞÇİ: hansı nüsxə, harada, nə vaxtdan — ekranın bütün cavabı
        var staffPage = await admin.GetAsync($"/i/{first.Token}");
        Assert.Equal(HttpStatusCode.OK, staffPage.StatusCode);
        var html = await staffPage.Content.ReadAsStringAsync();

        Assert.Contains(first.TargetName!, html);      // nüsxə kodu
        Assert.Contains("Gəncə Xan bağı", html);       // obyekt
        Assert.Contains("Gəncə, Nizami r.", html);     // ünvan
        Assert.Contains("40.6828", html);              // koordinat
        Assert.Contains("Yol göstər", html);           // naviqasiya
        // Razor «+» işarəsini &#x2B; kimi kodlayır (köhnə UTF-7 hücumlarına qarşı),
        // ona görə rəqəmlərə baxılır — «tel:» linki brauzerdə düzgün açılır.
        Assert.Contains("staff-contact", html);
        Assert.Contains("994222567712", html);
        Assert.Contains("04.05.2025", html);           // quraşdırma tarixi
        // Digər iki nüsxə «bu obyektdə eyni modeldən» bölməsində
        Assert.Contains("Bu obyektdə eyni modeldən", html);

        // 2. QONAQ: eyni etiket müştəri səhifəsini açır, daxili məlumat SIZMIR
        var guest = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var guestPage = await guest.GetAsync($"/q/{first.Token}");
        Assert.Equal(HttpStatusCode.OK, guestPage.StatusCode);
        var guestHtml = await guestPage.Content.ReadAsStringAsync();
        Assert.Contains("Park skamyası", guestHtml);
        Assert.DoesNotContain("Gəncə Xan bağı", guestHtml);   // obyekt adı daxilidir
        Assert.DoesNotContain(first.TargetName!, guestHtml);  // nüsxə kodu da

        // 3. İşçi skanı /q/ üzərindən də nüsxə ekranına gedir
        var staffScan = await admin.GetAsync($"/q/{first.Token}");
        Assert.Equal(HttpStatusCode.Redirect, staffScan.StatusCode);
        Assert.Equal($"/i/{first.Token}", staffScan.Headers.Location?.ToString());
    }

    [Fact]
    public async Task UnitCodes_StayUnique_AcrossProductsWithSamePrefix()
    {
        if (_factory is null) return; // baza yoxdur

        var admin = await LoginAsync();
        var category = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/categories",
            new { name = "Stullar", codePrefix = "ST" })).Content
            .ReadFromJsonAsync<IdResponse>())!;

        // İKİ məhsul, hər ikisi SKU-suz → prefiks eyni yerdən (kateqoriyadan) gəlir.
        // Əvvəl nömrə məhsulun nüsxə SAYINDAN alınırdı: ikinci məhsulun birinci
        // nüsxəsi «ST/001» olurdu, halbuki o kod artıq mövcud idi → 500.
        var a = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Qatlanan stul", categoryId = category.Id })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        var b = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Bar stulu", categoryId = category.Id })).Content
            .ReadFromJsonAsync<IdResponse>())!;

        Assert.Equal(HttpStatusCode.Created,
            (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/units/bulk",
                new { productId = a.Id, quantity = 3 })).StatusCode);

        var second = await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/units/bulk",
            new { productId = b.Id, quantity = 2 });
        Assert.True(second.IsSuccessStatusCode,
            $"İkinci məhsulun nüsxələri yaradılmadı: {(int)second.StatusCode}");

        var units = await admin.GetFromJsonAsync<List<UnitRowResponse>>("/api/admin/units");
        var stCodes = units!.Where(u => u.Code.StartsWith("ST/")).Select(u => u.Code).ToList();
        Assert.Equal(5, stCodes.Count);
        Assert.Equal(5, stCodes.Distinct().Count()); // toqquşma YOXDUR
    }

    [Fact]
    public async Task CustomShortCode_Works_WarnsAndStaysUnique()
    {
        if (_factory is null) return; // baza yoxdur

        var admin = await LoginAsync();
        var created = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/users",
            new { email = "usta@test.az", displayName = "Usta", role = "Viewer" })).Content
            .ReadFromJsonAsync<CreatedUserResponse>())!;

        // 1. Rəhbərlik yadda qalan kod istəyir — «1655» qəbul olunmalı, AMMA
        //    zəiflik barədə xəbərdarlıq gəlməlidir (səssizcə razılaşmaq yanlışdır)
        var setRes = await SendJsonAsync(admin, HttpMethod.Post,
            $"/api/admin/users/{created.Id}/reset-code", new { code = "1655" });
        Assert.Equal(HttpStatusCode.OK, setRes.StatusCode);
        var set = (await setRes.Content.ReadFromJsonAsync<AccessCodeResponse>())!;
        Assert.Equal("1655", set.AccessCode);
        Assert.NotNull(set.Warning);
        Assert.Contains("zəif", set.Warning!);

        // 2. Qısa kodla giriş işləyir
        var worker = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK,
            (await SendJsonAsync(worker, HttpMethod.Post, "/api/auth/login-code",
                new { code = "1655" })).StatusCode);

        // 3. Köhnə sistem kodu ölüb
        var stale = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendJsonAsync(stale, HttpMethod.Post, "/api/auth/login-code",
                new { code = created.AccessCode })).StatusCode);

        // 4. Eyni kod ikinci nəfərə VERİLMƏMƏLİDİR — yoxsa giriş qeyri-müəyyən olar
        var second = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/users",
            new { email = "usta2@test.az", displayName = "Usta 2", role = "Viewer" })).Content
            .ReadFromJsonAsync<CreatedUserResponse>())!;
        var clash = await SendJsonAsync(admin, HttpMethod.Post,
            $"/api/admin/users/{second.Id}/reset-code", new { code = "16 55" });
        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
        Assert.Contains("başqa istifadəçidədir", await clash.Content.ReadAsStringAsync());

        // 5. Həddən qısa kod rədd olunur — 3 simvol praktiki olaraq açıq qapıdır
        var tooShort = await SendJsonAsync(admin, HttpMethod.Post,
            $"/api/admin/users/{second.Id}/reset-code", new { code = "12" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        // 6. E-poçt düzəlişi: səhv yazılmış ünvan hesabı silmədən düzəlir
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendJsonAsync(admin, HttpMethod.Put, $"/api/admin/users/{second.Id}",
                new { email = "duzgun@test.az", displayName = "Düzgün Ad" })).StatusCode);
        var rows = (await admin.GetFromJsonAsync<List<UserRowResponse>>("/api/admin/users"))!;
        var fixedRow = rows.Single(u => u.Id == second.Id);
        Assert.Equal("duzgun@test.az", fixedRow.Email);
        Assert.Equal("Düzgün Ad", fixedRow.DisplayName);
        // Giriş e-poçtu da dəyişməlidir, yoxsa yeni ünvanla girmək mümkün olmaz
        Assert.True((await SendJsonAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true }),
            HttpMethod.Post, "/api/auth/login",
            new { email = "duzgun@test.az", password = second.TempPassword })).IsSuccessStatusCode);
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

    [Fact]
    public async Task StaffInfoPage_AnswersManagementQuestions()
    {
        if (_factory is null) return; // baza yoxdur

        var admin = await LoginAsync();

        var category = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/categories",
            new { name = "Skamyalar", codePrefix = "SK" })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        var product = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Bağ skamyası «İpək»", categoryId = category.Id, sku = "SK-IP-AK" })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        await SendJsonAsync(admin, HttpMethod.Post, $"/api/admin/products/{product.Id}/publish");

        // İki obyekt, fərqli saylarla — «hara qoymuşuq, neçə dənə» sualının cavabı
        var qebele = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/sites",
            new { name = "Qəbələ dağ oteli", kind = "hotel", latitude = 40.98, longitude = 47.84 })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        var park = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/sites",
            new { name = "Dənizkənarı park", kind = "park", latitude = 40.37, longitude = 49.84 })).Content
            .ReadFromJsonAsync<IdResponse>())!;

        await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/units/bulk",
            new { productId = product.Id, siteId = qebele.Id, quantity = 3 });
        await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/units/bulk",
            new { productId = product.Id, siteId = park.Id, quantity = 7 });
        // Anbardaki nüsxələr obyekt siyahısına DÜŞMƏMƏLİDİR, amma ümumi saya düşür
        await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/units/bulk",
            new { productId = product.Id, siteId = (Guid?)null, quantity = 2 });

        var qr = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/qrcodes",
            new { targetType = "product", targetId = product.Id })).Content
            .ReadFromJsonAsync<QrCodeResponse>())!;

        var page = await admin.GetAsync($"/i/{qr.Token}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();

        Assert.Contains("Bağ skamyası «İpək»", html);
        Assert.Contains("Qəbələ dağ oteli", html);
        Assert.Contains("Dənizkənarı park", html);
        Assert.Contains("SK-IP-AK", html);
        // 3 + 7 quraşdırılıb, 2 anbarda → işlək cəmi 12
        Assert.Contains(">12<", html);

        // Qonaq bu ekranı GÖRMƏMƏLİDİR — daxili məlumatdır
        var guest = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var guestTry = await guest.GetAsync($"/i/{qr.Token}");
        Assert.Equal(HttpStatusCode.Redirect, guestTry.StatusCode);
        Assert.Contains("/admin/login", guestTry.Headers.Location?.ToString());
    }

    [Fact]
    public async Task AccessCode_LogsInWithSingleField()
    {
        if (_factory is null) return; // baza yoxdur

        var admin = await LoginAsync();

        var created = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/users",
            new { email = "brigada@test.az", displayName = "Briqadir", role = "Viewer" })).Content
            .ReadFromJsonAsync<CreatedUserResponse>())!;

        Assert.StartsWith("WM-", created.AccessCode);
        Assert.Equal(12, created.AccessCode.Length); // WM-XXXX-XXXX

        // 1. Kod ilə giriş — e-poçt tələb olunmur
        var worker = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        Assert.Equal(HttpStatusCode.OK,
            (await SendJsonAsync(worker, HttpMethod.Post, "/api/auth/login-code",
                new { code = created.AccessCode, rememberMe = true })).StatusCode);
        Assert.True((await worker.GetAsync("/api/admin/products")).IsSuccessStatusCode);

        // 2. Defis, boşluq və registr fərqi bağışlanır — işçi kodu əl ilə yazır
        var sloppy = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var messy = created.AccessCode.Replace("-", " ").ToLowerInvariant();
        Assert.Equal(HttpStatusCode.OK,
            (await SendJsonAsync(sloppy, HttpMethod.Post, "/api/auth/login-code",
                new { code = messy })).StatusCode);

        // 3. Səhv kod → 401, boş kod → 401 (boş sətir kiməsə uyğun gəlməməlidir)
        var stranger = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendJsonAsync(stranger, HttpMethod.Post, "/api/auth/login-code",
                new { code = "WM-AAAA-BBBB" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendJsonAsync(stranger, HttpMethod.Post, "/api/auth/login-code",
                new { code = "" })).StatusCode);

        // 4. Kod yenilənəndə köhnəsi DƏRHAL ölür
        var users = (await admin.GetFromJsonAsync<List<UserRowResponse>>("/api/admin/users"))!;
        var row = users.Single(u => u.Email == "brigada@test.az");
        Assert.True(row.HasCode);

        var renewed = (await (await SendJsonAsync(admin, HttpMethod.Post,
            $"/api/admin/users/{row.Id}/reset-code")).Content
            .ReadFromJsonAsync<AccessCodeResponse>())!;
        Assert.NotEqual(created.AccessCode, renewed.AccessCode);
        Assert.Null(renewed.Warning); // sistem kodu güclüdür — xəbərdarlıq olmamalıdır

        var afterReset = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendJsonAsync(afterReset, HttpMethod.Post, "/api/auth/login-code",
                new { code = created.AccessCode })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await SendJsonAsync(afterReset, HttpMethod.Post, "/api/auth/login-code",
                new { code = renewed.AccessCode })).StatusCode);

        // 5. Deaktiv hesabın kodu işləmir
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendJsonAsync(admin, HttpMethod.Post,
                $"/api/admin/users/{row.Id}/deactivate")).StatusCode);
        var blocked = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendJsonAsync(blocked, HttpMethod.Post, "/api/auth/login-code",
                new { code = renewed.AccessCode })).StatusCode);
    }

    private sealed record BulkCodesResponse(int Created, int LastSequence);
    private sealed record PagedQrResponse(List<QrCodeResponse> Items, int Total);
    private sealed record UnitRowResponse(Guid Id, string Code, string Status);
    private sealed record AntiforgeryResponse(string Token);
    private sealed record IdResponse(Guid Id);
    private sealed record QrCodeResponse(Guid Id, string Token, string HumanCode,
        string TargetType, Guid? TargetId, string Status, DateTime CreatedAtUtc, string? TargetName);
    private sealed record CreatedUserResponse(
        Guid Id, string Email, string TempPassword, string AccessCode);
    private sealed record AccessCodeResponse(string AccessCode, string? Warning);
    private sealed record TempPasswordResponse(string TempPassword);
    private sealed record UserRowResponse(Guid Id, string Email, string DisplayName,
        string Role, bool Deactivated, bool HasCode);
    private sealed record TopProductResponse(string Name, int Count);
    private sealed record DashboardResponse(int ProductsTotal, int ProductsPublished,
        int NewInquiries, int Scans30d, List<TopProductResponse> TopProducts, int UnscannedCodes);
}
