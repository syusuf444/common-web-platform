namespace Platform.Modules.Webhooks.Domain;

public sealed class WebhookDeliveryAttempt
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid DeliveryId { get; set; }

    public int AttemptNumber { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset CompletedAtUtc { get; set; }

    public int? HttpStatusCode { get; set; }

    public bool Succeeded { get; set; }

    public string? Error { get; set; }
}
