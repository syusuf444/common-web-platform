using System.Security.Claims;
using Platform.Modules.Tenancy.Application;
using Platform.Modules.Tenancy.Domain;

namespace Platform.UnitTests;

public sealed class TenantContextTests
{
    [Fact]
    public async Task ResolverReturnsContextOnlyForAuthenticatedActiveMembership()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var repository = new MembershipRepository(userId, tenantId, isActive: true);
        var resolver = new TenantContextResolver(repository);
        var principal = CreatePrincipal(userId);

        var context = await resolver.ResolveAsync(principal, tenantId);

        Assert.Equal(new TenantContext(userId, tenantId), context);
        Assert.Null(await resolver.ResolveAsync(principal, Guid.NewGuid()));
        Assert.Null(await resolver.ResolveAsync(CreatePrincipal(userId, authenticated: false), tenantId));
        Assert.Null(await resolver.ResolveAsync(CreatePrincipal(userId), Guid.Empty));
    }

    [Fact]
    public async Task ResolverRejectsSuspendedMembershipAndInvalidUserClaim()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var resolver = new TenantContextResolver(new MembershipRepository(userId, tenantId, isActive: false));

        Assert.Null(await resolver.ResolveAsync(CreatePrincipal(userId), tenantId));
        var invalidClaimPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "not-a-guid")],
            "unit-test"));
        Assert.Null(await resolver.ResolveAsync(invalidClaimPrincipal, tenantId));
    }

    [Fact]
    public void AccessorRequiresContextBeforeTenantOperations()
    {
        var accessor = new TenantContextAccessor();

        Assert.Throws<TenantContextRequiredException>(accessor.RequireCurrent);

        var context = new TenantContext(Guid.NewGuid(), Guid.NewGuid());
        accessor.SetCurrent(context);
        Assert.Equal(context, accessor.RequireCurrent());
    }

    private static ClaimsPrincipal CreatePrincipal(Guid userId, bool authenticated = true)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            authenticated ? "unit-test" : null);
        return new ClaimsPrincipal(identity);
    }

    private sealed class MembershipRepository(Guid userId, Guid tenantId, bool isActive) : ITenantMembershipRepository
    {
        public Task<bool> HasActiveMembershipAsync(Guid selectedTenantId, Guid selectedUserId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(isActive && selectedTenantId == tenantId && selectedUserId == userId);
        }

        public Task<IReadOnlyList<MembershipSummary>> ListAsync(Guid selectedTenantId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<MembershipSummary?> GetAsync(Guid selectedTenantId, Guid membershipId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<MembershipSummary> AddAsync(Guid selectedTenantId, Guid selectedUserId, DateTimeOffset joinedAtUtc, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> UpdateStatusAsync(Guid selectedTenantId, Guid membershipId, MembershipStatus status, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteAsync(Guid selectedTenantId, Guid membershipId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
