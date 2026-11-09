using Microsoft.EntityFrameworkCore;
using Platform.Modules.Files.Application;
using Platform.Modules.Files.Domain;

namespace Platform.Api.Data;

public sealed class EfTenantFileRepository(PlatformDbContext database) : ITenantFileRepository
{
    public async Task AddAsync(
        Guid tenantId,
        Guid fileId,
        Guid uploadedByUserId,
        string fileName,
        string contentType,
        long sizeBytes,
        DateTimeOffset uploadedAtUtc,
        CancellationToken cancellationToken)
    {
        database.TenantFiles.Add(new TenantFile
        {
            Id = fileId,
            TenantId = tenantId,
            UploadedByUserId = uploadedByUserId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            UploadedAtUtc = uploadedAtUtc,
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TenantFileResponse>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return await database.TenantFiles
            .AsNoTracking()
            .Where(file => file.TenantId == tenantId)
            .OrderByDescending(file => file.UploadedAtUtc)
            .ThenBy(file => file.Id)
            .Select(file => new TenantFileResponse(
                file.Id,
                file.FileName,
                file.ContentType,
                file.SizeBytes,
                file.UploadedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<TenantFileResponse?> GetAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken)
    {
        return database.TenantFiles
            .AsNoTracking()
            .Where(file => file.TenantId == tenantId && file.Id == fileId)
            .Select(file => new TenantFileResponse(
                file.Id,
                file.FileName,
                file.ContentType,
                file.SizeBytes,
                file.UploadedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
