namespace Platform.Api.Identity;

public static class TenantPermissionPolicies
{
    public const string MembersRead = "tenant.members.read";
    public const string MembersManage = "tenant.members.manage";
    public const string RolesManage = "tenant.roles.manage";
    public const string AuditRead = "tenant.audit.read";
    public const string SettingsRead = "tenant.settings.read";
    public const string SettingsManage = "tenant.settings.manage";
    public const string NotificationsRead = "tenant.notifications.read";
    public const string NotificationsSend = "tenant.notifications.send";
    public const string FilesRead = "tenant.files.read";
    public const string FilesUpload = "tenant.files.upload";
    public const string ReportsRead = "tenant.reports.read";
    public const string ReportsExport = "tenant.reports.export";
    public const string WebhooksRead = "tenant.webhooks.read";
    public const string WebhooksManage = "tenant.webhooks.manage";
    public const string WorkflowsManage = "tenant.workflows.manage";
}
