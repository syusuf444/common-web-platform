using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Application;

namespace Platform.Modules.Identity.Application;

public interface ITenantRoleRepository
{
    Task<IReadOnlyList<TenantRoleSummary>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<TenantRoleSummary> CreateAsync(Guid tenantId, string name, IReadOnlyList<string> permissionCodes, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default);

    Task<TenantRoleAssignmentResult> AssignAsync(Guid tenantId, Guid membershipId, Guid roleId, DateTimeOffset assignedAtUtc, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Guid tenantId, Guid membershipId, Guid roleId, CancellationToken cancellationToken = default);

    Task<bool> HasPermissionAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken cancellationToken = default);
}

public sealed class TenantRoleService(
    ITenantRoleRepository roles,
    ITenantContextAccessor tenantContext,
    TimeProvider timeProvider)
{
    public Task<IReadOnlyList<TenantRoleSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        return roles.ListAsync(tenantContext.RequireCurrent().TenantId, cancellationToken);
    }

    public Task<TenantRoleSummary> CreateAsync(CreateTenantRoleRequest request, CancellationToken cancellationToken = default)
    {
        var context = tenantContext.RequireCurrent();
        var name = request.Name.Trim();

        var permissionCodes = request.PermissionCodes
            .Select(code => code.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return roles.CreateAsync(context.TenantId, name, permissionCodes, timeProvider.GetUtcNow(), cancellationToken);
    }

    public Task<TenantRoleAssignmentResult> AssignAsync(Guid membershipId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var context = tenantContext.RequireCurrent();
        return roles.AssignAsync(context.TenantId, membershipId, roleId, timeProvider.GetUtcNow(), cancellationToken);
    }

    public Task<bool> RemoveAsync(Guid membershipId, Guid roleId, CancellationToken cancellationToken = default)
    {
        return roles.RemoveAsync(tenantContext.RequireCurrent().TenantId, membershipId, roleId, cancellationToken);
    }
}
