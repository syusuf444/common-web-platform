namespace Platform.Modules.Notifications.Application;

/// <summary>Request to deliver a plain-text notification to one recipient.</summary>
public sealed record SendNotificationRequest
{
    /// <summary>Email address used as the recipient in the local notification outbox.</summary>
    public required string Recipient { get; init; }

    /// <summary>Short notification subject.</summary>
    public required string Subject { get; init; }

    /// <summary>Plain-text notification body.</summary>
    public required string Body { get; init; }
}

/// <summary>A tenant-scoped notification accepted by a delivery adapter.</summary>
public sealed record NotificationMessage(
    Guid Id,
    Guid TenantId,
    string Recipient,
    string Subject,
    string Body,
    DateTimeOffset CreatedAtUtc);

/// <summary>Receipt confirming a notification was written to the configured delivery adapter.</summary>
public sealed record NotificationDeliveryReceipt(
    Guid Id,
    string Status,
    DateTimeOffset DeliveredAtUtc);

/// <summary>A notification visible in a tenant's local outbox.</summary>
public sealed record NotificationOutboxItem(
    Guid Id,
    Guid TenantId,
    string Recipient,
    string Subject,
    string Body,
    DateTimeOffset CreatedAtUtc);

public interface INotificationDeliveryPort
{
    Task DeliverAsync(NotificationMessage notification, CancellationToken cancellationToken);
}

public interface INotificationOutboxReader
{
    Task<IReadOnlyList<NotificationOutboxItem>> ListAsync(
        Guid tenantId,
        int take,
        CancellationToken cancellationToken);

    Task<NotificationOutboxItem?> GetAsync(
        Guid tenantId,
        Guid notificationId,
        CancellationToken cancellationToken);
}

public interface INotificationService
{
    Task<NotificationDeliveryReceipt> SendAsync(
        Guid tenantId,
        SendNotificationRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationOutboxItem>> ListAsync(
        Guid tenantId,
        int take,
        CancellationToken cancellationToken);

    Task<NotificationOutboxItem?> GetAsync(
        Guid tenantId,
        Guid notificationId,
        CancellationToken cancellationToken);
}
