using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Modules.Tenancy.Infrastructure;

public sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("Memberships", "platform");
        builder.HasKey(membership => membership.Id);
        builder.HasAlternateKey(membership => new { membership.TenantId, membership.Id });
        builder.Property(membership => membership.Status).HasMaxLength(32).IsRequired();
        builder.Property(membership => membership.JoinedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(membership => membership.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(membership => new { membership.TenantId, membership.UserId }).IsUnique();
        builder.HasIndex(membership => new { membership.UserId, membership.Status });
    }
}
