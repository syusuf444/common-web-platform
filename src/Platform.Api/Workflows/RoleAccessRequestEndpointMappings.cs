using Microsoft.AspNetCore.Mvc;
using Platform.Api.Auditing;
using Platform.Api.Identity;
using Platform.Modules.Workflows.Application;

namespace Platform.Api.Workflows;

public static class RoleAccessRequestEndpointMappings
{
    private const int MaximumOffset = 10000;
    private const int MaximumPageSize = 100;

    public static IEndpointRouteBuilder MapRoleAccessRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var requests = endpoints.MapGroup("/api/v1/tenant/role-access-requests")
            .WithTags("Tenant Role Access Requests")
            .RequireAuthorization();

        requests.MapGet("/mine", async (
                int? skip,
                int? take,
                HttpContext context,
                IRoleAccessRequestService service,
                CancellationToken cancellationToken) =>
            {
                var (offset, pageSize) = GetPage(skip, take);
                if (!IsValidPage(offset, pageSize))
                {
                    return InvalidPage(context, offset, pageSize);
                }

                return Results.Ok(await service.ListMineAsync(offset, pageSize, cancellationToken));
            })
            .WithName("ListMyRoleAccessRequests")
            .WithSummary("List role access requests submitted by the active member")
            .WithDescription("Results are scoped to the authenticated member and active tenant.")
            .Produces<RoleAccessRequestListResponse>()
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        requests.MapGet("/{requestId:guid}", async (
                Guid requestId,
                IRoleAccessRequestService service,
                CancellationToken cancellationToken) =>
            {
                var request = await service.GetMineAsync(requestId, cancellationToken);
                return request is null ? Results.NotFound() : Results.Ok(request);
            })
            .WithName("GetMyRoleAccessRequest")
            .WithSummary("Get a role access request submitted by the active member")
            .WithDescription("Requests belonging to another member or tenant are returned as not found.")
            .Produces<RoleAccessRequestResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        requests.MapGet("/pending", async (
                int? skip,
                int? take,
                HttpContext context,
                IRoleAccessRequestService service,
                CancellationToken cancellationToken) =>
            {
                var (offset, pageSize) = GetPage(skip, take);
                if (!IsValidPage(offset, pageSize))
                {
                    return InvalidPage(context, offset, pageSize);
                }

                return Results.Ok(await service.ListPendingAsync(offset, pageSize, cancellationToken));
            })
            .WithName("ListPendingRoleAccessRequests")
            .WithSummary("List pending role access requests for the active tenant")
            .WithDescription("Requires workflows.manage; results are paginated oldest-first for review.")
            .RequireAuthorization(TenantPermissionPolicies.WorkflowsManage)
            .Produces<RoleAccessRequestListResponse>()
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        requests.MapPost("", async (
                CreateRoleAccessRequest request,
                HttpContext context,
                IRoleAccessRequestService service,
                IAuditRecordService auditRecords,
                CancellationToken cancellationToken) =>
            {
                if (request.RoleId == Guid.Empty)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            [nameof(request.RoleId)] = ["A role ID is required."],
                        },
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The role access request is invalid.",
                        extensions: ErrorExtensions(context));
                }

                var result = await service.CreateAsync(request.RoleId, cancellationToken);
                if (result.Status == RoleAccessRequestCreationStatus.RoleNotFound)
                {
                    return Results.NotFound(new ProblemDetails
                    {
                        Status = StatusCodes.Status404NotFound,
                        Title = "The active membership or role was not found.",
                        Extensions = ErrorExtensions("role_or_membership_not_found", context),
                    });
                }

                if (result.Status is RoleAccessRequestCreationStatus.RoleAlreadyAssigned
                    or RoleAccessRequestCreationStatus.PendingRequestExists)
                {
                    return Results.Conflict(new ProblemDetails
                    {
                        Status = StatusCodes.Status409Conflict,
                        Title = result.Status == RoleAccessRequestCreationStatus.RoleAlreadyAssigned
                            ? "The requested role is already assigned."
                            : "A pending request for this role already exists.",
                        Extensions = ErrorExtensions(
                            result.Status == RoleAccessRequestCreationStatus.RoleAlreadyAssigned
                                ? "role_already_assigned"
                                : "pending_request_exists",
                            context),
                    });
                }

                var created = result.Request
                    ?? throw new InvalidOperationException("A created role access request was not returned.");
                await auditRecords.RecordAsync(
                    new AuditEvent(
                        "tenant.role_access.requested",
                        "role_access_request",
                        created.Id.ToString(),
                        Metadata: new Dictionary<string, string>
                        {
                            ["roleId"] = created.RoleId.ToString(),
                            ["membershipId"] = created.RequesterMembershipId.ToString(),
                        }),
                    cancellationToken);
                return Results.Created($"/api/v1/tenant/role-access-requests/{created.Id}", created);
            })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("CreateRoleAccessRequest")
            .WithSummary("Request a role for the authenticated tenant membership")
            .WithDescription("The requester can only request access for their own active membership. Only one pending request per role is allowed.")
            .Produces<RoleAccessRequestResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        MapDecision(requests, "approve", approve: true);
        MapDecision(requests, "reject", approve: false);
        return endpoints;
    }

    private static void MapDecision(RouteGroupBuilder requests, string action, bool approve)
    {
        requests.MapPost($"/{{requestId:guid}}/{action}", async (
                Guid requestId,
                HttpContext context,
                IRoleAccessRequestService service,
                IAuditRecordService auditRecords,
                CancellationToken cancellationToken) =>
            {
                var decision = await service.DecideAsync(requestId, approve, cancellationToken);
                var result = decision.Status;
                if (result is RoleAccessRequestDecisionStatus.NotFound
                    or RoleAccessRequestDecisionStatus.RoleNotFound)
                {
                    return Results.NotFound(new ProblemDetails
                    {
                        Status = StatusCodes.Status404NotFound,
                        Title = result == RoleAccessRequestDecisionStatus.NotFound
                            ? "The role access request was not found in the active tenant."
                            : "The requested role no longer exists in the active tenant.",
                        Extensions = ErrorExtensions(
                            result == RoleAccessRequestDecisionStatus.NotFound
                                ? "role_access_request_not_found"
                                : "role_not_found",
                            context),
                    });
                }

                if (result is RoleAccessRequestDecisionStatus.AlreadyDecided
                    or RoleAccessRequestDecisionStatus.CannotDecideOwnRequest
                    or RoleAccessRequestDecisionStatus.RequesterInactive)
                {
                    var code = result switch
                    {
                        RoleAccessRequestDecisionStatus.AlreadyDecided => "request_already_decided",
                        RoleAccessRequestDecisionStatus.CannotDecideOwnRequest => "cannot_decide_own_request",
                        _ => "requester_membership_inactive",
                    };
                    var title = result switch
                    {
                        RoleAccessRequestDecisionStatus.AlreadyDecided => "The role access request has already been decided.",
                        RoleAccessRequestDecisionStatus.CannotDecideOwnRequest => "You cannot decide your own role access request.",
                        _ => "The requester is no longer an active member of this tenant.",
                    };
                    return Results.Conflict(new ProblemDetails
                    {
                        Status = StatusCodes.Status409Conflict,
                        Title = title,
                        Extensions = ErrorExtensions(code, context),
                    });
                }

                var decidedRequest = decision.Request
                    ?? throw new InvalidOperationException("A completed role access decision was not returned.");
                await auditRecords.RecordAsync(
                    new AuditEvent(
                        approve ? "tenant.role_access.approved" : "tenant.role_access.rejected",
                        "role_access_request",
                        requestId.ToString(),
                        Metadata: new Dictionary<string, string>
                        {
                            ["roleId"] = decidedRequest.RoleId.ToString(),
                            ["membershipId"] = decidedRequest.RequesterMembershipId.ToString(),
                        }),
                    cancellationToken);
                return Results.Ok(decidedRequest);
            })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName(approve ? "ApproveRoleAccessRequest" : "RejectRoleAccessRequest")
            .WithSummary(approve ? "Approve a pending role access request" : "Reject a pending role access request")
            .WithDescription("Requires workflows.manage. The requester cannot decide their own request; approval assigns the role in the same transaction.")
            .RequireAuthorization(TenantPermissionPolicies.WorkflowsManage)
            .Produces<RoleAccessRequestResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static Dictionary<string, object?> ErrorExtensions(HttpContext context)
    {
        return new Dictionary<string, object?>
        {
            ["code"] = "validation_failed",
            ["correlationId"] = context.Items[CorrelationId.ItemKey],
        };
    }

    private static (int Skip, int Take) GetPage(int? skip, int? take)
    {
        return (skip ?? 0, take ?? 50);
    }

    private static bool IsValidPage(int skip, int take)
    {
        return skip is >= 0 and <= MaximumOffset && take is >= 1 and <= MaximumPageSize;
    }

    private static IResult InvalidPage(HttpContext context, int skip, int take)
    {
        var errors = new Dictionary<string, string[]>();
        if (skip is < 0 or > MaximumOffset)
        {
            errors[nameof(skip)] = [$"Skip must be between 0 and {MaximumOffset}."];
        }

        if (take is < 1 or > MaximumPageSize)
        {
            errors[nameof(take)] = [$"Take must be between 1 and {MaximumPageSize}."];
        }

        return Results.ValidationProblem(
            errors,
            statusCode: StatusCodes.Status400BadRequest,
            title: "The role access request page is invalid.",
            extensions: ErrorExtensions(context));
    }

    private static Dictionary<string, object?> ErrorExtensions(string code, HttpContext context)
    {
        return new Dictionary<string, object?>
        {
            ["code"] = code,
            ["correlationId"] = context.Items[CorrelationId.ItemKey],
        };
    }
}
