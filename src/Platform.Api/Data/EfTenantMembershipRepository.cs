using Microsoft.EntityFrameworkCore;
using Platform.Modules.Tenancy.Application;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Api.Data;

public sealed class EfTenantMembershipRepository(PlatformDbContext database) : ITenantMembershipRepository
{
    public Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        return database.Memberships.AsNoTracking().AnyAsync(
            membership => membership.TenantId == tenantId
                && membership.UserId == userId
                && membership.Status == MembershipStatus.Active.ToString(),
            cancellationToken);
    }

    public async Task<IReadOnlyList<MembershipSummary>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await database.Memberships
            .AsNoTracking()
            .Where(membership => membership.TenantId == tenantId)
            .OrderBy(membership => membership.JoinedAtUtc)
            .Select(membership => new MembershipSummary(
                membership.Id,
                membership.UserId,
                membership.Status,
                membership.JoinedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<MembershipSummary?> GetAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default)
    {
        return database.Memberships
            .AsNoTracking()
            .Where(membership => membership.TenantId == tenantId && membership.Id == membershipId)
            .Select(membership => new MembershipSummary(
                membership.Id,
                membership.UserId,
                membership.Status,
                membership.JoinedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<MembershipSummary> AddAsync(Guid tenantId, Guid userId, DateTimeOffset joinedAtUtc, CancellationToken cancellationToken = default)
    {
        var membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Status = MembershipStatus.Active.ToString(),
            JoinedAtUtc = joinedAtUtc,
        };
        database.Memberships.Add(membership);
        await database.SaveChangesAsync(cancellationToken);

        return new MembershipSummary(membership.Id, membership.UserId, membership.Status, membership.JoinedAtUtc);
    }

    public async Task<bool> UpdateStatusAsync(Guid tenantId, Guid membershipId, MembershipStatus status, CancellationToken cancellationToken = default)
    {
        var membership = await database.Memberships.SingleOrDefaultAsync(
            item => item.TenantId == tenantId && item.Id == membershipId,
            cancellationToken);

        if (membership is null)
        {
            return false;
        }

        membership.Status = status.ToString();
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default)
    {
        var membership = await database.Memberships.SingleOrDefaultAsync(
            item => item.TenantId == tenantId && item.Id == membershipId,
            cancellationToken);

        if (membership is null)
        {
            return false;
        }

        database.Memberships.Remove(membership);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }
}
