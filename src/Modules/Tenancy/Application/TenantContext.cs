using System.Security.Claims;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Modules.Tenancy.Application;

public sealed record TenantContext(Guid UserId, Guid TenantId);

public interface ITenantContextAccessor
{
    TenantContext? Current { get; }

    void SetCurrent(TenantContext context);

    TenantContext RequireCurrent();
}

public sealed class TenantContextAccessor : ITenantContextAccessor
{
    public TenantContext? Current { get; private set; }

    public void SetCurrent(TenantContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Current = context;
    }

    public TenantContext RequireCurrent()
    {
        return Current ?? throw new TenantContextRequiredException();
    }
}

public sealed class TenantContextRequiredException : Exception
{
    public TenantContextRequiredException()
        : base("A verified tenant context is required for this operation.")
    {
    }
}

public interface ITenantContextResolver
{
    Task<TenantContext?> ResolveAsync(ClaimsPrincipal principal, Guid selectedTenantId, CancellationToken cancellationToken = default);
}

public interface ITenantMembershipRepository
{
    Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MembershipSummary>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<MembershipSummary?> GetAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default);

    Task<MembershipSummary> AddAsync(Guid tenantId, Guid userId, DateTimeOffset joinedAtUtc, CancellationToken cancellationToken = default);

    Task<bool> UpdateStatusAsync(Guid tenantId, Guid membershipId, MembershipStatus status, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default);
}

public sealed class TenantContextResolver(ITenantMembershipRepository memberships) : ITenantContextResolver
{
    public async Task<TenantContext?> ResolveAsync(ClaimsPrincipal principal, Guid selectedTenantId, CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true || selectedTenantId == Guid.Empty)
        {
            return null;
        }

        var userIdText = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdText, out var userId))
        {
            return null;
        }

        var hasActiveMembership = await memberships.HasActiveMembershipAsync(selectedTenantId, userId, cancellationToken);

        return hasActiveMembership ? new TenantContext(userId, selectedTenantId) : null;
    }
}
