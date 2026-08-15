using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// M6 "bitmiş sayılır" axını: qonaq sorğu göndərir → admin panelə mənbəyi ilə düşür;
/// honeypot bot cəhdini udur; status/qeyd axını işləyir.
/// </summary>
public sealed class InquiryFlowTests : IAsyncLifetime
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

    [Fact]
    public async Task FullFlow_PublicSubmit_AdminSees_StatusAndNote()
    {
        if (_factory is null) return; // Docker yoxdur

        var admin = await LoginAsync();
        var anon = _factory.CreateClient();

        // Məhsul + QR hazırla
        var category = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/categories",
            new { name = "Şezlonqlar", codePrefix = "SZ" })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        var product = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new { name = "Bahama şezlonq", categoryId = category.Id })).Content
            .ReadFromJsonAsync<IdResponse>())!;
        await SendJsonAsync(admin, HttpMethod.Post, $"/api/admin/products/{product.Id}/publish");
        var qr = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/qrcodes",
            new { targetType = "product", targetId = product.Id })).Content
            .ReadFromJsonAsync<QrCodeResponse>())!;

        // 1. Qonaq QR-dan gələn səhifədən sorğu göndərir
        var submit = await anon.PostAsJsonAsync("/api/public/inquiries", new
        {
            name = "Orxan Məmmədov",
            phone = "+994 50 123 45 67",
            message = "Qiyməti neçəyədir? 10 ədəd lazımdır.",
            qrToken = qr.Token,
        });
        Assert.Equal(HttpStatusCode.NoContent, submit.StatusCode);

        // 2. Honeypot dolu — bota uğur görünür, amma saxlanmır
        var bot = await anon.PostAsJsonAsync("/api/public/inquiries", new
        {
            name = "Bot",
            phone = "+994501111111",
            website = "http://spam.example",
        });
        Assert.Equal(HttpStatusCode.NoContent, bot.StatusCode);

        // 3. Yanlış telefon rədd olunur
        var badPhone = await anon.PostAsJsonAsync("/api/public/inquiries", new
        {
            name = "Qısa",
            phone = "12",
        });
        Assert.Equal(HttpStatusCode.BadRequest, badPhone.StatusCode);

        // 4. Admin siyahısı: yalnız real sorğu, mənbəyi ilə
        var list = (await admin.GetFromJsonAsync<PagedResponse>("/api/admin/inquiries"))!;
        var inquiry = Assert.Single(list.Items);
        Assert.Equal("Orxan Məmmədov", inquiry.Name);
        Assert.Equal("Bahama şezlonq", inquiry.ProductName);
        Assert.Equal(qr.HumanCode, inquiry.HumanCode);
        Assert.Equal("New", inquiry.Status);
        Assert.Contains("10 ədəd", inquiry.Message);

        // 5. Status + daxili qeyd
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendJsonAsync(admin, HttpMethod.Put,
                $"/api/admin/inquiries/{inquiry.Id}/status", new { status = "answered" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendJsonAsync(admin, HttpMethod.Put,
                $"/api/admin/inquiries/{inquiry.Id}/note",
                new { note = "Zəng etdim, qiymət razılaşdırıldı" })).StatusCode);

        var updated = (await admin.GetFromJsonAsync<PagedResponse>(
            "/api/admin/inquiries?status=answered"))!;
        var item = Assert.Single(updated.Items);
        Assert.Equal("Answered", item.Status);
        Assert.Contains("razılaşdırıldı", item.InternalNote);

        // 6. Girişsiz admin siyahısı yoxdur
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync("/api/admin/inquiries")).StatusCode);

        // 7. Public məhsul səhifəsində forma elementləri var
        var page = await anon.GetStringAsync($"/q/{qr.Token}");
        Assert.Contains("inquiry-form", page);
        Assert.Contains("Sorğu göndərin", page);
    }

    private sealed record AntiforgeryResponse(string Token);
    private sealed record IdResponse(Guid Id);
    private sealed record QrCodeResponse(Guid Id, string Token, string HumanCode,
        string TargetType, Guid? TargetId, string Status, DateTime CreatedAtUtc, string? TargetName);
    private sealed record InquiryResponse(Guid Id, string Name, string Phone, string? Message,
        string Status, string? InternalNote, string? ProductName, string? HumanCode,
        DateTime CreatedAtUtc);
    private sealed record PagedResponse(List<InquiryResponse> Items, int Total, int Page, int PageSize);
}
