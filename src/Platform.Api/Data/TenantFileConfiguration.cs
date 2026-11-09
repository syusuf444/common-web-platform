using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Files.Domain;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Api.Data;

public sealed class TenantFileConfiguration : IEntityTypeConfiguration<TenantFile>
{
    public void Configure(EntityTypeBuilder<TenantFile> builder)
    {
        builder.ToTable("TenantFiles", "platform");
        builder.HasKey(file => new { file.TenantId, file.Id });
        builder.Property(file => file.FileName).HasMaxLength(255).IsRequired();
        builder.Property(file => file.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(file => file.SizeBytes).IsRequired();
        builder.Property(file => file.UploadedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.HasIndex(file => new { file.TenantId, file.UploadedAtUtc });
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(file => file.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
