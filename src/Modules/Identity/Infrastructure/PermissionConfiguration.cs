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
            new Permission { Code = PermissionCodes.SettingsManage, Description = "Create, update, and delete non-secret tenant settings." });
    }
}
