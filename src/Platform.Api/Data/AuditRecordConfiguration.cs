using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Platform.Api.Data;

public sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("AuditRecords", "platform");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Action).HasMaxLength(100).IsRequired();
        builder.Property(record => record.ResourceType).HasMaxLength(100).IsRequired();
        builder.Property(record => record.ResourceId).HasMaxLength(128);
        builder.Property(record => record.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(record => record.MetadataJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(record => new { record.TenantId, record.OccurredAtUtc });
        builder.HasIndex(record => record.CorrelationId);
        builder.HasIndex(record => record.ActorUserId);
    }
}
