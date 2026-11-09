using Microsoft.AspNetCore.Mvc;
using Platform.Api.Auditing;
using Platform.Modules.Identity.Application;
using Platform.Modules.Tenancy.Application;

namespace Platform.Api.Identity;

public static class TenantAuthorizationEndpointMappings
{
    public static IEndpointRouteBuilder MapTenantAuthorizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tenant = endpoints.MapGroup("/api/v1/tenant").WithTags("Tenant Authorization");

        tenant.MapGet("/memberships", async (TenantMembershipService memberships, CancellationToken cancellationToken) =>
            Results.Ok(await memberships.ListAsync(cancellationToken)))
            .WithName("ListTenantMemberships")
            .WithSummary("List memberships in the active tenant")
            .RequireAuthorization(TenantPermissionPolicies.MembersRead)
            .Produces<IReadOnlyList<MembershipSummary>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        tenant.MapGet("/roles", async (TenantRoleService roles, CancellationToken cancellationToken) =>
            Results.Ok(await roles.ListAsync(cancellationToken)))
            .WithName("ListTenantRoles")
            .WithSummary("List roles and permissions in the active tenant")
            .RequireAuthorization(TenantPermissionPolicies.RolesManage)
            .Produces<IReadOnlyList<TenantRoleSummary>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        tenant.MapPost("/roles", async (
            CreateTenantRoleRequest request,
            HttpContext context,
            TenantRoleService roles,
            IAuditRecordService auditRecords,
            CancellationToken cancellationToken) =>
        {
            var errors = TenantRoleRequestValidator.Validate(request);
            if (errors.Count != 0)
            {
                return Results.ValidationProblem(
                    errors,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "The role request is invalid.",
                    extensions: ErrorExtensions("validation_failed", context));
            }

            var role = await roles.CreateAsync(request, cancellationToken);
            await auditRecords.RecordAsync(
                new AuditEvent(
                    "tenant.role.created",
                    "tenant_role",
                    role.Id.ToString(),
                    Metadata: new Dictionary<string, string>
                    {
                        ["permissionCount"] = role.PermissionCodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    }),
                cancellationToken);
            return Results.Created($"/api/v1/tenant/roles/{role.Id}", role);
        })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("CreateTenantRole")
            .WithSummary("Create a tenant-scoped role with supported permissions")
            .RequireAuthorization(TenantPermissionPolicies.RolesManage)
            .Produces<TenantRoleSummary>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        tenant.MapPost("/memberships/{membershipId:guid}/roles/{roleId:guid}", async (
            Guid membershipId,
            Guid roleId,
            TenantRoleService roles,
            IAuditRecordService auditRecords,
            CancellationToken cancellationToken) =>
        {
            var assignmentResult = await roles.AssignAsync(membershipId, roleId, cancellationToken);
            if (assignmentResult == TenantRoleAssignmentResult.Assigned)
            {
                await auditRecords.RecordAsync(
                    new AuditEvent(
                        "tenant.role.assigned",
                        "membership_role",
                        membershipId.ToString(),
                        Metadata: new Dictionary<string, string> { ["roleId"] = roleId.ToString() }),
                    cancellationToken);
            }

            return assignmentResult == TenantRoleAssignmentResult.NotFound ? Results.NotFound() : Results.NoContent();
        })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("AssignTenantRole")
            .WithSummary("Assign a role to an active membership in the active tenant")
            .RequireAuthorization(TenantPermissionPolicies.RolesManage)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        tenant.MapDelete("/memberships/{membershipId:guid}/roles/{roleId:guid}", async (
            Guid membershipId,
            Guid roleId,
            TenantRoleService roles,
            IAuditRecordService auditRecords,
            CancellationToken cancellationToken) =>
        {
            var removed = await roles.RemoveAsync(membershipId, roleId, cancellationToken);
            if (removed)
            {
                await auditRecords.RecordAsync(
                    new AuditEvent(
                        "tenant.role.removed",
                        "membership_role",
                        membershipId.ToString(),
                        Metadata: new Dictionary<string, string> { ["roleId"] = roleId.ToString() }),
                    cancellationToken);
            }

            return removed ? Results.NoContent() : Results.NotFound();
        })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("RemoveTenantRole")
            .WithSummary("Remove a tenant role from a membership in the active tenant")
            .RequireAuthorization(TenantPermissionPolicies.RolesManage)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static Dictionary<string, object?> ErrorExtensions(string code, HttpContext context)
    {
        return new Dictionary<string, object?>
        {
            ["code"] = code,
            ["correlationId"] = context.Items["CorrelationId"],
        };
    }
}
