namespace Platform.Modules.Identity.Application;

public sealed record CreateTenantRoleRequest(string Name, IReadOnlyList<string> PermissionCodes);

public sealed record TenantRoleSummary(Guid Id, string Name, bool IsSystem, IReadOnlyList<string> PermissionCodes);

public enum TenantRoleAssignmentResult
{
    Assigned,
    AlreadyAssigned,
    NotFound,
}
