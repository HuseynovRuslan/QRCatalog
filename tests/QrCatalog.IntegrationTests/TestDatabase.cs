using Npgsql;
using Testcontainers.PostgreSql;

// Hər test sinfi öz Postgres bazasını və öz host-unu qaldırır. Paralel işləyəndə
// WebApplicationFactory-nin entry-point tapıcısı (HostFactoryResolver) toqquşur və
// "The entry point exited without ever building an IHost" xətası verir — ilk CI
// işəsalmalarında dörd test bu səbəbdən uğursuz oldu. Ardıcıl işlətmək həm bunu,
// həm də runner-də resurs qıtlığını aradan qaldırır.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace QrCatalog.IntegrationTests;

/// <summary>
/// İnteqrasiya testləri üçün təmiz baza. İki rejim ardıcıllıqla yoxlanılır:
///
/// 1. <c>TEST_PG</c> mühit dəyişəni — mövcud Postgres serverində test üçün ayrıca baza
///    yaradılır. Docker tələb etmir, ona görə lokal maşında (Docker/WSL olmadan) bütün
///    inteqrasiya testləri işləyir. Bu vacibdir: əvvəl testlər lokalda səssizcə skip
///    olunurdu və qüsurlar yalnız CI-da görünürdü.
/// 2. Docker (CI, ya da <c>DOCKER_AVAILABLE=true</c>) — Testcontainers ilə konteyner.
///
/// Heç biri yoxdursa <c>null</c> qaytarır və test erkən çıxır (xunit 2.9-da Assert.Skip yoxdur).
/// </summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly PostgreSqlContainer? _container;
    private readonly string? _serverConnection;
    private readonly string? _databaseName;

    public string ConnectionString { get; }

    private TestDatabase(string connectionString, PostgreSqlContainer? container,
        string? serverConnection, string? databaseName)
    {
        ConnectionString = connectionString;
        _container = container;
        _serverConnection = serverConnection;
        _databaseName = databaseName;
    }

    public static async Task<TestDatabase?> StartAsync()
    {
        var server = Environment.GetEnvironmentVariable("TEST_PG");
        if (!string.IsNullOrWhiteSpace(server))
        {
            var name = "qrcatalog_test_" + Guid.NewGuid().ToString("N");
            await using (var connection = new NpgsqlConnection(server))
            {
                await connection.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
                await create.ExecuteNonQueryAsync();
            }

            var target = new NpgsqlConnectionStringBuilder(server) { Database = name };
            return new TestDatabase(target.ConnectionString, null, server, name);
        }

        if (Environment.GetEnvironmentVariable("CI") == "true" ||
            Environment.GetEnvironmentVariable("DOCKER_AVAILABLE") == "true")
        {
            var container = new PostgreSqlBuilder("postgres:18-alpine").Build();
            await container.StartAsync();
            return new TestDatabase(container.GetConnectionString(), container, null, null);
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        if (_serverConnection is null || _databaseName is null)
            return;

        // Havuzdaki bağlantılar bağlanmasa DROP DATABASE gözləyir
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_serverConnection);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
