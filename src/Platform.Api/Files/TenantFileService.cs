using Platform.Modules.Files.Application;

namespace Platform.Api.Files;

public sealed class TenantFileService(
    ITenantFileRepository files,
    ITenantFileStorage storage,
    TimeProvider timeProvider) : ITenantFileService
{
    public async Task<TenantFileResponse> UploadAsync(
        Guid tenantId,
        Guid userId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream content,
        CancellationToken cancellationToken)
    {
        var errors = TenantFileRequestValidator.Validate(fileName, contentType, sizeBytes);
        if (errors.Count != 0)
        {
            throw new TenantFileRequestValidationException(errors);
        }

        var normalizedFileName = TenantFileRequestValidator.NormalizeFileName(fileName);
        if (!await TenantFileRequestValidator.HasValidSignatureAsync(normalizedFileName, content, cancellationToken))
        {
            throw new TenantFileRequestValidationException(
                new Dictionary<string, string[]> { ["file"] = ["The file content does not match the permitted file type."] });
        }

        var fileId = Guid.NewGuid();
        var uploadedAtUtc = timeProvider.GetUtcNow();
        var storedSize = await storage.StoreAsync(tenantId, fileId, content, cancellationToken);
        var metadataPersisted = false;
        try
        {
            if (storedSize != sizeBytes)
            {
                throw new TenantFileRequestValidationException(
                    new Dictionary<string, string[]> { ["file"] = ["The uploaded file size did not match its content."] });
            }

            await files.AddAsync(
                tenantId,
                fileId,
                userId,
                normalizedFileName,
                contentType,
                storedSize,
                uploadedAtUtc,
                cancellationToken);
            metadataPersisted = true;
        }
        finally
        {
            if (!metadataPersisted)
            {
                await storage.DeleteAsync(tenantId, fileId, CancellationToken.None);
            }
        }

        return new TenantFileResponse(fileId, normalizedFileName, contentType, storedSize, uploadedAtUtc);
    }

    public Task<IReadOnlyList<TenantFileResponse>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return files.ListAsync(tenantId, cancellationToken);
    }

    public Task<TenantFileResponse?> GetAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken)
    {
        return files.GetAsync(tenantId, fileId, cancellationToken);
    }

    public async Task<TenantFileDownload?> OpenDownloadAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken)
    {
        var metadata = await files.GetAsync(tenantId, fileId, cancellationToken);
        if (metadata is null)
        {
            return null;
        }

        var content = await storage.OpenReadAsync(tenantId, fileId, cancellationToken);
        return new TenantFileDownload(metadata, content);
    }
}

public sealed class TenantFileRequestValidationException(
    IReadOnlyDictionary<string, string[]> errors) : Exception("The file request is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
