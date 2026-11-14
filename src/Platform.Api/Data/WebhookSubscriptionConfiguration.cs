using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Tenancy.Domain;
using Platform.Modules.Webhooks.Domain;

namespace Platform.Api.Data;

public sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.ToTable("WebhookSubscriptions", "platform");
        builder.HasKey(subscription => new { subscription.TenantId, subscription.Id });
        builder.Property(subscription => subscription.EventType).HasMaxLength(100).IsRequired();
        builder.Property(subscription => subscription.EndpointUrl).HasMaxLength(2048).IsRequired();
        builder.Property(subscription => subscription.ProtectedSigningSecret).HasMaxLength(4096).IsRequired();
        builder.Property(subscription => subscription.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(subscription => subscription.DisabledAtUtc).HasColumnType("datetimeoffset");
        builder.HasIndex(subscription => new { subscription.TenantId, subscription.EventType, subscription.IsActive });
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(subscription => subscription.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
