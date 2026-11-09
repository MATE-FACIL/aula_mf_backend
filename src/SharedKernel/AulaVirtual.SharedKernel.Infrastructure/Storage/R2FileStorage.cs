using Amazon.S3;
using Amazon.S3.Model;
using AulaVirtual.SharedKernel.Application.Storage;

namespace AulaVirtual.SharedKernel.Infrastructure.Storage;

public sealed class R2FileStorage(IAmazonS3 s3, R2StorageOptions options) : IFileStorage
{
    public async Task<StoredObject> StoreAsync(StoreFileRequest request, CancellationToken cancellationToken = default)
    {
        FileObjectKeys.Validate(request.ObjectKey);
        ArgumentNullException.ThrowIfNull(request.Content);
        if (!request.Content.CanRead) throw new ArgumentException("Content must be readable.", nameof(request));
        if (request.Size is not { } size) throw new ArgumentException("Backend upload size is required.", nameof(request));
        options.ValidateContent(request.ContentType, size, direct: false);
        if (request.OriginalFileName is { } name &&
            (name.Length > 255 || name.Any(char.IsControl)))
            throw new ArgumentException("Original filename contains invalid metadata characters.", nameof(request));
        if (request.Content.CanSeek && request.Content.Length - request.Content.Position != size)
            throw new ArgumentException("Declared size does not match stream length.", nameof(request));

        using var boundedContent = new BoundedReadStream(request.Content, size);
        var put = new PutObjectRequest
        {
            BucketName = options.Bucket(request.Visibility),
            Key = request.ObjectKey,
            InputStream = boundedContent,
            ContentType = request.ContentType,
            AutoCloseStream = false,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        };
        put.Headers.ContentLength = size;
        put.Metadata["original-filename"] = request.OriginalFileName ?? string.Empty;
        await s3.PutObjectAsync(put, cancellationToken);
        if (boundedContent.BytesRead != size)
            throw new InvalidDataException("Uploaded stream ended before the declared size.");
        return new StoredObject(request.ObjectKey, request.Visibility, request.ContentType, size);
    }

    public Task DeleteAsync(string objectKey, FileVisibility visibility, CancellationToken cancellationToken = default)
    {
        FileObjectKeys.Validate(objectKey);
        return s3.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = options.Bucket(visibility), Key = objectKey
        }, cancellationToken);
    }

    public async Task<FileObjectInfo?> GetInfoAsync(string objectKey, FileVisibility visibility,
        CancellationToken cancellationToken = default)
    {
        FileObjectKeys.Validate(objectKey);
        try
        {
            var result = await s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = options.Bucket(visibility), Key = objectKey
            }, cancellationToken);
            return new FileObjectInfo(objectKey, visibility, result.Headers.ContentType,
                result.ContentLength, result.LastModified);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Uri? GetPublicUrl(string objectKey)
    {
        FileObjectKeys.Validate(objectKey);
        if (string.IsNullOrWhiteSpace(options.PublicBaseUrl))
            return null;
        return new Uri(new Uri(options.PublicBaseUrl!.TrimEnd('/') + "/"), objectKey);
    }

    public Task<Uri> GetPrivateDownloadUrlAsync(string objectKey, TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileObjectKeys.Validate(objectKey);
        options.ValidateExpiration(expiresIn);
        return SignAsync(new GetPreSignedUrlRequest
        {
            BucketName = options.Bucket(FileVisibility.Private), Key = objectKey,
            Verb = HttpVerb.GET, Expires = DateTime.UtcNow.Add(expiresIn)
        }, cancellationToken);
    }

    public async Task<DirectUploadGrant> CreateDirectUploadAsync(DirectUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        FileObjectKeys.Validate(request.ObjectKey);
        options.ValidateContent(request.ContentType, request.Size, direct: true);
        options.ValidateExpiration(request.ExpiresIn);
        var expiresAt = DateTimeOffset.UtcNow.Add(request.ExpiresIn);
        var url = await SignAsync(new GetPreSignedUrlRequest
        {
            BucketName = options.Bucket(request.Visibility), Key = request.ObjectKey,
            Verb = HttpVerb.PUT, Expires = expiresAt.UtcDateTime,
            ContentType = request.ContentType
        }, cancellationToken);
        return new DirectUploadGrant(url, "PUT",
            new Dictionary<string, string> { ["Content-Type"] = request.ContentType }, expiresAt);
    }

    private async Task<Uri> SignAsync(GetPreSignedUrlRequest request, CancellationToken cancellationToken)
    {
        // The SDK signs https:// links by default; follow the endpoint so a local HTTP server (development only,
        // see AllowHttpEndpoint) gets links that work. R2 is always HTTPS.
        request.Protocol = Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) && endpoint.Scheme == Uri.UriSchemeHttp
            ? Protocol.HTTP
            : Protocol.HTTPS;
        var url = await s3.GetPreSignedURLAsync(request);
        cancellationToken.ThrowIfCancellationRequested();
        return new Uri(url);
    }
}
