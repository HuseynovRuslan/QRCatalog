using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// M1-in "bitmiş sayılır" kriteriyası: admin girir və çıxır, səlahiyyətsiz istifadəçi bloklanır.
/// Real Postgres (Testcontainers) — lokalda Docker yoxdursa sakit keçilir, CI-da tam işləyir.
/// </summary>
public sealed class AuthFlowTests : IAsyncLifetime
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
            builder.UseSetting("Bootstrap:CompanyName", "Test müəssisəsi");
            builder.UseSetting("Bootstrap:CompanySlug", "test");
            builder.UseEnvironment("Development");
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        if (_database is not null) await _database.DisposeAsync();
    }

    private HttpClient CreateClient() =>
        _factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var res = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        return res!.Token;
    }

    [Fact]
    public async Task Me_WithoutLogin_Returns401()
    {
        if (_factory is null) return; // Docker yoxdur

        var client = CreateClient();
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        if (_factory is null) return;

        var client = CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = AdminEmail, password = "sehv-parol" }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithoutAntiforgeryToken_IsRejected()
    {
        if (_factory is null) return;

        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = AdminEmail, password = AdminPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullFlow_LoginMeLogout_Works()
    {
        if (_factory is null) return;

        var client = CreateClient();

        // 1. Giriş
        var loginToken = await GetAntiforgeryTokenAsync(client);
        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = AdminEmail, password = AdminPassword }),
        };
        loginRequest.Headers.Add("X-XSRF-TOKEN", loginToken);

        var loginResponse = await client.SendAsync(loginRequest);
        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        Assert.True(loginResponse.IsSuccessStatusCode, $"Giriş alınmadı: {loginBody}");

        // 2. /me — giriş etmiş istifadəçini qaytarır
        var me = await client.GetFromJsonAsync<UserInfoResponse>("/api/auth/me");
        Assert.NotNull(me);
        Assert.Equal(AdminEmail, me.Email);
        Assert.Contains("Admin", me.Roles);
        Assert.NotNull(me.CompanyId); // bootstrap admin müəssisəyə bağlıdır

        // 3. Çıxış — girişdən sonra token rotasiya olunub, yenisini al
        var logoutToken = await GetAntiforgeryTokenAsync(client);
        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("X-XSRF-TOKEN", logoutToken);

        var logoutResponse = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // 4. Çıxışdan sonra /me yenidən 401
        var meAfter = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfter.StatusCode);
    }

    private sealed record AntiforgeryResponse(string Token);

    private sealed record UserInfoResponse(
        string Email, string DisplayName, string[] Roles, Guid? CompanyId);
}
