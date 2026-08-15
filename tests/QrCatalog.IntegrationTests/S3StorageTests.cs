using Amazon.S3;
using Microsoft.Extensions.Configuration;
using QrCatalog.Infrastructure.Storage;
using Testcontainers.Minio;

namespace QrCatalog.IntegrationTests;

/// <summary>
/// S3Storage-in R2-yə gedəcək kodu MinIO (S3-uyğun) üzərində yoxlanılır —
/// R2 açarları olmadan real save/delete axını CI-da təsdiqlənir.
/// </summary>
public sealed class S3StorageTests : IAsyncLifetime
{
    private const string Bucket = "qrcatalog-test";

    private static bool DockerAvailable =>
        Environment.GetEnvironmentVariable("CI") == "true" ||
        Environment.GetEnvironmentVariable("DOCKER_AVAILABLE") == "true";

    private MinioContainer? _minio;
    private S3Storage? _storage;
    private IAmazonS3? _client;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable)
            return;

        _minio = new MinioBuilder("minio/minio:latest").Build();
        await _minio.StartAsync();

        _client = new AmazonS3Client(
            _minio.GetAccessKey(), _minio.GetSecretKey(),
            new AmazonS3Config
            {
                ServiceURL = _minio.GetConnectionString(),
                ForcePathStyle = true,
            });
        await _client.PutBucketAsync(Bucket);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:S3:ServiceUrl"] = _minio.GetConnectionString(),
            ["Storage:S3:AccessKey"] = _minio.GetAccessKey(),
            ["Storage:S3:SecretKey"] = _minio.GetSecretKey(),
            ["Storage:S3:Bucket"] = Bucket,
            ["Storage:S3:PublicBaseUrl"] = "https://cdn.example.az",
        }).Build();

        _storage = new S3Storage(config);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_minio is not null)
            await _minio.DisposeAsync();
    }

    [Fact]
    public async Task SaveAndDeleteByPrefix_RoundTrips()
    {
        if (_storage is null) return; // Docker yoxdur

        var bytes = "salam webp"u8.ToArray();
        await using (var ms = new MemoryStream(bytes))
            await _storage.SaveAsync("products/p1/img1/w320.webp", ms, "image/webp");
        await using (var ms = new MemoryStream(bytes))
            await _storage.SaveAsync("products/p1/img1/w640.webp", ms, "image/webp");

        // Yazılıb?
        var stored = await _client!.GetObjectAsync(Bucket, "products/p1/img1/w320.webp");
        Assert.Equal("image/webp", stored.Headers.ContentType);
        using var reader = new MemoryStream();
        await stored.ResponseStream.CopyToAsync(reader);
        Assert.Equal(bytes, reader.ToArray());

        // Prefiks üzrə silinir
        await _storage.DeleteByPrefixAsync("products/p1/img1");
        var listed = await _client.ListObjectsV2Async(new Amazon.S3.Model.ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = "products/p1",
        });
        Assert.True(listed.S3Objects is null or { Count: 0 });
    }

    [Fact]
    public void GetPublicUrl_UsesConfiguredBase()
    {
        if (_storage is null) return;

        Assert.Equal("https://cdn.example.az/products/p1/img1/w320.webp",
            _storage.GetPublicUrl("products/p1/img1/w320.webp"));
    }
}
