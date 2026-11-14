using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Files.Domain;
using Platform.Modules.Tenancy.Domain;
using Platform.Modules.Webhooks.Domain;
using Platform.Modules.Workflows.Domain;

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

    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    public DbSet<WebhookDeliveryAttempt> WebhookDeliveryAttempts => Set<WebhookDeliveryAttempt>();

    public DbSet<RoleAccessRequest> RoleAccessRequests => Set<RoleAccessRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Tenant).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformUser).Assembly);
        modelBuilder.ApplyConfiguration(new AuditRecordConfiguration());
        modelBuilder.ApplyConfiguration(new TenantSettingConfiguration());
        modelBuilder.ApplyConfiguration(new TenantFileConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookSubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookDeliveryConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookDeliveryAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new RoleAccessRequestConfiguration());
        modelBuilder.Entity<TenantMembership>()
            .HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
