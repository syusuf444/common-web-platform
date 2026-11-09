using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Api.Data;

public sealed class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting>
{
    public void Configure(EntityTypeBuilder<TenantSetting> builder)
    {
        builder.ToTable("TenantSettings", "platform");
        builder.HasKey(setting => new { setting.TenantId, setting.Key });
        builder.Property(setting => setting.Key).HasMaxLength(100).IsRequired();
        builder.Property(setting => setting.Value).HasMaxLength(2000).IsRequired();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(setting => setting.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
