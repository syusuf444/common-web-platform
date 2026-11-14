using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Platform.Modules.Webhooks.Application;
using Platform.Modules.Webhooks.Domain;

namespace Platform.Api.Webhooks;

public sealed class WebhookService(
    IWebhookStore store,
    WebhookEndpointPolicy endpointPolicy,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider) : IWebhookService
{
    private readonly IDataProtector _secretProtector = dataProtectionProvider.CreateProtector("CommonWebPlatform.Webhooks.SigningSecret.v1");

    public async Task<CreatedWebhookSubscriptionResponse> CreateSubscriptionAsync(
        Guid tenantId,
        CreateWebhookSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var errors = WebhookRequestValidator.Validate(request.EventType, request.EndpointUrl);
        if (errors.Count != 0)
        {
            throw new WebhookRequestValidationException(errors);
        }

        var endpoint = new Uri(request.EndpointUrl, UriKind.Absolute);
        if (!endpointPolicy.IsAllowed(endpoint))
        {
            throw new WebhookRequestValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.EndpointUrl)] = ["Endpoint host is not platform-allowlisted or its scheme is not permitted in this environment."],
            });
        }

        var now = timeProvider.GetUtcNow();
        var id = Guid.NewGuid();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        await store.AddSubscriptionAsync(
            tenantId,
            id,
            request.EventType,
            endpoint.AbsoluteUri,
            _secretProtector.Protect(secret),
            now,
            cancellationToken);

        return new CreatedWebhookSubscriptionResponse(
            id,
            request.EventType,
            endpoint.AbsoluteUri,
            secret,
            true,
            now);
    }

    public Task<IReadOnlyList<WebhookSubscriptionResponse>> ListSubscriptionsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return store.ListSubscriptionsAsync(tenantId, cancellationToken);
    }

    public Task<WebhookSubscriptionResponse?> GetSubscriptionAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        return store.GetSubscriptionAsync(tenantId, subscriptionId, cancellationToken);
    }

    public Task<bool> DisableSubscriptionAsync(
        Guid tenantId,
        Guid subscriptionId,
        DateTimeOffset disabledAtUtc,
        CancellationToken cancellationToken)
    {
        return store.DisableSubscriptionAsync(tenantId, subscriptionId, disabledAtUtc, cancellationToken);
    }

    public Task<WebhookDeliveryListResponse> ListDeliveriesAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        return store.ListDeliveriesAsync(tenantId, skip, take, cancellationToken);
    }

    public Task<int> PublishAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);
        if (webhookEvent.TenantId == Guid.Empty)
        {
            throw new ArgumentException("A verified tenant ID is required.", nameof(webhookEvent));
        }

        var errors = WebhookRequestValidator.Validate(
            webhookEvent.EventType,
            "https://placeholder.invalid",
            webhookEvent.PayloadJson);
        if (errors.TryGetValue(nameof(CreateWebhookSubscriptionRequest.EventType), out var eventErrors))
        {
            throw new ArgumentException(string.Join(" ", eventErrors), nameof(webhookEvent));
        }

        if (errors.TryGetValue(nameof(WebhookEvent.PayloadJson), out var payloadErrors))
        {
            throw new ArgumentException(string.Join(" ", payloadErrors), nameof(webhookEvent));
        }

        return store.EnqueueAsync(webhookEvent, cancellationToken);
    }
}

public sealed class WebhookRequestValidationException(
    IReadOnlyDictionary<string, string[]> errors) : Exception("The webhook request is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
