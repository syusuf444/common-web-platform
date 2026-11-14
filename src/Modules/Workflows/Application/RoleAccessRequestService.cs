using Platform.Modules.Tenancy.Application;

namespace Platform.Modules.Workflows.Application;

public interface IRoleAccessRequestService
{
    Task<RoleAccessRequestCreationResult> CreateAsync(Guid roleId, CancellationToken cancellationToken);

    Task<RoleAccessRequestListResponse> ListMineAsync(int skip, int take, CancellationToken cancellationToken);

    Task<RoleAccessRequestResponse?> GetMineAsync(Guid requestId, CancellationToken cancellationToken);

    Task<RoleAccessRequestListResponse> ListPendingAsync(int skip, int take, CancellationToken cancellationToken);

    Task<RoleAccessRequestDecisionResult> DecideAsync(
        Guid requestId,
        bool approve,
        CancellationToken cancellationToken);
}

public sealed class RoleAccessRequestService(
    IRoleAccessRequestStore store,
    ITenantContextAccessor tenantContext,
    TimeProvider timeProvider) : IRoleAccessRequestService
{
    public Task<RoleAccessRequestCreationResult> CreateAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        var context = tenantContext.RequireCurrent();
        return store.CreateAsync(
            context.TenantId,
            context.UserId,
            roleId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<RoleAccessRequestListResponse> ListMineAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var context = tenantContext.RequireCurrent();
        return store.ListForRequesterAsync(context.TenantId, context.UserId, skip, take, cancellationToken);
    }

    public Task<RoleAccessRequestResponse?> GetMineAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var context = tenantContext.RequireCurrent();
        return store.GetForRequesterAsync(context.TenantId, context.UserId, requestId, cancellationToken);
    }

    public Task<RoleAccessRequestListResponse> ListPendingAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var context = tenantContext.RequireCurrent();
        return store.ListPendingAsync(context.TenantId, skip, take, cancellationToken);
    }

    public Task<RoleAccessRequestDecisionResult> DecideAsync(
        Guid requestId,
        bool approve,
        CancellationToken cancellationToken)
    {
        var context = tenantContext.RequireCurrent();
        return store.DecideAsync(
            context.TenantId,
            requestId,
            context.UserId,
            approve,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
