using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Modules.Identity.Infrastructure;

public sealed class TenantRoleConfiguration : IEntityTypeConfiguration<TenantRole>
{
    public void Configure(EntityTypeBuilder<TenantRole> builder)
    {
        builder.ToTable("TenantRoles", "platform");
        builder.HasKey(role => role.Id);
        builder.HasAlternateKey(role => new { role.TenantId, role.Id });
        builder.Property(role => role.Name).HasMaxLength(100).IsRequired();
        builder.Property(role => role.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.HasIndex(role => new { role.TenantId, role.Name }).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(role => role.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
