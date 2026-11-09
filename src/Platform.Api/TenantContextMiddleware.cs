using Microsoft.AspNetCore.Mvc;
using Platform.Modules.Tenancy.Application;

namespace Platform.Api;

public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ITenantContextResolver resolver,
        ITenantContextAccessor tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.Request.Headers.TryGetValue("X-Tenant-ID", out var selectedTenantHeader))
        {
            if (!Guid.TryParse(selectedTenantHeader, out var selectedTenantId) || selectedTenantId == Guid.Empty)
            {
                await WriteAccessDeniedAsync(context);
                return;
            }

            var resolvedContext = await resolver.ResolveAsync(context.User, selectedTenantId, context.RequestAborted);
            if (resolvedContext is null)
            {
                await WriteAccessDeniedAsync(context);
                return;
            }

            tenantContext.SetCurrent(resolvedContext);
        }

        await next(context);
    }

    private static Task WriteAccessDeniedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Tenant access denied.",
            Extensions =
            {
                ["code"] = "tenant_access_denied",
                ["correlationId"] = context.Items[CorrelationId.ItemKey],
            },
        });
    }
}
