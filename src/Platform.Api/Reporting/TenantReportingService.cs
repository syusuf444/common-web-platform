using System.Text;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Data;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Api.Reporting;

/// <summary>Tenant-wide counts for the dashboard overview.</summary>
public sealed record TenantDashboardResponse(
    int ActiveMembers,
    int SuspendedMembers,
    int StoredFiles,
    int AuditEventsLast30Days);

/// <summary>A reportable security or administration event.</summary>
public sealed record TenantAuditReportItem(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string Action,
    string ResourceType,
    string? ResourceId,
    Guid? ActorUserId);

/// <summary>A page of tenant audit report rows.</summary>
public sealed record TenantAuditReportPage(
    IReadOnlyList<TenantAuditReportItem> Items,
    int Skip,
    int Take,
    bool HasMore);

public interface ITenantReportingQueries
{
    Task<TenantDashboardResponse> GetDashboardAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<TenantAuditReportPage> GetAuditReportAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken);
}

public sealed class EfTenantReportingQueries(PlatformDbContext database) : ITenantReportingQueries
{
    public async Task<TenantDashboardResponse> GetDashboardAsync(
        Guid tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var activeMembers = await database.Memberships
            .AsNoTracking()
            .CountAsync(
                membership => membership.TenantId == tenantId && membership.Status == MembershipStatus.Active.ToString(),
                cancellationToken);
        var suspendedMembers = await database.Memberships
            .AsNoTracking()
            .CountAsync(
                membership => membership.TenantId == tenantId && membership.Status == MembershipStatus.Suspended.ToString(),
                cancellationToken);
        var storedFiles = await database.TenantFiles
            .AsNoTracking()
            .CountAsync(file => file.TenantId == tenantId, cancellationToken);
        var since = now.AddDays(-30);
        var auditEvents = await database.AuditRecords
            .AsNoTracking()
            .CountAsync(
                record => record.TenantId == tenantId && record.OccurredAtUtc >= since,
                cancellationToken);

        return new TenantDashboardResponse(activeMembers, suspendedMembers, storedFiles, auditEvents);
    }

    public async Task<TenantAuditReportPage> GetAuditReportAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var records = await database.AuditRecords
            .AsNoTracking()
            .Where(record => record.TenantId == tenantId)
            .OrderByDescending(record => record.OccurredAtUtc)
            .ThenByDescending(record => record.Id)
            .Skip(skip)
            .Take(take + 1)
            .Select(record => new TenantAuditReportItem(
                record.Id,
                record.OccurredAtUtc,
                record.Action,
                record.ResourceType,
                record.ResourceId,
                record.ActorUserId))
            .ToListAsync(cancellationToken);

        var hasMore = records.Count > take;
        if (hasMore)
        {
            records.RemoveAt(records.Count - 1);
        }

        return new TenantAuditReportPage(records, skip, take, hasMore);
    }
}

public static class TenantAuditCsvExporter
{
    public static string Export(IEnumerable<TenantAuditReportItem> items)
    {
        var output = new StringBuilder("id,occurredAtUtc,action,resourceType,resourceId,actorUserId\r\n");
        foreach (var item in items)
        {
            AppendRow(
                output,
                item.Id.ToString(),
                item.OccurredAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                item.Action,
                item.ResourceType,
                item.ResourceId ?? string.Empty,
                item.ActorUserId?.ToString() ?? string.Empty);
        }

        return output.ToString();
    }

    private static void AppendRow(StringBuilder output, params string[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                output.Append(',');
            }

            var value = values[index];
            var firstNonWhitespace = value.TrimStart(' ', '\t', '\r', '\n');
            if (firstNonWhitespace.Length > 0
                && firstNonWhitespace[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')
            {
                value = $"'{firstNonWhitespace}";
            }

            output.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }

        output.Append("\r\n");
    }
}
