using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Webhooks.Domain;

namespace Platform.Api.Data;

public sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("WebhookDeliveries", "platform");
        builder.HasKey(delivery => new { delivery.TenantId, delivery.Id });
        builder.Property(delivery => delivery.EventType).HasMaxLength(100).IsRequired();
        builder.Property(delivery => delivery.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(delivery => delivery.Status).HasMaxLength(32).IsRequired();
        builder.Property(delivery => delivery.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(delivery => delivery.NextAttemptAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(delivery => delivery.LeaseExpiresAtUtc).HasColumnType("datetimeoffset");
        builder.Property(delivery => delivery.CompletedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(delivery => delivery.LastError).HasMaxLength(256);
        builder.HasIndex(delivery => new { delivery.Status, delivery.NextAttemptAtUtc, delivery.LeaseExpiresAtUtc });
        builder.HasIndex(delivery => new { delivery.TenantId, delivery.CreatedAtUtc });
        builder.HasOne<WebhookSubscription>()
            .WithMany()
            .HasForeignKey(delivery => new { delivery.TenantId, delivery.SubscriptionId })
            .HasPrincipalKey(subscription => new { subscription.TenantId, subscription.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
