using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Files.Domain;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Api.Data;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : IdentityUserContext<PlatformUser, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();

    public DbSet<TenantRole> TenantRoles => Set<TenantRole>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<TenantRolePermission> TenantRolePermissions => Set<TenantRolePermission>();

    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();

    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();

    public DbSet<TenantFile> TenantFiles => Set<TenantFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Tenant).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformUser).Assembly);
        modelBuilder.ApplyConfiguration(new AuditRecordConfiguration());
        modelBuilder.ApplyConfiguration(new TenantSettingConfiguration());
        modelBuilder.ApplyConfiguration(new TenantFileConfiguration());
        modelBuilder.Entity<TenantMembership>()
            .HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
