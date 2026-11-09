namespace Platform.Modules.Notifications.Application;

public sealed class NotificationService(
    INotificationDeliveryPort delivery,
    INotificationOutboxReader outboxReader,
    TimeProvider timeProvider) : INotificationService
{
    public async Task<NotificationDeliveryReceipt> SendAsync(
        Guid tenantId,
        SendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var errors = NotificationRequestValidator.Validate(request);
        if (errors.Count != 0)
        {
            throw new NotificationRequestValidationException(errors);
        }

        var message = new NotificationMessage(
            Guid.NewGuid(),
            tenantId,
            request.Recipient.Trim(),
            request.Subject.Trim(),
            request.Body,
            timeProvider.GetUtcNow());

        await delivery.DeliverAsync(message, cancellationToken);
        return new NotificationDeliveryReceipt(message.Id, "WrittenToLocalOutbox", timeProvider.GetUtcNow());
    }

    public Task<IReadOnlyList<NotificationOutboxItem>> ListAsync(
        Guid tenantId,
        int take,
        CancellationToken cancellationToken)
    {
        return outboxReader.ListAsync(tenantId, take, cancellationToken);
    }

    public Task<NotificationOutboxItem?> GetAsync(
        Guid tenantId,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        return outboxReader.GetAsync(tenantId, notificationId, cancellationToken);
    }
}

public sealed class NotificationRequestValidationException(
    IReadOnlyDictionary<string, string[]> errors) : Exception("The notification request is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
