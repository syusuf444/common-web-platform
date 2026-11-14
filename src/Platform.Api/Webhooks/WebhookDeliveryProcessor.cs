using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Platform.Modules.Webhooks.Application;
using Platform.Modules.Webhooks.Domain;

namespace Platform.Api.Webhooks;

public sealed class WebhookDeliveryTransport(
    IHttpClientFactory clientFactory,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider,
    ILogger<WebhookDeliveryTransport> logger) : IWebhookDeliveryTransport
{
    private readonly IDataProtector _secretProtector = dataProtectionProvider.CreateProtector("CommonWebPlatform.Webhooks.SigningSecret.v1");

    public async Task<WebhookDeliveryAttemptResult> SendAsync(
        WebhookDeliveryWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        if (!workItem.SubscriptionActive)
        {
            return new WebhookDeliveryAttemptResult(startedAt, null, false, false, "Subscription is disabled.");
        }

        string secret;
        try
        {
            secret = _secretProtector.Unprotect(workItem.ProtectedSigningSecret);
        }
        catch (CryptographicException exception)
        {
            logger.LogError(exception, "Could not decrypt signing secret for webhook subscription {SubscriptionId}", workItem.SubscriptionId);
            return new WebhookDeliveryAttemptResult(startedAt, null, false, false, "Signing secret is unavailable.");
        }

        byte[] key;
        try
        {
            var base64 = secret.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            key = Convert.FromBase64String(base64);
        }
        catch (FormatException exception)
        {
            logger.LogError(exception, "Signing secret for webhook subscription {SubscriptionId} is malformed", workItem.SubscriptionId);
            return new WebhookDeliveryAttemptResult(startedAt, null, false, false, "Signing secret is malformed.");
        }

        var timestamp = startedAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signedPayload = Encoding.UTF8.GetBytes($"{timestamp}.{workItem.PayloadJson}");
        var signature = Convert.ToHexString(HMACSHA256.HashData(key, signedPayload)).ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, workItem.EndpointUrl)
        {
            Content = new StringContent(workItem.PayloadJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-CWP-Webhook-Id", workItem.Id.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-CWP-Webhook-Event", workItem.EventType);
        request.Headers.TryAddWithoutValidation("X-CWP-Webhook-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-CWP-Webhook-Signature", $"v1={signature}");

        try
        {
            var client = clientFactory.CreateClient(WebhookHttpClientNames.Delivery);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var statusCode = (int)response.StatusCode;
            var succeeded = response.IsSuccessStatusCode;
            var transient = response.StatusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                || statusCode >= 500;
            return new WebhookDeliveryAttemptResult(
                startedAt,
                statusCode,
                succeeded,
                transient,
                succeeded ? null : $"Endpoint returned HTTP {statusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Webhook delivery {DeliveryId} to subscription {SubscriptionId} failed before receiving a response",
                workItem.Id,
                workItem.SubscriptionId);
            return new WebhookDeliveryAttemptResult(
                startedAt,
                null,
                false,
                true,
                "Endpoint request failed before a response was received.");
        }
    }
}

public sealed class WebhookDeliveryProcessor(
    IWebhookStore store,
    IWebhookDeliveryTransport transport,
    IOptions<WebhookDeliveryOptions> options,
    TimeProvider timeProvider) : IWebhookDeliveryProcessor
{
    private static readonly TimeSpan DeliveryLease = TimeSpan.FromMinutes(2);

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var deliveryId = await store.ClaimNextDeliveryAsync(now, now.Add(DeliveryLease), cancellationToken);
        if (deliveryId is null)
        {
            return false;
        }

        var workItem = await store.GetDeliveryAsync(deliveryId.Value, cancellationToken)
            ?? throw new InvalidOperationException("Claimed webhook delivery could not be loaded.");
        var attempt = await transport.SendAsync(workItem, cancellationToken);
        var completedAt = timeProvider.GetUtcNow();
        var willRetry = !attempt.Succeeded
            && attempt.IsTransient
            && workItem.AttemptNumber < WebhookDeliveryStatuses.MaximumAttempts;
        DateTimeOffset? nextAttemptAt = willRetry
            ? completedAt.Add(GetRetryDelay(workItem.AttemptNumber, options.Value))
            : null;

        await store.CompleteAttemptAsync(
            workItem,
            attempt,
            completedAt,
            nextAttemptAt,
            WebhookDeliveryStatuses.MaximumAttempts,
            cancellationToken);
        return true;
    }

    private static TimeSpan GetRetryDelay(int attemptNumber, WebhookDeliveryOptions options)
    {
        var exponentialSeconds = options.InitialRetryDelaySeconds * Math.Pow(2, Math.Max(0, attemptNumber - 1));
        var seconds = Math.Clamp(exponentialSeconds, 0, options.MaximumRetryDelaySeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}

public sealed class WebhookDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryProcessor>();
                processed = await processor.ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Webhook delivery worker encountered an error.");
            }

            if (!processed)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
