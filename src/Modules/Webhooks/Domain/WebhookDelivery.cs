namespace Platform.Modules.Webhooks.Domain;

public sealed class WebhookDelivery
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid SubscriptionId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public string Status { get; set; } = WebhookDeliveryStatuses.Pending;

    public int AttemptCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset NextAttemptAtUtc { get; set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public int? LastStatusCode { get; set; }

    public string? LastError { get; set; }
}

public static class WebhookDeliveryStatuses
{
    public const int MaximumAttempts = 5;
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}
