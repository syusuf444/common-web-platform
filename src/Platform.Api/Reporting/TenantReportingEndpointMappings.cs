using Microsoft.AspNetCore.Mvc;
using Platform.Api.Identity;
using Platform.Modules.Tenancy.Application;

namespace Platform.Api.Reporting;

public static class TenantReportingEndpointMappings
{
    private const int MaximumOffset = 10000;
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private const int MaximumExportSize = 1000;

    public static IEndpointRouteBuilder MapTenantReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var reports = endpoints.MapGroup("/api/v1/tenant")
            .WithTags("Tenant Reports");

        reports.MapGet("/dashboard", async (
                ITenantContextAccessor tenantContext,
                ITenantReportingQueries reporting,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var tenantId = tenantContext.RequireCurrent().TenantId;
                var dashboard = await reporting.GetDashboardAsync(
                    tenantId,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                return Results.Ok(dashboard);
            })
            .WithName("GetTenantDashboard")
            .WithSummary("Get aggregate dashboard counts for the active tenant")
            .WithDescription("Returns active and suspended member counts, stored files, and audit events from the last 30 days.")
            .RequireAuthorization(TenantPermissionPolicies.ReportsRead)
            .Produces<TenantDashboardResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        reports.MapGet("/reports/audit", async (
                int? skip,
                int? take,
                HttpContext context,
                ITenantContextAccessor tenantContext,
                ITenantReportingQueries reporting,
                CancellationToken cancellationToken) =>
            {
                var page = ValidatePage(skip, take, MaximumPageSize, context);
                if (page.Error is not null)
                {
                    return page.Error;
                }

                var tenantId = tenantContext.RequireCurrent().TenantId;
                return Results.Ok(await reporting.GetAuditReportAsync(
                    tenantId,
                    page.Skip,
                    page.Take,
                    cancellationToken));
            })
            .WithName("GetTenantAuditReport")
            .WithSummary("Get a page of tenant audit report rows")
            .WithDescription("Returns only operational event fields; sensitive audit metadata is excluded.")
            .RequireAuthorization(TenantPermissionPolicies.ReportsRead)
            .Produces<TenantAuditReportPage>()
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        reports.MapGet("/reports/audit/export.csv", async (
                int? skip,
                int? take,
                HttpContext context,
                ITenantContextAccessor tenantContext,
                ITenantReportingQueries reporting,
                CancellationToken cancellationToken) =>
            {
                var page = ValidatePage(skip, take, MaximumExportSize, context);
                if (page.Error is not null)
                {
                    return page.Error;
                }

                var tenantId = tenantContext.RequireCurrent().TenantId;
                var result = await reporting.GetAuditReportAsync(
                    tenantId,
                    page.Skip,
                    page.Take,
                    cancellationToken);
                var csv = TenantAuditCsvExporter.Export(result.Items);
                return Results.Text(csv, "text/csv; charset=utf-8");
            })
            .WithName("ExportTenantAuditReport")
            .WithSummary("Export a bounded page of tenant audit report rows as CSV")
            .WithDescription($"Exports at most {MaximumExportSize} rows per request. Use skip and take to page through larger reports.")
            .RequireAuthorization(TenantPermissionPolicies.ReportsExport)
            .Produces<string>(StatusCodes.Status200OK, "text/csv")
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static (int Skip, int Take, IResult? Error) ValidatePage(
        int? skip,
        int? take,
        int maximumPageSize,
        HttpContext context)
    {
        var offset = skip ?? 0;
        var pageSize = take ?? DefaultPageSize;
        var errors = new Dictionary<string, string[]>();
        if (offset is < 0 or > MaximumOffset)
        {
            errors[nameof(skip)] = [$"Skip must be between 0 and {MaximumOffset}."];
        }

        if (pageSize < 1 || pageSize > maximumPageSize)
        {
            errors[nameof(take)] = [$"Take must be between 1 and {maximumPageSize}."];
        }

        if (errors.Count != 0)
        {
            return (offset, pageSize, Results.ValidationProblem(
                errors,
                statusCode: StatusCodes.Status400BadRequest,
                title: "The report page request is invalid.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "validation_failed",
                    ["correlationId"] = context.Items[CorrelationId.ItemKey],
                }));
        }

        return (offset, pageSize, null);
    }
}
