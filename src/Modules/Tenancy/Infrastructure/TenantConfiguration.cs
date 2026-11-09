using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Modules.Tenancy.Infrastructure;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants", "platform");
        builder.HasKey(tenant => tenant.Id);
        builder.Property(tenant => tenant.Name).HasMaxLength(200).IsRequired();
        builder.Property(tenant => tenant.Slug).HasMaxLength(100).IsRequired();
        builder.Property(tenant => tenant.Status).HasMaxLength(32).IsRequired();
        builder.Property(tenant => tenant.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.HasIndex(tenant => tenant.Slug).IsUnique();
    }
}
