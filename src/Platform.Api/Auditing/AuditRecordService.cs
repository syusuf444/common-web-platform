using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Data;
using Platform.Modules.Tenancy.Application;

namespace Platform.Api.Auditing;

/// <summary>Describes a security or administration event to append to the audit log.</summary>
public sealed record AuditEvent(
    string Action,
    string ResourceType,
    string? ResourceId = null,
    Guid? ActorUserId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>A security or administration event returned by the tenant audit API.</summary>
public sealed record AuditRecordResponse(
    Guid Id,
    Guid? TenantId,
    Guid? ActorUserId,
    string Action,
    string ResourceType,
    string? ResourceId,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? MetadataJson);

/// <summary>A page of audit events for the active tenant.</summary>
public sealed record AuditRecordListResponse(
    IReadOnlyList<AuditRecordResponse> Items,
    int Skip,
    int Take,
    bool HasMore);

public interface IAuditRecordService
{
    Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<AuditRecordListResponse> ListAsync(int skip, int take, CancellationToken cancellationToken = default);
}

public sealed class AuditRecordService(
    PlatformDbContext database,
    ITenantContextAccessor tenantContext,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider) : IAuditRecordService
{
    public async Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        if (string.IsNullOrWhiteSpace(auditEvent.Action) || auditEvent.Action.Length > 100)
        {
            throw new ArgumentException("Audit action must be between 1 and 100 characters.", nameof(auditEvent));
        }

        if (string.IsNullOrWhiteSpace(auditEvent.ResourceType) || auditEvent.ResourceType.Length > 100)
        {
            throw new ArgumentException("Audit resource type must be between 1 and 100 characters.", nameof(auditEvent));
        }

        if (auditEvent.ResourceId?.Length > 128)
        {
            throw new ArgumentException("Audit resource ID cannot exceed 128 characters.", nameof(auditEvent));
        }

        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Audit records can only be written during an HTTP request.");
        var correlationId = httpContext.Items[CorrelationId.ItemKey] as string
            ?? throw new InvalidOperationException("The request correlation ID is unavailable.");
        var actorId = auditEvent.ActorUserId;
        if (actorId is null
            && Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var authenticatedUserId))
        {
            actorId = authenticatedUserId;
        }

        var metadataJson = auditEvent.Metadata is { Count: > 0 }
            ? JsonSerializer.Serialize(auditEvent.Metadata)
            : null;

        database.AuditRecords.Add(new AuditRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.Current?.TenantId,
            ActorUserId = actorId,
            Action = auditEvent.Action,
            ResourceType = auditEvent.ResourceType,
            ResourceId = auditEvent.ResourceId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            CorrelationId = correlationId,
            MetadataJson = metadataJson,
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuditRecordListResponse> ListAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var records = await database.AuditRecords
            .AsNoTracking()
            .Where(record => record.TenantId == tenantId)
            .OrderByDescending(record => record.OccurredAtUtc)
            .ThenByDescending(record => record.Id)
            .Skip(skip)
            .Take(take + 1)
            .Select(record => new AuditRecordResponse(
                record.Id,
                record.TenantId,
                record.ActorUserId,
                record.Action,
                record.ResourceType,
                record.ResourceId,
                record.OccurredAtUtc,
                record.CorrelationId,
                record.MetadataJson))
            .ToListAsync(cancellationToken);

        var hasMore = records.Count > take;
        if (hasMore)
        {
            records.RemoveAt(records.Count - 1);
        }

        return new AuditRecordListResponse(records, skip, take, hasMore);
    }
}
