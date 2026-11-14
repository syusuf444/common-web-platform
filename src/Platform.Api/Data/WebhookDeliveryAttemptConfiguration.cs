using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Webhooks.Domain;

namespace Platform.Api.Data;

public sealed class WebhookDeliveryAttemptConfiguration : IEntityTypeConfiguration<WebhookDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<WebhookDeliveryAttempt> builder)
    {
        builder.ToTable("WebhookDeliveryAttempts", "platform");
        builder.HasKey(attempt => new { attempt.TenantId, attempt.Id });
        builder.Property(attempt => attempt.StartedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(attempt => attempt.CompletedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(attempt => attempt.Error).HasMaxLength(256);
        builder.HasIndex(attempt => new { attempt.TenantId, attempt.DeliveryId, attempt.AttemptNumber }).IsUnique();
        builder.HasOne<WebhookDelivery>()
            .WithMany()
            .HasForeignKey(attempt => new { attempt.TenantId, attempt.DeliveryId })
            .HasPrincipalKey(delivery => new { delivery.TenantId, delivery.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
