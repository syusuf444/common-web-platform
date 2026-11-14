using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Infrastructure;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions", "platform");
        builder.HasKey(permission => permission.Code);
        builder.Property(permission => permission.Code).HasMaxLength(80).IsRequired();
        builder.Property(permission => permission.Description).HasMaxLength(200).IsRequired();
        builder.HasData(
            new Permission { Code = PermissionCodes.MembersRead, Description = "Read tenant members." },
            new Permission { Code = PermissionCodes.MembersManage, Description = "Manage tenant members." },
            new Permission { Code = PermissionCodes.RolesManage, Description = "Manage tenant roles and assignments." },
            new Permission { Code = PermissionCodes.AuditRead, Description = "Read tenant security and administration audit records." },
            new Permission { Code = PermissionCodes.SettingsRead, Description = "Read non-secret tenant settings." },
            new Permission { Code = PermissionCodes.SettingsManage, Description = "Create, update, and delete non-secret tenant settings." },
            new Permission { Code = PermissionCodes.NotificationsRead, Description = "Read notifications in the tenant outbox." },
            new Permission { Code = PermissionCodes.NotificationsSend, Description = "Write notifications to the tenant outbox." },
            new Permission { Code = PermissionCodes.FilesRead, Description = "Read metadata and download files in the tenant." },
            new Permission { Code = PermissionCodes.FilesUpload, Description = "Upload files to the tenant." },
            new Permission { Code = PermissionCodes.ReportsRead, Description = "Read tenant dashboard and report data." },
            new Permission { Code = PermissionCodes.ReportsExport, Description = "Export tenant report data." },
            new Permission { Code = PermissionCodes.WebhooksRead, Description = "Read tenant webhook subscriptions and delivery attempts." },
            new Permission { Code = PermissionCodes.WebhooksManage, Description = "Create and disable tenant webhook subscriptions." });
    }
}
