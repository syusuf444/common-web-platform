using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Data;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Application;
using Platform.Modules.Tenancy.Domain;
using System.Security.Claims;

namespace Platform.IntegrationTests;

public sealed class SqlServerPersistenceTests
{
    [Fact]
    public async Task MigrationCreatesTenantTableAndSupportsPersistence()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PlatformDb")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=CommonWebPlatform;Integrated Security=True;TrustServerCertificate=True";
        var isolatedConnection = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = $"CWP_Integration_{Guid.NewGuid():N}",
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(isolatedConnection)
            .Options;

        await using var database = new PlatformDbContext(options);
        try
        {
            await database.Database.MigrateAsync();

            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = "Integration Tenant",
                Slug = $"integration-{Guid.NewGuid():N}",
                Status = "Active",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            database.Tenants.Add(tenant);
            await database.SaveChangesAsync();

            var storedTenant = await database.Tenants.SingleAsync(item => item.Id == tenant.Id);
            Assert.Equal(tenant.Name, storedTenant.Name);
            Assert.Equal(tenant.Slug, storedTenant.Slug);
            Assert.Contains(await database.Database.GetAppliedMigrationsAsync(), migration => migration.Contains("InitialPlatformSchema", StringComparison.Ordinal));
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task MembershipServiceDeniesCrossTenantListReadUpdateDeleteAndGuessedIds()
    {
        var options = CreateIsolatedOptions();
        await using var database = new PlatformDbContext(options);
        try
        {
            await database.Database.MigrateAsync();

            var tenantA = CreateTenant("tenant-a");
            var tenantB = CreateTenant("tenant-b");
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();
            var membershipA = CreateMembership(tenantA.Id, userA);
            var membershipB = CreateMembership(tenantB.Id, userB);
            database.Tenants.AddRange(tenantA, tenantB);
            database.Users.AddRange(CreateUser(userA), CreateUser(userB));
            database.Memberships.AddRange(membershipA, membershipB);
            await database.SaveChangesAsync();

            var repository = new EfTenantMembershipRepository(database);
            var resolver = new TenantContextResolver(repository);
            var principalA = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userA.ToString())],
                authenticationType: "integration-test"));
            var contextA = await resolver.ResolveAsync(principalA, tenantA.Id);
            Assert.NotNull(contextA);
            Assert.Null(await resolver.ResolveAsync(principalA, tenantB.Id));
            Assert.Null(await resolver.ResolveAsync(new ClaimsPrincipal(new ClaimsIdentity()), tenantA.Id));
            Assert.Null(await resolver.ResolveAsync(principalA, Guid.NewGuid()));

            var accessor = new TenantContextAccessor();
            accessor.SetCurrent(contextA);
            var service = new TenantMembershipService(repository, accessor, TimeProvider.System);

            var listed = await service.ListAsync();
            Assert.Single(listed);
            Assert.Equal(membershipA.Id, Assert.Single(listed).Id);
            Assert.NotNull(await service.GetAsync(membershipA.Id));
            Assert.Null(await service.GetAsync(membershipB.Id));
            Assert.Null(await service.GetAsync(Guid.NewGuid()));
            Assert.False(await service.UpdateStatusAsync(membershipB.Id, MembershipStatus.Suspended));
            Assert.False(await service.DeleteAsync(membershipB.Id));

            var addedUser = Guid.NewGuid();
            database.Users.Add(CreateUser(addedUser));
            await database.SaveChangesAsync();
            var added = await service.AddAsync(addedUser);
            Assert.Equal(tenantA.Id, (await database.Memberships.SingleAsync(item => item.Id == added.Id)).TenantId);
            Assert.Equal(MembershipStatus.Active.ToString(), added.Status);
            Assert.Equal(MembershipStatus.Active.ToString(), (await database.Memberships.SingleAsync(item => item.Id == membershipB.Id)).Status);

            var missingContextService = new TenantMembershipService(repository, new TenantContextAccessor(), TimeProvider.System);
            await Assert.ThrowsAsync<TenantContextRequiredException>(() => missingContextService.ListAsync());
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }

    private static DbContextOptions<PlatformDbContext> CreateIsolatedOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PlatformDb")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=CommonWebPlatform;Integrated Security=True;TrustServerCertificate=True";
        var isolatedConnection = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = $"CWP_Integration_{Guid.NewGuid():N}",
        }.ConnectionString;

        return new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(isolatedConnection)
            .Options;
    }

    private static Tenant CreateTenant(string suffix)
    {
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = suffix,
            Slug = $"{suffix}-{Guid.NewGuid():N}",
            Status = "Active",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static TenantMembership CreateMembership(Guid tenantId, Guid userId)
    {
        return new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Status = MembershipStatus.Active.ToString(),
            JoinedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static PlatformUser CreateUser(Guid userId)
    {
        return new PlatformUser
        {
            Id = userId,
            UserName = $"{userId:N}@example.test",
            NormalizedUserName = $"{userId:N}@EXAMPLE.TEST",
            Email = $"{userId:N}@example.test",
            NormalizedEmail = $"{userId:N}@EXAMPLE.TEST",
            DisplayName = "Integration user",
            Status = "Active",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}
