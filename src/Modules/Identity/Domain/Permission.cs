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

    public static IReadOnlyList<string> All { get; } = [MembersRead, MembersManage, RolesManage];
}
