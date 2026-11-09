using Microsoft.AspNetCore.Mvc;
using Platform.Api.Identity;
using Platform.Modules.Notifications.Application;
using Platform.Modules.Tenancy.Application;

namespace Platform.Api.Notifications;

public static class NotificationEndpointMappings
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/api/v1/tenant/notifications")
            .WithTags("Tenant Notifications");

        notifications.MapGet("", async (
                int? take,
                HttpContext context,
                ITenantContextAccessor tenantContext,
                INotificationService service,
                CancellationToken cancellationToken) =>
            {
                var pageSize = take ?? 50;
                if (pageSize is < 1 or > 100)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { [nameof(take)] = ["Take must be between 1 and 100."] },
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The notification outbox request is invalid.",
                        extensions: ErrorExtensions(context));
                }

                var tenantId = tenantContext.RequireCurrent().TenantId;
                return Results.Ok(await service.ListAsync(tenantId, pageSize, cancellationToken));
            })
            .WithName("ListTenantNotifications")
            .WithSummary("List recent notifications from the active tenant's local outbox")
            .WithDescription("Returns at most 100 local notifications. No external email provider is contacted.")
            .RequireAuthorization(TenantPermissionPolicies.NotificationsRead)
            .Produces<IReadOnlyList<NotificationOutboxItem>>()
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        notifications.MapGet("/{notificationId:guid}", async (
                Guid notificationId,
                ITenantContextAccessor tenantContext,
                INotificationService service,
                CancellationToken cancellationToken) =>
            {
                var tenantId = tenantContext.RequireCurrent().TenantId;
                var item = await service.GetAsync(tenantId, notificationId, cancellationToken);
                return item is null ? Results.NotFound() : Results.Ok(item);
            })
            .WithName("GetTenantNotification")
            .WithSummary("Read a notification from the active tenant's local outbox")
            .RequireAuthorization(TenantPermissionPolicies.NotificationsRead)
            .Produces<NotificationOutboxItem>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        notifications.MapPost("", async (
                SendNotificationRequest request,
                HttpContext context,
                ITenantContextAccessor tenantContext,
                INotificationService service,
                CancellationToken cancellationToken) =>
            {
                var errors = NotificationRequestValidator.Validate(request);
                if (errors.Count != 0)
                {
                    return Results.ValidationProblem(
                        errors,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The notification request is invalid.",
                        extensions: ErrorExtensions(context));
                }

                var tenantId = tenantContext.RequireCurrent().TenantId;
                var receipt = await service.SendAsync(tenantId, request, cancellationToken);
                return Results.Created($"/api/v1/tenant/notifications/{receipt.Id}", receipt);
            })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("SendTenantNotification")
            .WithSummary("Write a notification to the active tenant's local outbox")
            .WithDescription("This local adapter does not send email or contact a third-party service. Outbox write failures are logged and returned as an API error.")
            .RequireAuthorization(TenantPermissionPolicies.NotificationsSend)
            .Produces<NotificationDeliveryReceipt>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static Dictionary<string, object?> ErrorExtensions(HttpContext context)
    {
        return new Dictionary<string, object?>
        {
            ["code"] = "validation_failed",
            ["correlationId"] = context.Items[CorrelationId.ItemKey],
        };
    }
}
