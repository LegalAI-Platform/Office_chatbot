using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Office.Application.Panel;

namespace Office.Infrastructure.Panel;

/// <summary>
/// The client's files in storage (Garage in the test environment, R2 in production), read with the
/// api's own key (S3_API_ACCESS_KEY_ID): read-only on the two client buckets, nothing on backups.
/// Only those two buckets are ever read, whatever a stored reference says.
/// </summary>
public sealed class S3ClientFiles : IClientFiles, IDisposable
{
    public static readonly string[] Buckets = ["office-media", "office-sensitive"];
    private readonly AmazonS3Client? _s3;

    public S3ClientFiles(IConfiguration config)
    {
        var id = config["S3:AccessKeyId"];
        var secret = config["S3:SecretAccessKey"];
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        _s3 = new AmazonS3Client(new BasicAWSCredentials(id, secret), new AmazonS3Config
        {
            ServiceURL = config["S3:Endpoint"] is { Length: > 0 } e ? e : "http://garage:3900",
            AuthenticationRegion = config["S3:Region"] is { Length: > 0 } r ? r : "garage",
            ForcePathStyle = true,
            Timeout = TimeSpan.FromSeconds(30),
            MaxErrorRetry = 2,
        });
    }

    public bool Configured => _s3 is not null;

    public async Task<ClientFile?> OpenAsync(ClientFileRef file, CancellationToken ct)
    {
        if (_s3 is null || !Buckets.Contains(file.Bucket, StringComparer.Ordinal))
        {
            return null;
        }

        try
        {
            var o = await _s3.GetObjectAsync(new GetObjectRequest { BucketName = file.Bucket, Key = file.Key }, ct);
            return new ClientFile(o.ResponseStream, file.MimeType ?? o.Headers.ContentType ?? "application/octet-stream",
                o.Headers.ContentLength > 0 ? o.Headers.ContentLength : null, file.Filename);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public void Dispose() => _s3?.Dispose();
}
