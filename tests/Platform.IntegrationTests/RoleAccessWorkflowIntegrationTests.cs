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

public sealed class RoleAccessWorkflowIntegrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task RoleAccessRequestsAreTenantScopedAndRequireIndependentApproval()
    {
        var scenario = await SeedScenarioAsync();
        using var requester = await LoginAsync(scenario.RequesterEmail, scenario.Password);
        SetTenant(requester, scenario.TenantAId);

        using var unauthenticated = fixture.CreateClient();
        using var anonymousResponse = await unauthenticated.PostAsJsonAsync(
            "/api/v1/tenant/role-access-requests",
            new { roleId = scenario.RequestedRoleId });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var csrfToken = await GetCsrfTokenAsync(requester);
        using var created = await PostJsonWithCsrfAsync(
            requester,
            "/api/v1/tenant/role-access-requests",
            new { roleId = scenario.RequestedRoleId },
            csrfToken);
        Assert.True(
            created.StatusCode == HttpStatusCode.Created,
            $"Expected Created but received {(int)created.StatusCode}: {await created.Content.ReadAsStringAsync()}");
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var requestId = createdBody.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(
            $"/api/v1/tenant/role-access-requests/{requestId}",
            created.Headers.Location?.OriginalString);
        Assert.Equal("Pending", createdBody.RootElement.GetProperty("status").GetString());
        Assert.Equal("Analyst", createdBody.RootElement.GetProperty("roleName").GetString());
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var requestedAudit = await database.AuditRecords.SingleAsync(item =>
                item.TenantId == scenario.TenantAId
                && item.Action == "tenant.role_access.requested"
                && item.ResourceId == requestId.ToString());
            Assert.Equal(scenario.RequesterUserId, requestedAudit.ActorUserId);
        }

        using var mine = await requester.GetAsync("/api/v1/tenant/role-access-requests/mine");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        using (var mineBody = JsonDocument.Parse(await mine.Content.ReadAsStringAsync()))
        {
            Assert.Equal(requestId, mineBody.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
        }

        using var invalidPage = await requester.GetAsync("/api/v1/tenant/role-access-requests/mine?take=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);

        using var requestDetails = await requester.GetAsync($"/api/v1/tenant/role-access-requests/{requestId}");
        Assert.Equal(HttpStatusCode.OK, requestDetails.StatusCode);

        using var requesterPending = await requester.GetAsync("/api/v1/tenant/role-access-requests/pending");
        Assert.Equal(HttpStatusCode.Forbidden, requesterPending.StatusCode);

        csrfToken = await GetCsrfTokenAsync(requester);
        using var duplicate = await PostJsonWithCsrfAsync(
            requester,
            "/api/v1/tenant/role-access-requests",
            new { roleId = scenario.RequestedRoleId },
            csrfToken);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var manager = await LoginAsync(scenario.ManagerEmail, scenario.Password);
        SetTenant(manager, scenario.TenantAId);
        using var pending = await manager.GetAsync("/api/v1/tenant/role-access-requests/pending");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        using (var pendingBody = JsonDocument.Parse(await pending.Content.ReadAsStringAsync()))
        {
            Assert.Contains(
                pendingBody.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("id").GetGuid() == requestId);
        }

        csrfToken = await GetCsrfTokenAsync(requester);
        using var forbiddenDecision = await PostEmptyWithCsrfAsync(
            requester,
            $"/api/v1/tenant/role-access-requests/{requestId}/approve",
            csrfToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDecision.StatusCode);

        csrfToken = await GetCsrfTokenAsync(manager);
        using var approved = await PostEmptyWithCsrfAsync(
            manager,
            $"/api/v1/tenant/role-access-requests/{requestId}/approve",
            csrfToken);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        using (var approvedBody = JsonDocument.Parse(await approved.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Approved", approvedBody.RootElement.GetProperty("status").GetString());
            Assert.Equal(scenario.RequestedRoleId, approvedBody.RootElement.GetProperty("roleId").GetGuid());
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            Assert.True(await database.MembershipRoles.AnyAsync(item =>
                item.TenantId == scenario.TenantAId
                && item.MembershipId == scenario.RequesterMembershipId
                && item.RoleId == scenario.RequestedRoleId));
            var audit = await database.AuditRecords.SingleAsync(item =>
                item.TenantId == scenario.TenantAId
                && item.Action == "tenant.role_access.approved"
                && item.ResourceId == requestId.ToString());
            Assert.Equal(scenario.ManagerUserId, audit.ActorUserId);
            using var metadata = JsonDocument.Parse(audit.MetadataJson!);
            Assert.Equal(scenario.RequestedRoleId, Guid.Parse(metadata.RootElement.GetProperty("roleId").GetString()!));
            Assert.Equal(scenario.RequesterMembershipId, Guid.Parse(metadata.RootElement.GetProperty("membershipId").GetString()!));
        }

        using var updatedDetails = await requester.GetAsync($"/api/v1/tenant/role-access-requests/{requestId}");
        using (var updatedBody = JsonDocument.Parse(await updatedDetails.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Approved", updatedBody.RootElement.GetProperty("status").GetString());
            Assert.Equal(scenario.ManagerUserId, updatedBody.RootElement.GetProperty("decidedByUserId").GetGuid());
        }

        csrfToken = await GetCsrfTokenAsync(manager);
        using var repeatDecision = await PostEmptyWithCsrfAsync(
            manager,
            $"/api/v1/tenant/role-access-requests/{requestId}/approve",
            csrfToken);
        Assert.Equal(HttpStatusCode.Conflict, repeatDecision.StatusCode);

        csrfToken = await GetCsrfTokenAsync(manager);
        using var ownRequest = await PostJsonWithCsrfAsync(
            manager,
            "/api/v1/tenant/role-access-requests",
            new { roleId = scenario.SecondRequestedRoleId },
            csrfToken);
        Assert.Equal(HttpStatusCode.Created, ownRequest.StatusCode);
        using var ownRequestBody = JsonDocument.Parse(await ownRequest.Content.ReadAsStringAsync());
        var ownRequestId = ownRequestBody.RootElement.GetProperty("id").GetGuid();

        csrfToken = await GetCsrfTokenAsync(manager);
        using var selfApproval = await PostEmptyWithCsrfAsync(
            manager,
            $"/api/v1/tenant/role-access-requests/{ownRequestId}/approve",
            csrfToken);
        Assert.Equal(HttpStatusCode.Conflict, selfApproval.StatusCode);

        using var otherManager = await LoginAsync(scenario.OtherManagerEmail, scenario.Password);
        SetTenant(otherManager, scenario.TenantAId);
        csrfToken = await GetCsrfTokenAsync(otherManager);
        using var rejected = await PostEmptyWithCsrfAsync(
            otherManager,
            $"/api/v1/tenant/role-access-requests/{ownRequestId}/reject",
            csrfToken);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        using (var rejectedBody = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Rejected", rejectedBody.RootElement.GetProperty("status").GetString());
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            Assert.False(await database.MembershipRoles.AnyAsync(item =>
                item.TenantId == scenario.TenantAId
                && item.MembershipId == scenario.ManagerMembershipId
                && item.RoleId == scenario.SecondRequestedRoleId));
            Assert.True(await database.AuditRecords.AnyAsync(item =>
                item.TenantId == scenario.TenantAId
                && item.Action == "tenant.role_access.rejected"
                && item.ResourceId == ownRequestId.ToString()));
        }

        SetTenant(manager, scenario.TenantBId);
        csrfToken = await GetCsrfTokenAsync(manager);
        using var crossTenantDecision = await PostEmptyWithCsrfAsync(
            manager,
            $"/api/v1/tenant/role-access-requests/{requestId}/approve",
            csrfToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantDecision.StatusCode);
    }

    private async Task<RoleAccessScenario> SeedScenarioAsync()
    {
        const string password = "CWP-Role-Workflow-642!Secure";
        var managerEmail = $"{Guid.NewGuid():N}@example.test";
        var otherManagerEmail = $"{Guid.NewGuid():N}@example.test";
        var requesterEmail = $"{Guid.NewGuid():N}@example.test";
        await using var scope = fixture.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PlatformUser>>();
        var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var manager = CreateUser(managerEmail, "Role Workflow Manager");
        var otherManager = CreateUser(otherManagerEmail, "Role Workflow Reviewer");
        var requester = CreateUser(requesterEmail, "Role Requester");
        Assert.True((await users.CreateAsync(manager, password)).Succeeded);
        Assert.True((await users.CreateAsync(otherManager, password)).Succeeded);
        Assert.True((await users.CreateAsync(requester, password)).Succeeded);

        var tenantA = CreateTenant("role-workflow-a");
        var tenantB = CreateTenant("role-workflow-b");
        var managerMembership = CreateMembership(tenantA.Id, manager.Id);
        var managerMembershipB = CreateMembership(tenantB.Id, manager.Id);
        var otherManagerMembership = CreateMembership(tenantA.Id, otherManager.Id);
        var requesterMembership = CreateMembership(tenantA.Id, requester.Id);
        var managerRole = CreateRole(tenantA.Id, "Role Workflow Manager");
        var managerRoleB = CreateRole(tenantB.Id, "Role Workflow Manager");
        var otherManagerRole = CreateRole(tenantA.Id, "Role Workflow Reviewer");
        var requesterRole = CreateRole(tenantA.Id, "Role Requester");
        var requestedRole = CreateRole(tenantA.Id, "Analyst");
        var secondRequestedRole = CreateRole(tenantA.Id, "Auditor");

        database.Tenants.AddRange(tenantA, tenantB);
        database.Memberships.AddRange(managerMembership, managerMembershipB, otherManagerMembership, requesterMembership);
        database.TenantRoles.AddRange(
            managerRole,
            managerRoleB,
            otherManagerRole,
            requesterRole,
            requestedRole,
            secondRequestedRole);
        database.MembershipRoles.AddRange(
            CreateAssignment(tenantA.Id, managerMembership.Id, managerRole.Id),
            CreateAssignment(tenantB.Id, managerMembershipB.Id, managerRoleB.Id),
            CreateAssignment(tenantA.Id, otherManagerMembership.Id, otherManagerRole.Id),
            CreateAssignment(tenantA.Id, requesterMembership.Id, requesterRole.Id));
        database.TenantRolePermissions.AddRange(
            CreatePermission(tenantA.Id, managerRole.Id, PermissionCodes.WorkflowsManage),
            CreatePermission(tenantB.Id, managerRoleB.Id, PermissionCodes.WorkflowsManage),
            CreatePermission(tenantA.Id, otherManagerRole.Id, PermissionCodes.WorkflowsManage));
        await database.SaveChangesAsync();

        return new RoleAccessScenario(
            tenantA.Id,
            tenantB.Id,
            manager.Id,
            managerMembership.Id,
            requesterMembership.Id,
            requester.Id,
            requestedRole.Id,
            secondRequestedRole.Id,
            managerEmail,
            otherManagerEmail,
            requesterEmail,
            password);
    }

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = fixture.CreateClient();
        var token = await GetCsrfTokenAsync(client);
        using var login = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/auth/login",
            new { email, password },
            token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private static void SetTenant(HttpClient client, Guid tenantId)
    {
        client.DefaultRequestHeaders.Remove("X-Tenant-ID");
        client.DefaultRequestHeaders.Add("X-Tenant-ID", tenantId.ToString());
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

    private static async Task<HttpResponseMessage> PostEmptyWithCsrfAsync(
        HttpClient client,
        string path,
        string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
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

    private static TenantMembership CreateMembership(Guid tenantId, Guid userId)
    {
        return new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Status = MembershipStatus.Active.ToString(),
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
            IsSystem = false,
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

    private sealed record RoleAccessScenario(
        Guid TenantAId,
        Guid TenantBId,
        Guid ManagerUserId,
        Guid ManagerMembershipId,
        Guid RequesterMembershipId,
        Guid RequesterUserId,
        Guid RequestedRoleId,
        Guid SecondRequestedRoleId,
        string ManagerEmail,
        string OtherManagerEmail,
        string RequesterEmail,
        string Password);
}
