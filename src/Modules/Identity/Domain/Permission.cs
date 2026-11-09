namespace Platform.Modules.Identity.Domain;

public sealed class Permission
{
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

public static class PermissionCodes
{
    public const string MembersRead = "members.read";
    public const string MembersManage = "members.manage";
    public const string RolesManage = "roles.manage";
    public const string AuditRead = "audit.read";
    public const string SettingsRead = "settings.read";
    public const string SettingsManage = "settings.manage";

    public static IReadOnlyList<string> All { get; } =
        [MembersRead, MembersManage, RolesManage, AuditRead, SettingsRead, SettingsManage];
}
