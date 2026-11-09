using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Modules.Identity.Infrastructure;

public sealed class MembershipRoleConfiguration : IEntityTypeConfiguration<MembershipRole>
{
    public void Configure(EntityTypeBuilder<MembershipRole> builder)
    {
        builder.ToTable("MembershipRoles", "platform");
        builder.HasKey(item => new { item.TenantId, item.MembershipId, item.RoleId });
        builder.Property(item => item.AssignedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.HasOne<TenantMembership>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.MembershipId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TenantRole>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.RoleId })
            .HasPrincipalKey(role => new { role.TenantId, role.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
