namespace Platform.Modules.Files.Application;

/// <summary>Metadata for a file stored in a tenant's local file area.</summary>
public sealed record TenantFileResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAtUtc);

/// <summary>Metadata and a readable stream for a tenant file download.</summary>
public sealed record TenantFileDownload(TenantFileResponse Metadata, Stream Content);

public interface ITenantFileStorage
{
    Task<long> StoreAsync(Guid tenantId, Guid fileId, Stream content, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken);
}

public interface ITenantFileRepository
{
    Task AddAsync(
        Guid tenantId,
        Guid fileId,
        Guid uploadedByUserId,
        string fileName,
        string contentType,
        long sizeBytes,
        DateTimeOffset uploadedAtUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantFileResponse>> ListAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<TenantFileResponse?> GetAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken);
}

public interface ITenantFileService
{
    Task<TenantFileResponse> UploadAsync(
        Guid tenantId,
        Guid userId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream content,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantFileResponse>> ListAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<TenantFileResponse?> GetAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken);

    Task<TenantFileDownload?> OpenDownloadAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken);
}
