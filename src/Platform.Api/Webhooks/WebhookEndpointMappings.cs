using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Platform.Api.Auditing;
using Platform.Api.Identity;
using Platform.Modules.Tenancy.Application;
using Platform.Modules.Webhooks.Application;

namespace Platform.Api.Webhooks;

public static class WebhookEndpointMappings
{
    private const int MaximumOffset = 10000;
    private const int MaximumPageSize = 100;

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var webhooks = endpoints.MapGroup("/api/v1/tenant/webhooks")
            .WithTags("Tenant Webhooks");

        webhooks.MapGet("/subscriptions", async (
                ITenantContextAccessor tenantContext,
                IWebhookService service,
                CancellationToken cancellationToken) =>
            {
                var tenantId = tenantContext.RequireCurrent().TenantId;
                return Results.Ok(await service.ListSubscriptionsAsync(tenantId, cancellationToken));
            })
            .WithName("ListTenantWebhookSubscriptions")
            .WithSummary("List webhook subscriptions in the active tenant")
            .WithDescription("Signing secrets are never included in subscription list responses.")
            .RequireAuthorization(TenantPermissionPolicies.WebhooksRead)
            .Produces<IReadOnlyList<WebhookSubscriptionResponse>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        webhooks.MapGet("/subscriptions/{subscriptionId:guid}", async (
                Guid subscriptionId,
                ITenantContextAccessor tenantContext,
                IWebhookService service,
                CancellationToken cancellationToken) =>
            {
                var tenantId = tenantContext.RequireCurrent().TenantId;
                var subscription = await service.GetSubscriptionAsync(tenantId, subscriptionId, cancellationToken);
                return subscription is null ? Results.NotFound() : Results.Ok(subscription);
            })
            .WithName("GetTenantWebhookSubscription")
            .WithSummary("Get webhook subscription metadata in the active tenant")
            .RequireAuthorization(TenantPermissionPolicies.WebhooksRead)
            .Produces<WebhookSubscriptionResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        webhooks.MapPost("/subscriptions", async (
                CreateWebhookSubscriptionRequest request,
                HttpContext context,
                ITenantContextAccessor tenantContext,
                IWebhookService service,
                IAuditRecordService auditRecords,
                CancellationToken cancellationToken) =>
            {
                var errors = WebhookRequestValidator.Validate(request.EventType, request.EndpointUrl);
                if (errors.Count != 0)
                {
                    return Results.ValidationProblem(
                        errors,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The webhook subscription request is invalid.",
                        extensions: ErrorExtensions(context));
                }

                var tenantId = tenantContext.RequireCurrent().TenantId;
                CreatedWebhookSubscriptionResponse subscription;
                try
                {
                    subscription = await service.CreateSubscriptionAsync(tenantId, request, cancellationToken);
                }
                catch (WebhookRequestValidationException exception)
                {
                    return Results.ValidationProblem(
                        exception.Errors,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The webhook subscription request is invalid.",
                        extensions: ErrorExtensions(context));
                }

                await auditRecords.RecordAsync(
                    new AuditEvent("tenant.webhook.subscription.created", "webhook_subscription", subscription.Id.ToString()),
                    cancellationToken);
                return Results.Created(
                    $"/api/v1/tenant/webhooks/subscriptions/{subscription.Id}",
                    subscription);
            })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("CreateTenantWebhookSubscription")
            .WithSummary("Subscribe an active tenant to an event type")
            .WithDescription("The generated signing secret is returned only once. The endpoint host must be configured in Webhooks:AllowedHosts.")
            .RequireAuthorization(TenantPermissionPolicies.WebhooksManage)
            .Produces<CreatedWebhookSubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        webhooks.MapDelete("/subscriptions/{subscriptionId:guid}", async (
                Guid subscriptionId,
                ITenantContextAccessor tenantContext,
                IWebhookService service,
                IAuditRecordService auditRecords,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var tenantId = tenantContext.RequireCurrent().TenantId;
                var disabled = await service.DisableSubscriptionAsync(
                    tenantId,
                    subscriptionId,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                if (!disabled)
                {
                    return Results.NotFound();
                }

                await auditRecords.RecordAsync(
                    new AuditEvent("tenant.webhook.subscription.disabled", "webhook_subscription", subscriptionId.ToString()),
                    cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("DisableTenantWebhookSubscription")
            .WithSummary("Disable a webhook subscription in the active tenant")
            .RequireAuthorization(TenantPermissionPolicies.WebhooksManage)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        webhooks.MapGet("/deliveries", async (
                int? skip,
                int? take,
                HttpContext context,
                ITenantContextAccessor tenantContext,
                IWebhookService service,
                CancellationToken cancellationToken) =>
            {
                var offset = skip ?? 0;
                var pageSize = take ?? 50;
                if (offset is < 0 or > MaximumOffset || pageSize is < 1 or > MaximumPageSize)
                {
                    var errors = new Dictionary<string, string[]>();
                    if (offset is < 0 or > MaximumOffset)
                    {
                        errors[nameof(skip)] = [$"Skip must be between 0 and {MaximumOffset}."];
                    }

                    if (pageSize is < 1 or > MaximumPageSize)
                    {
                        errors[nameof(take)] = [$"Take must be between 1 and {MaximumPageSize}."];
                    }

                    return Results.ValidationProblem(
                        errors,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The webhook delivery page request is invalid.",
                        extensions: ErrorExtensions(context));
                }

                var tenantId = tenantContext.RequireCurrent().TenantId;
                return Results.Ok(await service.ListDeliveriesAsync(
                    tenantId,
                    offset,
                    pageSize,
                    cancellationToken));
            })
            .WithName("ListTenantWebhookDeliveries")
            .WithSummary("List webhook deliveries and their attempt history in the active tenant")
            .RequireAuthorization(TenantPermissionPolicies.WebhooksRead)
            .Produces<WebhookDeliveryListResponse>()
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
