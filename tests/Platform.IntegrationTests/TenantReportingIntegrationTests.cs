using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Data;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Domain;

namespace Platform.IntegrationTests;

public sealed class TenantReportingIntegrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task DashboardAndPagedAuditReportAreTenantScoped()
    {
        var scenario = await SeedScenarioAsync(fixture.Services);
        using var anonymous = fixture.CreateClient();
        using var anonymousResponse = await anonymous.GetAsync("/api/v1/tenant/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var reader = await LoginAsync(scenario.ReaderEmail, scenario.Password);
        reader.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantAId.ToString());
        using var readerDashboard = await reader.GetAsync("/api/v1/tenant/dashboard");
        Assert.Equal(HttpStatusCode.OK, readerDashboard.StatusCode);
        using var readerReport = await reader.GetAsync("/api/v1/tenant/reports/audit?skip=0&take=1");
        Assert.Equal(HttpStatusCode.OK, readerReport.StatusCode);
        using var readerExport = await reader.GetAsync("/api/v1/tenant/reports/audit/export.csv");
        Assert.Equal(HttpStatusCode.Forbidden, readerExport.StatusCode);

        using var admin = await LoginAsync(scenario.AdminEmail, scenario.Password);
        admin.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantAId.ToString());
        using var dashboardResponse = await admin.GetAsync("/api/v1/tenant/dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        using (var dashboard = JsonDocument.Parse(await dashboardResponse.Content.ReadAsStringAsync()))
        {
            var root = dashboard.RootElement;
            Assert.Equal(2, root.GetProperty("activeMembers").GetInt32());
            Assert.Equal(1, root.GetProperty("suspendedMembers").GetInt32());
            Assert.Equal(1, root.GetProperty("storedFiles").GetInt32());
            Assert.Equal(2, root.GetProperty("auditEventsLast30Days").GetInt32());
        }

        using var invalidPage = await admin.GetAsync("/api/v1/tenant/reports/audit?skip=-1&take=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        using var oversizedExport = await admin.GetAsync("/api/v1/tenant/reports/audit/export.csv?take=1001");
        Assert.Equal(HttpStatusCode.BadRequest, oversizedExport.StatusCode);

        using var firstPage = await admin.GetAsync("/api/v1/tenant/reports/audit?skip=0&take=1");
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        using (var page = JsonDocument.Parse(await firstPage.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, page.RootElement.GetProperty("items").GetArrayLength());
            Assert.True(page.RootElement.GetProperty("hasMore").GetBoolean());
            var firstItem = page.RootElement.GetProperty("items")[0];
            Assert.Equal("report-a-recent", firstItem.GetProperty("resourceId").GetString());
            Assert.False(firstItem.TryGetProperty("tenantId", out _));
            Assert.False(firstItem.TryGetProperty("metadataJson", out _));
        }

        using var secondPage = await admin.GetAsync("/api/v1/tenant/reports/audit?skip=1&take=1");
        using (var page = JsonDocument.Parse(await secondPage.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, page.RootElement.GetProperty("items").GetArrayLength());
            Assert.True(page.RootElement.GetProperty("hasMore").GetBoolean());
        }

        using var lastPage = await admin.GetAsync("/api/v1/tenant/reports/audit?skip=2&take=1");
        using (var page = JsonDocument.Parse(await lastPage.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, page.RootElement.GetProperty("items").GetArrayLength());
            Assert.False(page.RootElement.GetProperty("hasMore").GetBoolean());
        }

        using var exported = await admin.GetAsync("/api/v1/tenant/reports/audit/export.csv?skip=0&take=100");
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        Assert.Equal("text/csv", exported.Content.Headers.ContentType?.MediaType);
        var csv = await exported.Content.ReadAsStringAsync();
        Assert.Contains("id,occurredAtUtc,action,resourceType,resourceId,actorUserId", csv);
        Assert.Contains("'=HYPERLINK", csv);
        Assert.DoesNotContain("tenant-b-only", csv);
        Assert.DoesNotContain("sensitive-secret", csv);

        admin.DefaultRequestHeaders.Remove("X-Tenant-ID");
        admin.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantBId.ToString());
        using var crossTenantDashboard = await admin.GetAsync("/api/v1/tenant/dashboard");
        using (var dashboard = JsonDocument.Parse(await crossTenantDashboard.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, dashboard.RootElement.GetProperty("activeMembers").GetInt32());
            Assert.Equal(1, dashboard.RootElement.GetProperty("storedFiles").GetInt32());
            Assert.Equal(1, dashboard.RootElement.GetProperty("auditEventsLast30Days").GetInt32());
        }

        using var tenantBReport = await admin.GetAsync("/api/v1/tenant/reports/audit?take=100");
        using (var page = JsonDocument.Parse(await tenantBReport.Content.ReadAsStringAsync()))
        {
            var item = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal("tenant-b-only", item.GetProperty("resourceId").GetString());
        }
    }

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = fixture.CreateClient();
        var token = await GetCsrfTokenAsync(client);
        using var login = await PostJsonWithCsrfAsync(client, "/api/v1/auth/login", new { email, password }, token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private async Task<ReportingScenario> SeedScenarioAsync(IServiceProvider services)
    {
        const string password = "CWP-Reporting-Test-642!Secure";
        var adminEmail = $"{Guid.NewGuid():N}@example.test";
        var readerEmail = $"{Guid.NewGuid():N}@example.test";
        var suspendedEmail = $"{Guid.NewGuid():N}@example.test";
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<PlatformUser>>();
        var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var admin = CreateUser(adminEmail, "Reporting Admin");
        var reader = CreateUser(readerEmail, "Reporting Reader");
        var suspendedUser = CreateUser(suspendedEmail, "Suspended Member");
        Assert.True((await userManager.CreateAsync(admin, password)).Succeeded);
        Assert.True((await userManager.CreateAsync(reader, password)).Succeeded);
        Assert.True((await userManager.CreateAsync(suspendedUser, password)).Succeeded);

        var tenantA = CreateTenant("report-a");
        var tenantB = CreateTenant("report-b");
        var adminMembershipA = CreateMembership(tenantA.Id, admin.Id);
        var adminMembershipB = CreateMembership(tenantB.Id, admin.Id);
        var readerMembership = CreateMembership(tenantA.Id, reader.Id);
        var suspendedMembership = CreateMembership(tenantA.Id, suspendedUser.Id, MembershipStatus.Suspended);
        var adminRoleA = CreateRole(tenantA.Id, "Report Admin");
        var adminRoleB = CreateRole(tenantB.Id, "Report Admin");
        var readerRole = CreateRole(tenantA.Id, "Report Reader");

        database.Tenants.AddRange(tenantA, tenantB);
        database.Memberships.AddRange(adminMembershipA, adminMembershipB, readerMembership, suspendedMembership);
        database.TenantRoles.AddRange(adminRoleA, adminRoleB, readerRole);
        database.MembershipRoles.AddRange(
            CreateAssignment(tenantA.Id, adminMembershipA.Id, adminRoleA.Id),
            CreateAssignment(tenantB.Id, adminMembershipB.Id, adminRoleB.Id),
            CreateAssignment(tenantA.Id, readerMembership.Id, readerRole.Id));
        database.TenantRolePermissions.AddRange(
            CreatePermission(tenantA.Id, adminRoleA.Id, PermissionCodes.ReportsRead),
            CreatePermission(tenantA.Id, adminRoleA.Id, PermissionCodes.ReportsExport),
            CreatePermission(tenantB.Id, adminRoleB.Id, PermissionCodes.ReportsRead),
            CreatePermission(tenantB.Id, adminRoleB.Id, PermissionCodes.ReportsExport),
            CreatePermission(tenantA.Id, readerRole.Id, PermissionCodes.ReportsRead));

        var now = DateTimeOffset.UtcNow;
        database.AuditRecords.AddRange(
            CreateAudit(tenantA.Id, now.AddMinutes(-1), " =HYPERLINK(\"https://example.test\")", "report-a-recent"),
            CreateAudit(tenantA.Id, now.AddMinutes(-2), "tenant.member.added", "report-a-recent-2", """{"note":"sensitive-secret"}"""),
            CreateAudit(tenantA.Id, now.AddDays(-40), "tenant.member.added", "report-a-old"),
            CreateAudit(tenantB.Id, now.AddMinutes(-1), "tenant.member.added", "tenant-b-only"));
        database.TenantFiles.AddRange(
            CreateFile(tenantA.Id, "report-a.pdf"),
            CreateFile(tenantB.Id, "report-b.pdf"));
        await database.SaveChangesAsync();

        return new ReportingScenario(tenantA.Id, tenantB.Id, adminEmail, readerEmail, password);
    }

    private static PlatformUser CreateUser(string email, string displayName)
    {
        return new PlatformUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = displayName,
            Status = "Active",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static Tenant CreateTenant(string name)
    {
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = $"{name}-{Guid.NewGuid():N}",
            Status = "Active",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static TenantMembership CreateMembership(
        Guid tenantId,
        Guid userId,
        MembershipStatus status = MembershipStatus.Active)
    {
        return new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Status = status.ToString(),
            JoinedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static TenantRole CreateRole(Guid tenantId, string name)
    {
        return new TenantRole
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            IsSystem = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static MembershipRole CreateAssignment(Guid tenantId, Guid membershipId, Guid roleId)
    {
        return new MembershipRole
        {
            TenantId = tenantId,
            MembershipId = membershipId,
            RoleId = roleId,
            AssignedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static TenantRolePermission CreatePermission(Guid tenantId, Guid roleId, string code)
    {
        return new TenantRolePermission
        {
            TenantId = tenantId,
            RoleId = roleId,
            PermissionCode = code,
        };
    }

    private static AuditRecord CreateAudit(
        Guid tenantId,
        DateTimeOffset occurredAt,
        string action,
        string resourceId,
        string? metadata = null)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = Guid.NewGuid(),
            Action = action,
            ResourceType = "membership",
            ResourceId = resourceId,
            OccurredAtUtc = occurredAt,
            CorrelationId = Guid.NewGuid().ToString("N"),
            MetadataJson = metadata,
        };
    }

    private static Platform.Modules.Files.Domain.TenantFile CreateFile(Guid tenantId, string name)
    {
        return new Platform.Modules.Files.Domain.TenantFile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UploadedByUserId = Guid.NewGuid(),
            FileName = name,
            ContentType = "application/pdf",
            SizeBytes = 100,
            UploadedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostJsonWithCsrfAsync(
        HttpClient client,
        string path,
        object body,
        string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private sealed record ReportingScenario(
        Guid TenantAId,
        Guid TenantBId,
        string AdminEmail,
        string ReaderEmail,
        string Password);
}
