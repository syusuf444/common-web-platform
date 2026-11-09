using Microsoft.AspNetCore.Mvc;
using Platform.Api.Identity;

namespace Platform.Api.Auditing;

public static class AuditEndpointMappings
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/tenant/audit", async (
                int? skip,
                int? take,
                HttpContext context,
                IAuditRecordService auditRecords,
                CancellationToken cancellationToken) =>
            {
                var offset = skip ?? 0;
                var pageSize = take ?? 50;
                if (offset is < 0 or > 10000 || pageSize is < 1 or > 100)
                {
                    var errors = new Dictionary<string, string[]>();
                    if (offset is < 0 or > 10000)
                    {
                        errors[nameof(skip)] = ["Skip must be between 0 and 10000."];
                    }

                    if (pageSize is < 1 or > 100)
                    {
                        errors[nameof(take)] = ["Take must be between 1 and 100."];
                    }

                    return Results.ValidationProblem(
                        errors,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The audit page request is invalid.",
                        extensions: new Dictionary<string, object?>
                        {
                            ["code"] = "validation_failed",
                            ["correlationId"] = context.Items[CorrelationId.ItemKey],
                        });
                }

                return Results.Ok(await auditRecords.ListAsync(offset, pageSize, cancellationToken));
            })
            .WithName("ListTenantAuditRecords")
            .WithTags("Tenant Audit")
            .WithSummary("List security and administration audit records for the active tenant")
            .WithDescription("Results are ordered newest first. Audit records are append-only and scoped to the verified tenant.")
            .RequireAuthorization(TenantPermissionPolicies.AuditRead)
            .Produces<AuditRecordListResponse>()
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
