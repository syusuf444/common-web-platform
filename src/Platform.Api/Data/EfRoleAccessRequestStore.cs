using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Domain;
using Platform.Modules.Workflows.Application;
using Platform.Modules.Workflows.Domain;

namespace Platform.Api.Data;

public sealed class EfRoleAccessRequestStore(PlatformDbContext database) : IRoleAccessRequestStore
{
    public async Task<RoleAccessRequestCreationResult> CreateAsync(
        Guid tenantId,
        Guid requesterUserId,
        Guid roleId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var membership = await database.Memberships
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId
                && item.UserId == requesterUserId
                && item.Status == MembershipStatus.Active.ToString())
            .Select(item => new { item.Id })
            .SingleOrDefaultAsync(cancellationToken);
        var roleExists = await database.TenantRoles
            .AsNoTracking()
            .AnyAsync(role => role.TenantId == tenantId && role.Id == roleId, cancellationToken);
        if (membership is null || !roleExists)
        {
            return new RoleAccessRequestCreationResult(RoleAccessRequestCreationStatus.RoleNotFound, null);
        }

        var alreadyAssigned = await database.MembershipRoles
            .AsNoTracking()
            .AnyAsync(item => item.TenantId == tenantId
                && item.MembershipId == membership.Id
                && item.RoleId == roleId,
                cancellationToken);
        if (alreadyAssigned)
        {
            return new RoleAccessRequestCreationResult(RoleAccessRequestCreationStatus.RoleAlreadyAssigned, null);
        }

        var hasPendingRequest = await database.RoleAccessRequests
            .AsNoTracking()
            .AnyAsync(request => request.TenantId == tenantId
                && request.RequesterMembershipId == membership.Id
                && request.RoleId == roleId
                && request.Status == RoleAccessRequestStatuses.Pending,
                cancellationToken);
        if (hasPendingRequest)
        {
            return new RoleAccessRequestCreationResult(RoleAccessRequestCreationStatus.PendingRequestExists, null);
        }

