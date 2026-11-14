namespace Platform.Modules.Workflows.Application;

/// <summary>Requests a tenant role for the authenticated member.</summary>
public sealed record CreateRoleAccessRequest
{
    /// <summary>Identifier of the role requested in the active tenant.</summary>
    public required Guid RoleId { get; init; }
}

/// <summary>A tenant member's request for a role and its decision state.</summary>
public sealed record RoleAccessRequestResponse(
    Guid Id,
    Guid RequesterMembershipId,
    Guid RequesterUserId,
    Guid RoleId,
    string RoleName,
    string Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    Guid? DecidedByUserId);

/// <summary>A bounded page of role access requests.</summary>
public sealed record RoleAccessRequestListResponse(
    IReadOnlyList<RoleAccessRequestResponse> Items,
    int Skip,
    int Take,
    bool HasMore);

public enum RoleAccessRequestCreationStatus
{
    Created,
    RoleNotFound,
    RoleAlreadyAssigned,
    PendingRequestExists,
}

public sealed record RoleAccessRequestCreationResult(
    RoleAccessRequestCreationStatus Status,
    RoleAccessRequestResponse? Request);

public enum RoleAccessRequestDecisionStatus
{
    Approved,
    Rejected,
    NotFound,
    AlreadyDecided,
    CannotDecideOwnRequest,
    RequesterInactive,
    RoleNotFound,
}

public sealed record RoleAccessRequestDecisionResult(
    RoleAccessRequestDecisionStatus Status,
    RoleAccessRequestResponse? Request);

public interface IRoleAccessRequestStore
{
    Task<RoleAccessRequestCreationResult> CreateAsync(
        Guid tenantId,
        Guid requesterUserId,
        Guid roleId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken);

    Task<RoleAccessRequestListResponse> ListForRequesterAsync(
        Guid tenantId,
        Guid requesterUserId,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<RoleAccessRequestResponse?> GetForRequesterAsync(
        Guid tenantId,
        Guid requesterUserId,
        Guid requestId,
        CancellationToken cancellationToken);

    Task<RoleAccessRequestListResponse> ListPendingAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<RoleAccessRequestDecisionResult> DecideAsync(
        Guid tenantId,
        Guid requestId,
        Guid deciderUserId,
        bool approve,
        DateTimeOffset decidedAtUtc,
        CancellationToken cancellationToken);
}
