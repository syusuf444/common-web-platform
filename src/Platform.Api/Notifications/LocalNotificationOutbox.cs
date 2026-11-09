using System.Text.Json;
using Microsoft.Extensions.Options;
using Platform.Modules.Notifications.Application;

namespace Platform.Api.Notifications;

public sealed class LocalNotificationOutboxOptions
{
    public string DirectoryPath { get; set; } = string.Empty;
}

public sealed class LocalNotificationOutbox(
    IOptions<LocalNotificationOutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<LocalNotificationOutbox> logger) : INotificationDeliveryPort, INotificationOutboxReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directoryPath = ValidateDirectoryPath(options.Value.DirectoryPath);

    public async Task DeliverAsync(NotificationMessage notification, CancellationToken cancellationToken)
    {
        var tenantDirectory = Path.Combine(_directoryPath, notification.TenantId.ToString("N"));
        var filePath = Path.Combine(tenantDirectory, $"{notification.Id:N}.json");
        var temporaryPath = Path.Combine(tenantDirectory, $".{notification.Id:N}.{Guid.NewGuid():N}.tmp");
        var outboxItem = new NotificationOutboxItem(
            notification.Id,
            notification.TenantId,
            notification.Recipient,
            notification.Subject,
            notification.Body,
            timeProvider.GetUtcNow());

        try
        {
            Directory.CreateDirectory(tenantDirectory);
            var contents = JsonSerializer.SerializeToUtf8Bytes(outboxItem, SerializerOptions);
            await using (var stream = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Mode = FileMode.CreateNew,
                                 Access = FileAccess.Write,
                                 Share = FileShare.None,
                                 Options = FileOptions.Asynchronous,
                             }))
            {
                await stream.WriteAsync(contents, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, filePath);
            logger.LogInformation(
                "Local notification {NotificationId} for tenant {TenantId} was written to {OutboxPath}.",
                notification.Id,
                notification.TenantId,
                filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                exception,
                "Local notification {NotificationId} for tenant {TenantId} could not be written to the outbox.",
                notification.Id,
                notification.TenantId);
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogError(
                        exception,
                        "Temporary outbox file for notification {NotificationId} could not be removed.",
                        notification.Id);
                }
            }
        }
    }

    public async Task<IReadOnlyList<NotificationOutboxItem>> ListAsync(
        Guid tenantId,
        int take,
        CancellationToken cancellationToken)
    {
        if (take is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(take), "Take must be between 1 and 100.");
        }

        var tenantDirectory = Path.Combine(_directoryPath, tenantId.ToString("N"));
        string[] paths;
        try
        {
            paths = Directory.EnumerateFiles(tenantDirectory, "*.json").ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "The local notification outbox for tenant {TenantId} could not be read.", tenantId);
            throw;
        }

        try
        {
            var orderedPaths = paths
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(take);
            var items = new List<NotificationOutboxItem>();
            foreach (var path in orderedPaths)
            {
                var item = await ReadOutboxItemAsync(path, cancellationToken);
                if (item.TenantId == tenantId)
                {
                    items.Add(item);
                }
            }

            return items;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(exception, "The local notification outbox for tenant {TenantId} could not be read.", tenantId);
            throw;
        }
    }

    public async Task<NotificationOutboxItem?> GetAsync(
        Guid tenantId,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_directoryPath, tenantId.ToString("N"), $"{notificationId:N}.json");
        try
        {
            var item = await ReadOutboxItemAsync(filePath, cancellationToken);
            return item.TenantId == tenantId ? item : null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(
                exception,
                "Local notification {NotificationId} for tenant {TenantId} could not be read.",
                notificationId,
                tenantId);
            throw;
        }
    }

    private static async Task<NotificationOutboxItem> ReadOutboxItemAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        var item = await JsonSerializer.DeserializeAsync<NotificationOutboxItem>(
            stream,
            SerializerOptions,
            cancellationToken);
        return item ?? throw new JsonException("The local notification outbox file contains an empty record.");
    }

    private static string ValidateDirectoryPath(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException("Notifications:LocalOutboxPath must be configured.");
        }

        return Path.GetFullPath(directoryPath);
    }
}
