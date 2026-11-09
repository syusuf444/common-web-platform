namespace Platform.Modules.Identity.Domain;

public sealed class TenantRolePermission
{
    public Guid TenantId { get; set; }

    public Guid RoleId { get; set; }

    public string PermissionCode { get; set; } = string.Empty;
}
