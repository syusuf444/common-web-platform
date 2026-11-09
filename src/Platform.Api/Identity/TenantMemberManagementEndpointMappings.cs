using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Platform.Api.Auditing;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Api.Identity;

public static class TenantMemberManagementEndpointMappings
{
    public static IEndpointRouteBuilder MapTenantMemberManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var members = endpoints.MapGroup("/api/v1/tenant/members")
            .WithTags("Tenant Members");

        members.MapGet("", async (ITenantMemberManagementService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.ListAsync(cancellationToken)))
            .WithName("ListTenantMembers")
            .WithSummary("List members and role assignments in the active tenant")
            .RequireAuthorization(TenantPermissionPolicies.MembersRead)
            .Produces<IReadOnlyList<TenantMemberResponse>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        members.MapGet("/{membershipId:guid}", async (
                Guid membershipId,
                ITenantMemberManagementService service,
                CancellationToken cancellationToken) =>
            {
                var member = await service.GetAsync(membershipId, cancellationToken);
                return member is null ? Results.NotFound() : Results.Ok(member);
            })
            .WithName("GetTenantMember")
            .WithSummary("Get a member and role assignments in the active tenant")
            .RequireAuthorization(TenantPermissionPolicies.MembersRead)
            .Produces<TenantMemberResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        endpoints.MapPost("/api/v1/tenant/memberships", async (
                AddTenantMemberRequest request,
                HttpContext context,
                ITenantMemberManagementService service,
                IAuditRecordService auditRecords,
                CancellationToken cancellationToken) =>
            {
                var errors = ValidateEmail(request);
                if (errors.Count != 0)
                {
                    return Results.ValidationProblem(
                        errors,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The member request is invalid.",
                        extensions: ErrorExtensions("validation_failed", context));
                }

                var (outcome, member) = await service.AddAsync(request.Email, cancellationToken);
                if (outcome == AddTenantMemberOutcome.AccountNotFound)
                {
                    return Results.NotFound(new ProblemDetails
                    {
                        Status = StatusCodes.Status404NotFound,
                        Title = "No active account exists for that email. The user must register before being added.",
                        Extensions = { ["code"] = "account_not_found", ["correlationId"] = context.Items[CorrelationId.ItemKey] },
                    });
                }

                if (outcome == AddTenantMemberOutcome.AlreadyMember)
                {
                    return Results.Conflict(new ProblemDetails
                    {
                        Status = StatusCodes.Status409Conflict,
                        Title = "This account is already a member of the active tenant.",
                        Extensions = { ["code"] = "membership_exists", ["correlationId"] = context.Items[CorrelationId.ItemKey] },
                    });
                }

                await auditRecords.RecordAsync(
                    new AuditEvent("tenant.member.added", "membership", member!.MembershipId.ToString()),
                    cancellationToken);
                return Results.Created($"/api/v1/tenant/members/{member!.MembershipId}", member);
            })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("AddTenantMember")
            .WithSummary("Add an existing account to the active tenant")
            .WithDescription("Email invitations are not available yet. The user must first have an active platform account.")
            .RequireAuthorization(TenantPermissionPolicies.MembersManage)
            .Produces<TenantMemberResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPut("/api/v1/tenant/memberships/{membershipId:guid}/status", async (
                Guid membershipId,
                UpdateTenantMembershipStatusRequest request,
                HttpContext context,
                ITenantMemberManagementService service,
                IAuditRecordService auditRecords,
                CancellationToken cancellationToken) =>
            {
                if (!TryParseMembershipStatus(request.Status, out var status))
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            [nameof(request.Status)] = ["Status must be Active or Suspended."],
                        },
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The membership status is invalid.",
                        extensions: ErrorExtensions("validation_failed", context));
                }

                var memberBeforeUpdate = await service.GetAsync(membershipId, cancellationToken);
                var currentUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (memberBeforeUpdate is not null
                    && status == MembershipStatus.Suspended
                    && string.Equals(memberBeforeUpdate.UserId.ToString(), currentUserId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Conflict(new ProblemDetails
                    {
                        Status = StatusCodes.Status409Conflict,
                        Title = "You cannot suspend your own tenant membership.",
                        Extensions = ErrorExtensions("cannot_suspend_self", context),
                    });
                }

                var member = await service.UpdateStatusAsync(membershipId, status, cancellationToken);
                if (memberBeforeUpdate is not null
                    && member is not null
                    && !string.Equals(memberBeforeUpdate.Status, member.Status, StringComparison.Ordinal))
                {
                    await auditRecords.RecordAsync(
                        new AuditEvent(
                            "tenant.member.status_changed",
                            "membership",
                            membershipId.ToString(),
                            Metadata: new Dictionary<string, string>
                            {
                                ["from"] = memberBeforeUpdate.Status,
                                ["to"] = member.Status,
                            }),
                        cancellationToken);
                }

                return member is null ? Results.NotFound() : Results.Ok(member);
            })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("UpdateTenantMembershipStatus")
            .WithSummary("Activate or suspend a membership in the active tenant")
            .RequireAuthorization(TenantPermissionPolicies.MembersManage)
            .Produces<TenantMemberResponse>()
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static Dictionary<string, string[]> ValidateEmail(AddTenantMemberRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors[nameof(request.Email)] = ["Email is required."];
            return errors;
        }

        var email = request.Email.Trim();
        if (email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
        {
            errors[nameof(request.Email)] = ["Enter a valid email address of 256 characters or fewer."];
        }

        return errors;
    }

    private static bool TryParseMembershipStatus(string? value, out MembershipStatus status)
    {
        if (string.Equals(value, nameof(MembershipStatus.Active), StringComparison.OrdinalIgnoreCase))
        {
            status = MembershipStatus.Active;
            return true;
        }

        if (string.Equals(value, nameof(MembershipStatus.Suspended), StringComparison.OrdinalIgnoreCase))
        {
            status = MembershipStatus.Suspended;
            return true;
        }

        status = default;
        return false;
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
