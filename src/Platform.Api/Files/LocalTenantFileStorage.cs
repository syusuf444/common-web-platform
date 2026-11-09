using Microsoft.Extensions.Options;
using Platform.Modules.Files.Application;

namespace Platform.Api.Files;

public sealed class LocalTenantFileStorageOptions
{
    public string RootPath { get; set; } = string.Empty;
}

public sealed class LocalTenantFileStorage(
    IOptions<LocalTenantFileStorageOptions> options,
    ILogger<LocalTenantFileStorage> logger) : ITenantFileStorage
{
    private readonly string _rootPath = Path.GetFullPath(options.Value.RootPath);

    public async Task<long> StoreAsync(Guid tenantId, Guid fileId, Stream content, CancellationToken cancellationToken)
    {
        var destinationPath = GetFilePath(tenantId, fileId);
        var directoryPath = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("The configured file storage path is invalid.");
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(directoryPath);
            if ((File.GetAttributes(directoryPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Tenant file storage directories cannot be symbolic links or junctions.");
            }

            long copiedBytes = 0;
            await using (var output = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Mode = FileMode.CreateNew,
                                 Access = FileAccess.Write,
                                 Share = FileShare.None,
                                 Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                             }))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await content.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    copiedBytes += read;
                    if (copiedBytes > TenantFileRequestValidator.MaxFileSizeBytes)
                    {
                        throw new InvalidDataException("The uploaded file exceeds the configured size limit.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await output.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath);
            logger.LogInformation(
                "Stored tenant file {FileId} for tenant {TenantId} at {StoragePath}",
                fileId,
                tenantId,
                destinationPath);
            return copiedBytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                exception,
                "Failed to store tenant file {FileId} for tenant {TenantId} at {StoragePath}",
                fileId,
                tenantId,
                destinationPath);
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<Stream> OpenReadAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetFilePath(tenantId, fileId);

        try
        {
            Stream stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });
            return Task.FromResult(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                exception,
                "Failed to read tenant file {FileId} for tenant {TenantId} from {StoragePath}",
                fileId,
                tenantId,
                path);
            throw;
        }
    }

    public Task DeleteAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetFilePath(tenantId, fileId);

        try
        {
            File.Delete(path);
            return Task.CompletedTask;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                exception,
                "Failed to remove tenant file {FileId} for tenant {TenantId} from {StoragePath}",
                fileId,
                tenantId,
                path);
            throw;
        }
    }

    private string GetFilePath(Guid tenantId, Guid fileId)
    {
        if (tenantId == Guid.Empty || fileId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and file identifiers must be valid.");
        }

        var path = Path.GetFullPath(Path.Combine(
            _rootPath,
            tenantId.ToString("N"),
            $"{fileId:N}.bin"));
        var relativePath = Path.GetRelativePath(_rootPath, path);
        if (Path.IsPathRooted(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The tenant file path resolves outside the configured storage root.");
        }

        return path;
    }
}
