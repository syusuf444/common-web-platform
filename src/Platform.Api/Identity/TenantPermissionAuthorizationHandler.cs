using Microsoft.AspNetCore.Authorization;
using Platform.Modules.Identity.Application;
using Platform.Modules.Tenancy.Application;

namespace Platform.Api.Identity;

public sealed record TenantPermissionRequirement(string PermissionCode) : IAuthorizationRequirement;

public sealed class TenantPermissionAuthorizationHandler(
    ITenantContextAccessor tenantContext,
    ITenantRoleRepository roles) : AuthorizationHandler<TenantPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantPermissionRequirement requirement)
    {
        var activeTenant = tenantContext.Current;
        if (activeTenant is null || context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (await roles.HasPermissionAsync(activeTenant.TenantId, activeTenant.UserId, requirement.PermissionCode))
        {
            context.Succeed(requirement);
        }
    }
}