        var request = new RoleAccessRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequesterMembershipId = membership.Id,
            RequesterUserId = requesterUserId,
            RoleId = roleId,
            Status = RoleAccessRequestStatuses.Pending,
            RequestedAtUtc = requestedAtUtc,
        };
        database.RoleAccessRequests.Add(request);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return new RoleAccessRequestCreationResult(RoleAccessRequestCreationStatus.PendingRequestExists, null);
        }

        await transaction.CommitAsync(cancellationToken);
        return new RoleAccessRequestCreationResult(
            RoleAccessRequestCreationStatus.Created,
            await GetResponseAsync(tenantId, request.Id, cancellationToken));
    }

    public Task<RoleAccessRequestListResponse> ListForRequesterAsync(
        Guid tenantId,
        Guid requesterUserId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        return ListAsync(
            database.RoleAccessRequests.AsNoTracking()
                .Where(request => request.TenantId == tenantId && request.RequesterUserId == requesterUserId),
            oldestFirst: false,
            skip,
            take,
            cancellationToken);
    }

    public Task<RoleAccessRequestListResponse> ListPendingAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        return ListAsync(
            database.RoleAccessRequests.AsNoTracking()
                .Where(request => request.TenantId == tenantId
                    && request.Status == RoleAccessRequestStatuses.Pending),
            oldestFirst: true,
            skip,
            take,
            cancellationToken);
    }

    public Task<RoleAccessRequestResponse?> GetForRequesterAsync(
        Guid tenantId,
        Guid requesterUserId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return QueryResponses(tenantId, requesterUserId, requestId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<RoleAccessRequestDecisionResult> DecideAsync(
        Guid tenantId,
        Guid requestId,
        Guid deciderUserId,
        bool approve,
        DateTimeOffset decidedAtUtc,
        CancellationToken cancellationToken)
    {
        var request = await database.RoleAccessRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.TenantId == tenantId && item.Id == requestId,
                cancellationToken);
        if (request is null)
        {
            return new RoleAccessRequestDecisionResult(RoleAccessRequestDecisionStatus.NotFound, null);
        }

        if (request.RequesterUserId == deciderUserId)
        {
            return new RoleAccessRequestDecisionResult(RoleAccessRequestDecisionStatus.CannotDecideOwnRequest, null);
        }

        if (request.Status != RoleAccessRequestStatuses.Pending)
        {
            return new RoleAccessRequestDecisionResult(RoleAccessRequestDecisionStatus.AlreadyDecided, null);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var status = approve ? RoleAccessRequestStatuses.Approved : RoleAccessRequestStatuses.Rejected;
        var updated = await database.RoleAccessRequests
            .Where(item => item.TenantId == tenantId
                && item.Id == requestId
                && item.Status == RoleAccessRequestStatuses.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, status)
                    .SetProperty(item => item.DecidedAtUtc, decidedAtUtc)
                    .SetProperty(item => item.DecidedByUserId, deciderUserId),
                cancellationToken);
        if (updated != 1)
        {
            return new RoleAccessRequestDecisionResult(RoleAccessRequestDecisionStatus.AlreadyDecided, null);
        }

        if (approve)
        {
            var membershipIsActive = await database.Memberships
                .AsNoTracking()
                .AnyAsync(membership => membership.TenantId == tenantId
                    && membership.Id == request.RequesterMembershipId
                    && membership.UserId == request.RequesterUserId
                    && membership.Status == MembershipStatus.Active.ToString(),
                    cancellationToken);
            if (!membershipIsActive)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new RoleAccessRequestDecisionResult(RoleAccessRequestDecisionStatus.RequesterInactive, null);
            }

            var roleExists = await database.TenantRoles
                .AsNoTracking()
                .AnyAsync(role => role.TenantId == tenantId && role.Id == request.RoleId, cancellationToken);
            if (!roleExists)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new RoleAccessRequestDecisionResult(RoleAccessRequestDecisionStatus.RoleNotFound, null);
            }

            var alreadyAssigned = await database.MembershipRoles
                .AsNoTracking()
                .AnyAsync(item => item.TenantId == tenantId
                    && item.MembershipId == request.RequesterMembershipId
                    && item.RoleId == request.RoleId,
                    cancellationToken);
            if (!alreadyAssigned)
            {
                database.MembershipRoles.Add(new MembershipRole
                {
                    TenantId = tenantId,
                    MembershipId = request.RequesterMembershipId,
                    RoleId = request.RoleId,
                    AssignedAtUtc = decidedAtUtc,
                });
            }
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var response = await GetResponseAsync(tenantId, requestId, cancellationToken);
        return new RoleAccessRequestDecisionResult(
            approve ? RoleAccessRequestDecisionStatus.Approved : RoleAccessRequestDecisionStatus.Rejected,
            response);
    }

    private IQueryable<RoleAccessRequestResponse> QueryResponses(
        Guid tenantId,
        Guid? requesterUserId = null,
        Guid? requestId = null,
        bool pendingOnly = false)
    {
        var requests = database.RoleAccessRequests
            .AsNoTracking()
            .Where(request => request.TenantId == tenantId);
        if (requesterUserId.HasValue)
        {
            requests = requests.Where(request => request.RequesterUserId == requesterUserId.Value);
        }

        if (requestId.HasValue)
        {
            requests = requests.Where(request => request.Id == requestId.Value);
        }

        if (pendingOnly)
        {
            requests = requests.Where(request => request.Status == RoleAccessRequestStatuses.Pending);
        }

        return from request in requests
               join role in database.TenantRoles.AsNoTracking()
                   on new { request.TenantId, request.RoleId }
                   equals new { role.TenantId, RoleId = role.Id }
               select new RoleAccessRequestResponse(
                   request.Id,
                   request.RequesterMembershipId,
                   request.RequesterUserId,
                   request.RoleId,
                   role.Name,
                   request.Status,
                   request.RequestedAtUtc,
                   request.DecidedAtUtc,
                   request.DecidedByUserId);
    }

    private async Task<RoleAccessRequestListResponse> ListAsync(
        IQueryable<RoleAccessRequest> requests,
        bool oldestFirst,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var orderedRequests = oldestFirst
            ? requests.OrderBy(request => request.RequestedAtUtc).ThenBy(request => request.Id)
            : requests.OrderByDescending(request => request.RequestedAtUtc).ThenByDescending(request => request.Id);
        var pageRequests = orderedRequests.Skip(skip).Take(take + 1);
        var rows = await (from request in pageRequests
                          join role in database.TenantRoles.AsNoTracking()
                              on new { request.TenantId, request.RoleId }
                              equals new { role.TenantId, RoleId = role.Id }
                          select new RoleAccessRequestResponse(
                              request.Id,
                              request.RequesterMembershipId,
                              request.RequesterUserId,
                              request.RoleId,
                              role.Name,
                              request.Status,
                              request.RequestedAtUtc,
                              request.DecidedAtUtc,
                              request.DecidedByUserId))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > take;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        rows = (oldestFirst
                ? rows.OrderBy(request => request.RequestedAtUtc).ThenBy(request => request.Id)
                : rows.OrderByDescending(request => request.RequestedAtUtc).ThenByDescending(request => request.Id))
            .ToList();
        return new RoleAccessRequestListResponse(rows, skip, take, hasMore);
    }

    private async Task<RoleAccessRequestResponse> GetResponseAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return await QueryResponses(tenantId, requestId: requestId)
            .SingleAsync(cancellationToken);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.GetBaseException() is SqlException { Number: 2601 or 2627 };
    }
}
