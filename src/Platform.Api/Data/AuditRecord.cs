namespace Platform.Api.Data;

public sealed class AuditRecord
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public Guid? ActorUserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string ResourceType { get; set; } = string.Empty;

    public string? ResourceId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string? MetadataJson { get; set; }
}
