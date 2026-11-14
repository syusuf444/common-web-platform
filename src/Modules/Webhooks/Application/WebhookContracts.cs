namespace Platform.Modules.Webhooks.Application;

/// <summary>Request to subscribe a tenant endpoint to an event type.</summary>
public sealed record CreateWebhookSubscriptionRequest
{
    /// <summary>Stable event name such as tenant.member.added.</summary>
    public required string EventType { get; init; }

    /// <summary>HTTPS endpoint on a host explicitly allowed by the platform operator.</summary>
    public required string EndpointUrl { get; init; }
}

/// <summary>Tenant webhook subscription metadata; secrets are never included.</summary>
public sealed record WebhookSubscriptionResponse(
    Guid Id,
    string EventType,
    string EndpointUrl,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

/// <summary>New webhook subscription response. SigningSecret is shown only once.</summary>
public sealed record CreatedWebhookSubscriptionResponse(
    Guid Id,
    string EventType,
    string EndpointUrl,
    string SigningSecret,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

/// <summary>A paged view of delivery outcomes for the active tenant.</summary>
public sealed record WebhookDeliveryListResponse(
    IReadOnlyList<WebhookDeliveryResponse> Items,
    int Skip,
    int Take,
    bool HasMore);

/// <summary>Webhook delivery state and bounded retry history.</summary>
public sealed record WebhookDeliveryResponse(
    Guid Id,
    Guid SubscriptionId,
    string EventType,
    string Status,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int? LastStatusCode,
    string? LastError,
    IReadOnlyList<WebhookDeliveryAttemptResponse> Attempts);

/// <summary>One signed webhook HTTP delivery attempt.</summary>
public sealed record WebhookDeliveryAttemptResponse(
    int AttemptNumber,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int? HttpStatusCode,
    bool Succeeded,
    string? Error);

/// <summary>Application event dispatched to matching active tenant subscriptions.</summary>
public sealed record WebhookEvent(
    Guid TenantId,
    string EventType,
    string PayloadJson,
    DateTimeOffset OccurredAtUtc);

public interface IWebhookPublisher
{
    Task<int> PublishAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken);
}

public interface IWebhookService : IWebhookPublisher
{
    Task<CreatedWebhookSubscriptionResponse> CreateSubscriptionAsync(
        Guid tenantId,
        CreateWebhookSubscriptionRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookSubscriptionResponse>> ListSubscriptionsAsync(
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<WebhookSubscriptionResponse?> GetSubscriptionAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken);

    Task<bool> DisableSubscriptionAsync(
        Guid tenantId,
        Guid subscriptionId,
        DateTimeOffset disabledAtUtc,
        CancellationToken cancellationToken);

    Task<WebhookDeliveryListResponse> ListDeliveriesAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken);
}

public interface IWebhookStore
{
    Task AddSubscriptionAsync(
        Guid tenantId,
        Guid subscriptionId,
        string eventType,
        string endpointUrl,
        string protectedSigningSecret,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookSubscriptionResponse>> ListSubscriptionsAsync(
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<WebhookSubscriptionResponse?> GetSubscriptionAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken);

    Task<bool> DisableSubscriptionAsync(
        Guid tenantId,
        Guid subscriptionId,
        DateTimeOffset disabledAtUtc,
        CancellationToken cancellationToken);

    Task<int> EnqueueAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken);

    Task<Guid?> ClaimNextDeliveryAsync(
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAtUtc,
        CancellationToken cancellationToken);

    Task<WebhookDeliveryWorkItem?> GetDeliveryAsync(Guid deliveryId, CancellationToken cancellationToken);

    Task CompleteAttemptAsync(
        WebhookDeliveryWorkItem workItem,
        WebhookDeliveryAttemptResult attempt,
        DateTimeOffset completedAtUtc,
        DateTimeOffset? nextAttemptAtUtc,
        int maximumAttempts,
        CancellationToken cancellationToken);

    Task<WebhookDeliveryListResponse> ListDeliveriesAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken);
}

public sealed record WebhookDeliveryWorkItem(
    Guid Id,
    Guid TenantId,
    Guid SubscriptionId,
    string EndpointUrl,
    string ProtectedSigningSecret,
    bool SubscriptionActive,
    string EventType,
    string PayloadJson,
    int AttemptNumber);

public sealed record WebhookDeliveryAttemptResult(
    DateTimeOffset StartedAtUtc,
    int? HttpStatusCode,
    bool Succeeded,
    bool IsTransient,
    string? Error);

public interface IWebhookDeliveryTransport
{
    Task<WebhookDeliveryAttemptResult> SendAsync(
        WebhookDeliveryWorkItem workItem,
        CancellationToken cancellationToken);
}

public interface IWebhookDeliveryProcessor
{
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken);
}
