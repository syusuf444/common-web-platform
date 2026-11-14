namespace Platform.Modules.Webhooks.Domain;

public sealed class WebhookSubscription
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string EndpointUrl { get; set; } = string.Empty;

    public string ProtectedSigningSecret { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? DisabledAtUtc { get; set; }
}
