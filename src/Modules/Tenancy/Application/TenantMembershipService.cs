using Platform.Modules.Tenancy.Domain;

namespace Platform.Modules.Tenancy.Application;

public sealed record MembershipSummary(Guid Id, Guid UserId, string Status, DateTimeOffset JoinedAtUtc);

public sealed class TenantMembershipService(
    ITenantMembershipRepository memberships,
    ITenantContextAccessor tenantContext,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<MembershipSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var context = tenantContext.RequireCurrent();
        return await memberships.ListAsync(context.TenantId, cancellationToken);
    }

    public async Task<MembershipSummary?> GetAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        var context = tenantContext.RequireCurrent();
        return await memberships.GetAsync(context.TenantId, membershipId, cancellationToken);
    }

    public async Task<MembershipSummary> AddAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var context = tenantContext.RequireCurrent();
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A user ID is required.", nameof(userId));
        }

        return await memberships.AddAsync(context.TenantId, userId, timeProvider.GetUtcNow(), cancellationToken);
    }

    public async Task<bool> UpdateStatusAsync(Guid membershipId, MembershipStatus status, CancellationToken cancellationToken = default)
    {
        var context = tenantContext.RequireCurrent();
        if (!Enum.IsDefined(status))
        {
            return false;
        }

        return await memberships.UpdateStatusAsync(context.TenantId, membershipId, status, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        var context = tenantContext.RequireCurrent();
        return await memberships.DeleteAsync(context.TenantId, membershipId, cancellationToken);
    }
}
