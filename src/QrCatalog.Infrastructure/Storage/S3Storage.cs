using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using QrCatalog.Application.Abstractions;

namespace QrCatalog.Infrastructure.Storage;

/// <summary>
/// S3-uyğun storage — Cloudflare R2 üçün. Konfiqurasiya:
/// Storage:S3:ServiceUrl (R2 endpoint) · AccessKey · SecretKey · Bucket · PublicBaseUrl
/// (R2-nin public bucket domeni və ya custom domen — şəkillər oradan birbaşa verilir).
/// </summary>
public sealed class S3Storage : IFileStorage
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly string _publicBaseUrl;

    public S3Storage(IConfiguration config)
    {
        var section = config.GetSection("Storage:S3");
        _bucket = section["Bucket"]
            ?? throw new InvalidOperationException("Storage:S3:Bucket verilməlidir.");
        _publicBaseUrl = (section["PublicBaseUrl"]
            ?? throw new InvalidOperationException("Storage:S3:PublicBaseUrl verilməlidir.")).TrimEnd('/');

        _client = new AmazonS3Client(
            section["AccessKey"] ?? throw new InvalidOperationException("Storage:S3:AccessKey verilməlidir."),
            section["SecretKey"] ?? throw new InvalidOperationException("Storage:S3:SecretKey verilməlidir."),
            new AmazonS3Config
            {
                ServiceURL = section["ServiceUrl"]
                    ?? throw new InvalidOperationException("Storage:S3:ServiceUrl verilməlidir."),
                ForcePathStyle = true, // R2 və MinIO path-style istəyir
            });
    }

    public async Task SaveAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = path,
            InputStream = content,
            ContentType = contentType,
            // R2 üçün MD5/checksum başlıqları problem yaradır — sadə saxla
            DisableDefaultChecksumValidation = true,
        }, ct);
    }

    public async Task DeleteByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var listed = await _client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = _bucket,
            Prefix = prefix.TrimEnd('/') + "/",
        }, ct);

        if (listed.S3Objects is not { Count: > 0 })
            return;

        await _client.DeleteObjectsAsync(new DeleteObjectsRequest
        {
            BucketName = _bucket,
            Objects = listed.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList(),
        }, ct);
    }

    public string GetPublicUrl(string path) => $"{_publicBaseUrl}/{path}";
}
