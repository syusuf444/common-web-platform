using Microsoft.EntityFrameworkCore;
using Platform.Modules.Webhooks.Application;
using Platform.Modules.Webhooks.Domain;

namespace Platform.Api.Data;

public sealed class EfWebhookStore(PlatformDbContext database) : IWebhookStore
{
    public async Task AddSubscriptionAsync(
        Guid tenantId,
        Guid subscriptionId,
        string eventType,
        string endpointUrl,
        string protectedSigningSecret,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        database.WebhookSubscriptions.Add(new WebhookSubscription
        {
            Id = subscriptionId,
            TenantId = tenantId,
            EventType = eventType,
            EndpointUrl = endpointUrl,
            ProtectedSigningSecret = protectedSigningSecret,
            IsActive = true,
            CreatedAtUtc = createdAtUtc,
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookSubscriptionResponse>> ListSubscriptionsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return await database.WebhookSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.TenantId == tenantId)
            .OrderBy(subscription => subscription.CreatedAtUtc)
            .Select(subscription => new WebhookSubscriptionResponse(
                subscription.Id,
                subscription.EventType,
                subscription.EndpointUrl,
                subscription.IsActive,
                subscription.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<WebhookSubscriptionResponse?> GetSubscriptionAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        return database.WebhookSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.TenantId == tenantId && subscription.Id == subscriptionId)
            .Select(subscription => new WebhookSubscriptionResponse(
                subscription.Id,
                subscription.EventType,
                subscription.EndpointUrl,
                subscription.IsActive,
                subscription.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> DisableSubscriptionAsync(
        Guid tenantId,
        Guid subscriptionId,
        DateTimeOffset disabledAtUtc,
        CancellationToken cancellationToken)
    {
        var updated = await database.WebhookSubscriptions
            .Where(subscription => subscription.TenantId == tenantId
                && subscription.Id == subscriptionId
                && subscription.IsActive)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(subscription => subscription.IsActive, false)
                    .SetProperty(subscription => subscription.DisabledAtUtc, disabledAtUtc),
                cancellationToken);
        return updated == 1;
    }

    public async Task<int> EnqueueAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        var subscriptionIds = await database.WebhookSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.TenantId == webhookEvent.TenantId
                && subscription.IsActive
                && subscription.EventType == webhookEvent.EventType)
            .Select(subscription => subscription.Id)
            .ToListAsync(cancellationToken);

        foreach (var subscriptionId in subscriptionIds)
        {
            database.WebhookDeliveries.Add(new WebhookDelivery
            {
                Id = Guid.NewGuid(),
                TenantId = webhookEvent.TenantId,
                SubscriptionId = subscriptionId,
                EventType = webhookEvent.EventType,
                PayloadJson = webhookEvent.PayloadJson,
                Status = WebhookDeliveryStatuses.Pending,
                AttemptCount = 0,
                CreatedAtUtc = webhookEvent.OccurredAtUtc,
                NextAttemptAtUtc = webhookEvent.OccurredAtUtc,
            });
        }

        await database.SaveChangesAsync(cancellationToken);
        return subscriptionIds.Count;
    }

    public async Task<Guid?> ClaimNextDeliveryAsync(
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        await database.WebhookDeliveries
            .Where(delivery => delivery.Status == WebhookDeliveryStatuses.Processing
                && delivery.AttemptCount >= WebhookDeliveryStatuses.MaximumAttempts
                && delivery.LeaseExpiresAtUtc <= now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(delivery => delivery.Status, WebhookDeliveryStatuses.Failed)
                    .SetProperty(delivery => delivery.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(delivery => delivery.CompletedAtUtc, now)
                    .SetProperty(delivery => delivery.NextAttemptAtUtc, now)
                    .SetProperty(delivery => delivery.LastError, "Delivery lease expired before the final attempt completed."),
                cancellationToken);

        var candidate = await database.WebhookDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.AttemptCount < WebhookDeliveryStatuses.MaximumAttempts
                && ((delivery.Status == WebhookDeliveryStatuses.Pending && delivery.NextAttemptAtUtc <= now)
                    || (delivery.Status == WebhookDeliveryStatuses.Processing && delivery.LeaseExpiresAtUtc <= now)))
            .OrderBy(delivery => delivery.NextAttemptAtUtc)
            .ThenBy(delivery => delivery.Id)
            .Select(delivery => new { delivery.TenantId, delivery.Id, delivery.Status, delivery.AttemptCount })
            .FirstOrDefaultAsync(cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        var claimed = await database.WebhookDeliveries
            .Where(delivery => delivery.TenantId == candidate.TenantId
                && delivery.Id == candidate.Id
                && delivery.AttemptCount == candidate.AttemptCount
                && ((delivery.Status == WebhookDeliveryStatuses.Pending && delivery.NextAttemptAtUtc <= now)
                    || (delivery.Status == WebhookDeliveryStatuses.Processing && delivery.LeaseExpiresAtUtc <= now)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(delivery => delivery.Status, WebhookDeliveryStatuses.Processing)
                    .SetProperty(delivery => delivery.AttemptCount, delivery => delivery.AttemptCount + 1)
                    .SetProperty(delivery => delivery.LeaseExpiresAtUtc, leaseExpiresAtUtc),
                cancellationToken);

        return claimed == 1 ? candidate.Id : null;
    }

    public Task<WebhookDeliveryWorkItem?> GetDeliveryAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        return (from delivery in database.WebhookDeliveries.AsNoTracking()
                join subscription in database.WebhookSubscriptions.AsNoTracking()
                    on new { delivery.TenantId, delivery.SubscriptionId }
                    equals new { subscription.TenantId, SubscriptionId = subscription.Id }
                where delivery.Id == deliveryId
                select new WebhookDeliveryWorkItem(
                    delivery.Id,
                    delivery.TenantId,
                    delivery.SubscriptionId,
                    subscription.EndpointUrl,
                    subscription.ProtectedSigningSecret,
                    subscription.IsActive,
                    delivery.EventType,
                    delivery.PayloadJson,
                    delivery.AttemptCount))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task CompleteAttemptAsync(
        WebhookDeliveryWorkItem workItem,
        WebhookDeliveryAttemptResult attempt,
        DateTimeOffset completedAtUtc,
        DateTimeOffset? nextAttemptAtUtc,
        int maximumAttempts,
        CancellationToken cancellationToken)
    {
        var status = attempt.Succeeded
            ? WebhookDeliveryStatuses.Succeeded
            : attempt.IsTransient && workItem.AttemptNumber < maximumAttempts
                ? WebhookDeliveryStatuses.Pending
                : WebhookDeliveryStatuses.Failed;
        var completed = status is WebhookDeliveryStatuses.Succeeded or WebhookDeliveryStatuses.Failed;

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var updated = await database.WebhookDeliveries
            .Where(delivery => delivery.TenantId == workItem.TenantId
                && delivery.Id == workItem.Id
                && delivery.Status == WebhookDeliveryStatuses.Processing
                && delivery.AttemptCount == workItem.AttemptNumber)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(delivery => delivery.Status, status)
                    .SetProperty(delivery => delivery.LastStatusCode, attempt.HttpStatusCode)
                    .SetProperty(delivery => delivery.LastError, attempt.Error)
                    .SetProperty(delivery => delivery.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(delivery => delivery.NextAttemptAtUtc, nextAttemptAtUtc ?? completedAtUtc)
                    .SetProperty(delivery => delivery.CompletedAtUtc, completed ? completedAtUtc : (DateTimeOffset?)null),
                cancellationToken);
        if (updated == 1)
        {
            database.WebhookDeliveryAttempts.Add(new WebhookDeliveryAttempt
            {
                Id = Guid.NewGuid(),
                TenantId = workItem.TenantId,
                DeliveryId = workItem.Id,
                AttemptNumber = workItem.AttemptNumber,
                StartedAtUtc = attempt.StartedAtUtc,
                CompletedAtUtc = completedAtUtc,
                HttpStatusCode = attempt.HttpStatusCode,
                Succeeded = attempt.Succeeded,
                Error = attempt.Error,
            });
            await database.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<WebhookDeliveryListResponse> ListDeliveriesAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await database.WebhookDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.TenantId == tenantId)
            .OrderByDescending(delivery => delivery.CreatedAtUtc)
            .ThenByDescending(delivery => delivery.Id)
            .Skip(skip)
            .Take(take + 1)
            .Select(delivery => new
            {
                delivery.Id,
                delivery.SubscriptionId,
                delivery.EventType,
                delivery.Status,
                delivery.AttemptCount,
                delivery.CreatedAtUtc,
                delivery.CompletedAtUtc,
                delivery.LastStatusCode,
                delivery.LastError,
            })
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > take;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var deliveryIds = rows.Select(delivery => delivery.Id).ToArray();
        var attempts = await database.WebhookDeliveryAttempts
            .AsNoTracking()
            .Where(attempt => attempt.TenantId == tenantId && deliveryIds.Contains(attempt.DeliveryId))
            .OrderBy(attempt => attempt.AttemptNumber)
            .Select(attempt => new
            {
                attempt.DeliveryId,
                Response = new WebhookDeliveryAttemptResponse(
                    attempt.AttemptNumber,
                    attempt.StartedAtUtc,
                    attempt.CompletedAtUtc,
                    attempt.HttpStatusCode,
                    attempt.Succeeded,
                    attempt.Error),
            })
            .ToListAsync(cancellationToken);
        var attemptsByDelivery = attempts.GroupBy(attempt => attempt.DeliveryId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<WebhookDeliveryAttemptResponse>)group.Select(item => item.Response).ToArray());
        var items = rows.Select(delivery => new WebhookDeliveryResponse(
            delivery.Id,
            delivery.SubscriptionId,
            delivery.EventType,
            delivery.Status,
            delivery.AttemptCount,
            delivery.CreatedAtUtc,
            delivery.CompletedAtUtc,
            delivery.LastStatusCode,
            delivery.LastError,
            attemptsByDelivery.GetValueOrDefault(delivery.Id, []))).ToArray();

        return new WebhookDeliveryListResponse(items, skip, take, hasMore);
    }
}
