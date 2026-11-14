using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Workflows.Domain;

namespace Platform.Api.Data;

public sealed class RoleAccessRequestConfiguration : IEntityTypeConfiguration<RoleAccessRequest>
{
    public void Configure(EntityTypeBuilder<RoleAccessRequest> builder)
    {
        builder.ToTable("RoleAccessRequests", "platform");
        builder.HasKey(request => new { request.TenantId, request.Id });
        builder.Property(request => request.Status).HasMaxLength(20).IsRequired();
        builder.Property(request => request.RequestedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(request => request.DecidedAtUtc).HasColumnType("datetimeoffset");
        builder.HasIndex(request => new { request.TenantId, request.RequesterUserId, request.RequestedAtUtc });
        builder.HasIndex(request => new { request.TenantId, request.Status, request.RequestedAtUtc });
        builder.HasIndex(request => new { request.TenantId, request.RequesterMembershipId, request.RoleId })
            .IsUnique()
            .HasFilter("[Status] = N'Pending'");
        builder.HasOne<TenantRole>()
            .WithMany()
            .HasForeignKey(request => new { request.TenantId, request.RoleId })
            .HasPrincipalKey(role => new { role.TenantId, role.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
