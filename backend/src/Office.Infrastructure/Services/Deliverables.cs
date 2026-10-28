using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace Office.Infrastructure.Services;

/// <summary>A delivered file as storage reports it (the API never reads its content).</summary>
public sealed record StoredObject(long Size, string? ContentType);

/// <summary>
/// TASK 10 (v1.14, I1–I3): the file a lawyer delivers. The API only signs links and checks the file is there;
/// the bytes go from the panel to storage through the panel's nginx (/storage/…, same origin) and from storage
/// to WhatsApp by a short read link. Everything lives under office-sensitive/deliverables/&lt;order&gt;/.
/// </summary>
public interface IDeliverables
{
    bool Configured { get; }

    /// <summary>A signed PUT (10 minutes) for the panel: a path under /storage/, and the key it writes.</summary>
    (string Url, string Key) UploadLink(Guid orderId, string filename, string contentType);

    Task<StoredObject?> HeadAsync(string key, CancellationToken ct);

    /// <summary>A signed GET (one hour) Meta can read the file from (S3:PublicEndpoint in production, R2).</summary>
    string ReadLink(string key);
}

public sealed class S3Deliverables : IDeliverables, IDisposable
{
    public const string Bucket = "office-sensitive";
    public const string Prefix = "deliverables/";

    /// <summary>The panel's nginx forwards /storage/ to the storage endpoint the upload link is signed for.</summary>
    public const string UploadPath = "/storage";

    private readonly AmazonS3Client? _s3;
    private readonly AmazonS3Client? _public;
    private readonly string _endpoint = "";
    private readonly string _publicEndpoint = "";

    public S3Deliverables(IConfiguration config)
    {
        var id = config["S3:AccessKeyId"];
        var secret = config["S3:SecretAccessKey"];
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        _endpoint = (config["S3:Endpoint"] is { Length: > 0 } e ? e : "http://garage:3900").TrimEnd('/');
        var region = config["S3:Region"] is { Length: > 0 } r ? r : "garage";
        AmazonS3Client Client(string url) => new(new BasicAWSCredentials(id, secret), new AmazonS3Config
        {
            ServiceURL = url,
            AuthenticationRegion = region,
            ForcePathStyle = true,
            Timeout = TimeSpan.FromSeconds(30),
            MaxErrorRetry = 2,
        });
        _s3 = Client(_endpoint);
        _publicEndpoint = config["S3:PublicEndpoint"] is { Length: > 0 } p ? p.TrimEnd('/') : _endpoint;
        _public = Client(_publicEndpoint);
    }

    public bool Configured => _s3 is not null;

    public (string Url, string Key) UploadLink(Guid orderId, string filename, string contentType)
    {
        var key = $"{Prefix}{orderId}/{Guid.NewGuid():N}{Path.GetExtension(filename).ToLowerInvariant()}";
        var url = _s3!.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.AddMinutes(10),
            Protocol = _endpoint.StartsWith("https", StringComparison.Ordinal) ? Protocol.HTTPS : Protocol.HTTP,
        });
        // Signed for the storage host; the browser sends it to the panel's own origin, which forwards it there.
        var path = new Uri(url).PathAndQuery;
        return (UploadPath + path, key);
    }

    public async Task<StoredObject?> HeadAsync(string key, CancellationToken ct)
    {
        try
        {
            var m = await _s3!.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = Bucket, Key = key }, ct);
            return new StoredObject(m.ContentLength, m.Headers.ContentType);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// The name WhatsApp shows goes in the message itself (document.filename), not in the link. v1.28: signed for the
    /// endpoint's own scheme (the SDK's default is https, and the local storage speaks http: n8n could not read it).
    /// </summary>
    public string ReadLink(string key) => _public!.GetPreSignedURL(new GetPreSignedUrlRequest
    {
        BucketName = Bucket,
        Key = key,
        Verb = HttpVerb.GET,
        Expires = DateTime.UtcNow.AddHours(1),
        Protocol = _publicEndpoint.StartsWith("https", StringComparison.Ordinal) ? Protocol.HTTPS : Protocol.HTTP,
    });

    public void Dispose()
    {
        _s3?.Dispose();
        _public?.Dispose();
    }
}
