using System.Net;
using System.Net.Http.Json;
using ImageMagick;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// M3 "bitmiş sayılır" axını: məhsul yaranır, şəkil yüklənir (lokal storage), dərc olunur,
/// kopyalanır; QR məhsula bağlanır və /q/{token} yalnız Published halda məhsulu göstərir.
/// </summary>
public sealed class ProductFlowTests : IAsyncLifetime
{
    private const string AdminEmail = "admin@test.az";
    private const string AdminPassword = "Passw0rd!23";

    private static bool DockerAvailable =>
        Environment.GetEnvironmentVariable("CI") == "true" ||
        Environment.GetEnvironmentVariable("DOCKER_AVAILABLE") == "true";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private string? _storageRoot;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable)
            return;

        _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _postgres.StartAsync();

        _storageRoot = Path.Combine(Path.GetTempPath(), "qrcatalog-tests", Guid.NewGuid().ToString("N"));

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
            builder.UseSetting("Bootstrap:AdminEmail", AdminEmail);
            builder.UseSetting("Bootstrap:AdminPassword", AdminPassword);
            builder.UseSetting("Storage:Local:Root", _storageRoot);
            builder.UseEnvironment("Development");
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
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

    private static byte[] MakePng(uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, width, height);
        image.Format = MagickFormat.Png;
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task FullFlow_Create_Specs_Image_Publish_Qr_PublicPage()
    {
        if (_factory is null) return; // Docker yoxdur

        var admin = await LoginAsync();

        // Kateqoriya + məhsul
        var category = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/categories",
            new { name = "Şezlonqlar", codePrefix = "SZ" })).Content
            .ReadFromJsonAsync<IdResponse>())!;

        var createRes = await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/products",
            new
            {
                name = "Bahama şezlonq, ağ",
                description = "UV-davamlı plastik.",
                categoryId = category.Id,
                sku = "SZ-AG-01",
            });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var product = (await createRes.Content.ReadFromJsonAsync<IdResponse>())!;

        // Spesifikasiyalar
        var specsRes = await SendJsonAsync(admin, HttpMethod.Put,
            $"/api/admin/products/{product.Id}/specs",
            new { specs = new[] { new { label = "Ölçü", value = "190×60 sm" },
                                  new { label = "Material", value = "Plastik" } } });
        Assert.True(specsRes.StatusCode == HttpStatusCode.NoContent,
            $"Specs: {(int)specsRes.StatusCode} — {await specsRes.Content.ReadAsStringAsync()}");

        // Şəkil yükləmə — multipart + XSRF başlığı
        var xsrf = (await admin.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery"))!.Token;
        using var form = new MultipartFormDataContent();
        var png = new ByteArrayContent(MakePng(1600, 900));
        png.Headers.ContentType = new("image/png");
        form.Add(png, "files", "sekil.png");
        var uploadReq = new HttpRequestMessage(HttpMethod.Post,
            $"/api/admin/products/{product.Id}/images")
        { Content = form };
        uploadReq.Headers.Add("X-XSRF-TOKEN", xsrf);
        var uploadRes = await admin.SendAsync(uploadReq);
        Assert.True(uploadRes.IsSuccessStatusCode,
            $"Yükləmə alınmadı: {await uploadRes.Content.ReadAsStringAsync()}");

        // Detail: variantlar var və URL-lər işləyir
        var detail = (await admin.GetFromJsonAsync<ProductDetailResponse>(
            $"/api/admin/products/{product.Id}"))!;
        Assert.Equal(2, detail.Specs.Count);
        var image = Assert.Single(detail.Images);
        Assert.Equal([320, 640, 1280, 1600], image.Variants.Select(v => v.Width));

        var anon = _factory.CreateClient();
        var imgRes = await anon.GetAsync(image.Variants[0].Url);
        Assert.True(imgRes.IsSuccessStatusCode, $"Şəkil URL-i açılmadı: {image.Variants[0].Url}");
        Assert.Equal("image/webp", imgRes.Content.Headers.ContentType?.MediaType);

        // Rədd: PDF şəkil kimi keçmir
        using var badForm = new MultipartFormDataContent();
        var pdf = new ByteArrayContent("%PDF-1.4 saxta"u8.ToArray());
        pdf.Headers.ContentType = new("image/png"); // content-type yalan deyir
        badForm.Add(pdf, "files", "saxta.png");
        var badReq = new HttpRequestMessage(HttpMethod.Post,
            $"/api/admin/products/{product.Id}/images")
        { Content = badForm };
        badReq.Headers.Add("X-XSRF-TOKEN",
            (await admin.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery"))!.Token);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(badReq)).StatusCode);

        // QR məhsula bağlanır — prefiks kateqoriyadan (SZ)
        var qr = (await (await SendJsonAsync(admin, HttpMethod.Post, "/api/admin/qrcodes",
            new { targetType = "product", targetId = product.Id })).Content
            .ReadFromJsonAsync<QrCodeResponse>())!;
        Assert.StartsWith("SZ-", qr.HumanCode);

        // Draft ikən qonaq məhsulu görmür
        var draftPage = await anon.GetStringAsync($"/q/{qr.Token}");
        Assert.Contains("istehsal olunmur", draftPage);

        // Dərc olunandan sonra görünür
        await SendJsonAsync(admin, HttpMethod.Post, $"/api/admin/products/{product.Id}/publish");
        var publishedPage = await anon.GetStringAsync($"/q/{qr.Token}");
        Assert.Contains("Bahama şezlonq", publishedPage);

        // Arxivə keçəndə yenidən "istehsal olunmur"
        await SendJsonAsync(admin, HttpMethod.Post, $"/api/admin/products/{product.Id}/archive");
        var archivedPage = await anon.GetStringAsync($"/q/{qr.Token}");
        Assert.Contains("istehsal olunmur", archivedPage);

        // Kopya: specs köçür, şəkil yox, status Draft
        var copy = (await (await SendJsonAsync(admin, HttpMethod.Post,
            $"/api/admin/products/{product.Id}/copy")).Content.ReadFromJsonAsync<IdResponse>())!;
        var copyDetail = (await admin.GetFromJsonAsync<ProductDetailResponse>(
            $"/api/admin/products/{copy.Id}"))!;
        Assert.Contains("(kopya)", copyDetail.Name);
        Assert.Equal("Draft", copyDetail.Status);
        Assert.Equal(2, copyDetail.Specs.Count);
        Assert.Empty(copyDetail.Images);

        // Məhsulu olan kateqoriya silinmir
        var deleteRes = await SendJsonAsync(admin, HttpMethod.Delete,
            $"/api/admin/categories/{category.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteRes.StatusCode);

        // Silmə endpoint-i yoxdur — məhsul yalnız arxivləşir
        var noDelete = await SendJsonAsync(admin, HttpMethod.Delete,
            $"/api/admin/products/{product.Id}");
        Assert.True(noDelete.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    private sealed record AntiforgeryResponse(string Token);
    private sealed record IdResponse(Guid Id);
    private sealed record QrCodeResponse(Guid Id, string Token, string HumanCode,
        string TargetType, Guid? TargetId, string Status, DateTime CreatedAtUtc, string? TargetName);
    private sealed record ProductDetailResponse(Guid Id, string Name, string? Description,
        Guid CategoryId, string? Sku, string Slug, string Status,
        List<SpecResponse> Specs, List<ImageResponse> Images);
    private sealed record SpecResponse(string Label, string Value);
    private sealed record ImageResponse(Guid Id, string? AltText, List<VariantResponse> Variants);
    private sealed record VariantResponse(int Width, string Url);
}
