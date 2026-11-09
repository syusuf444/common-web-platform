using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Infrastructure;

public sealed class PlatformUserConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> builder)
    {
        builder.Property(user => user.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(user => user.Status).HasMaxLength(32).IsRequired();
        builder.Property(user => user.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
    }
}
