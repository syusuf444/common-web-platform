using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Infrastructure;

public sealed class TenantRolePermissionConfiguration : IEntityTypeConfiguration<TenantRolePermission>
{
    public void Configure(EntityTypeBuilder<TenantRolePermission> builder)
    {
        builder.ToTable("TenantRolePermissions", "platform");
        builder.HasKey(item => new { item.TenantId, item.RoleId, item.PermissionCode });
        builder.Property(item => item.PermissionCode).HasMaxLength(80).IsRequired();
        builder.HasOne<TenantRole>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.RoleId })
            .HasPrincipalKey(role => new { role.TenantId, role.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Permission>()
            .WithMany()
            .HasForeignKey(item => item.PermissionCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
