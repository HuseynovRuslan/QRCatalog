using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// Real Postgres (Testcontainers) üzərində smoke testlər. Lokal maşında Docker yoxdursa
/// sakit keçilir — CI-da (ubuntu runner, Docker var) həmişə tam işləyir.
/// </summary>
public sealed class SmokeTests : IAsyncLifetime
{
    private static bool DockerAvailable =>
        Environment.GetEnvironmentVariable("CI") == "true" ||
        Environment.GetEnvironmentVariable("DOCKER_AVAILABLE") == "true";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable)
            return; // Docker yoxdur — testlər CI-da işləyəcək

        _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
            builder.UseEnvironment("Development");
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task HomePage_Returns200()
    {
        if (_factory is null) return; // Docker yoxdur

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");

        Assert.True(response.IsSuccessStatusCode,
            $"Ana səhifə {(int)response.StatusCode} qaytardı.");
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        if (_factory is null) return; // Docker yoxdur

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"/health {(int)response.StatusCode}: {body}");
        Assert.Equal("Healthy", body);
    }
}
