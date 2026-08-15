using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// Real Postgres (Testcontainers) üzərində smoke testlər. Lokal maşında Docker yoxdursa
/// sakit keçilir — CI-da (ubuntu runner, Docker var) həmişə tam işləyir.
/// </summary>
public sealed class SmokeTests : IAsyncLifetime
{
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
            builder.UseEnvironment("Development");
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_database is not null)
            await _database.DisposeAsync();
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
