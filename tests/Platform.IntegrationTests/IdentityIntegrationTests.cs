using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Data;
using Platform.Modules.Identity.Application;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Domain;

namespace Platform.IntegrationTests;

public sealed class IdentityIntegrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public void SessionCookieHasFixedThirtyMinuteLifetime()
    {
        var options = fixture.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal(TimeSpan.FromMinutes(30), options.ExpireTimeSpan);
        Assert.False(options.SlidingExpiration);
        Assert.Equal("CWP.Auth", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, options.Cookie.SameSite);
    }

    [Fact]
    public async Task CookieSessionSupportsRegistrationLoginCurrentUserAndLogout()
    {
        var client = fixture.Client;
        var initialToken = await GetCsrfTokenAsync(client);
        const string email = "developer@example.test";
        const string password = "CWP-Local-Password-938!";

        using var register = await PostJsonWithCsrfAsync(client, "/api/v1/auth/register", new
        {
            email,
            displayName = "Local Developer",
            password,
        }, initialToken);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        await AssertPasswordIsHashedAsync(fixture.Services, email, password);

        var loginToken = await GetCsrfTokenAsync(client);
        using var login = await PostJsonWithCsrfAsync(client, "/api/v1/auth/login", new { email, password }, loginToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("CWP.Auth=", StringComparison.Ordinal));
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        using var me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using (var meBody = JsonDocument.Parse(await me.Content.ReadAsStringAsync()))
        {
            Assert.Equal(email, meBody.RootElement.GetProperty("email").GetString());
        }

        var authenticatedToken = await GetCsrfTokenAsync(client);
        using var logout = await PostEmptyWithCsrfAsync(client, "/api/v1/auth/logout", authenticatedToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var afterLogout = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);

        var correlations = new[]
        {
            (Response: register, Action: "auth.account.registered"),
            (Response: login, Action: "auth.login.succeeded"),
            (Response: logout, Action: "auth.logout"),
        };
        using var signedInBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var signedInUserId = signedInBody.RootElement.GetProperty("id").GetGuid();
        await using var auditScope = fixture.Services.CreateAsyncScope();
        var database = auditScope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        foreach (var (response, expectedAction) in correlations)
        {
            var correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
            var record = await database.AuditRecords.SingleAsync(item => item.CorrelationId == correlationId);
            Assert.Equal(expectedAction, record.Action);
            Assert.Equal(signedInUserId, record.ActorUserId);
            Assert.Null(record.MetadataJson);
        }
    }

    [Fact]
    public async Task LoginRequiresAntiforgeryAndUsesGenericFailureResponse()
    {
        using var missingToken = await fixture.Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "unknown@example.test",
            password = "CWP-Local-Password-938!",
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        using (var missingTokenBody = JsonDocument.Parse(await missingToken.Content.ReadAsStringAsync()))
        {
            Assert.Equal("antiforgery_validation_failed", missingTokenBody.RootElement.GetProperty("code").GetString());
        }

        var token = await GetCsrfTokenAsync(fixture.Client);
        using var failedLogin = await PostJsonWithCsrfAsync(fixture.Client, "/api/v1/auth/login", new
        {
            email = "unknown@example.test",
            password = "CWP-Local-Password-938!",
        }, token);
        Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);
        using var failureBody = JsonDocument.Parse(await failedLogin.Content.ReadAsStringAsync());
        Assert.Equal("The email or password is incorrect.", failureBody.RootElement.GetProperty("title").GetString());
        Assert.Equal("authentication_failed", failureBody.RootElement.GetProperty("code").GetString());

        var correlationId = Assert.Single(failedLogin.Headers.GetValues("X-Correlation-ID"));
        await using var auditScope = fixture.Services.CreateAsyncScope();
        var database = auditScope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var failedLoginAudit = await database.AuditRecords.SingleAsync(item => item.CorrelationId == correlationId);
        Assert.Equal("auth.login.failed", failedLoginAudit.Action);
        Assert.Equal("session", failedLoginAudit.ResourceType);
        Assert.Null(failedLoginAudit.ResourceId);
        Assert.Null(failedLoginAudit.ActorUserId);
        Assert.Null(failedLoginAudit.MetadataJson);
    }

    [Fact]
    public async Task RevokeAllSessionsEndsTheAuthenticatedSession()
    {
        var client = fixture.Client;
        using var secondClient = fixture.CreateClient();
        var token = await GetCsrfTokenAsync(client);
        const string email = "revoke@example.test";
        const string password = "CWP-Local-Password-938!";

        using var register = await PostJsonWithCsrfAsync(client, "/api/v1/auth/register", new
        {
            email,
            displayName = "Revocation Test",
            password,
        }, token);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var login = await PostJsonWithCsrfAsync(client, "/api/v1/auth/login", new { email, password }, token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var secondToken = await GetCsrfTokenAsync(secondClient);
        using var secondLogin = await PostJsonWithCsrfAsync(secondClient, "/api/v1/auth/login", new { email, password }, secondToken);
        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);
        using var secondMeBeforeRevocation = await secondClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, secondMeBeforeRevocation.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var revoke = await PostEmptyWithCsrfAsync(client, "/api/v1/auth/sessions/revoke-all", token);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        using var me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        using var secondMeAfterRevocation = await secondClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, secondMeAfterRevocation.StatusCode);

        var correlationId = Assert.Single(revoke.Headers.GetValues("X-Correlation-ID"));
        await using var auditScope = fixture.Services.CreateAsyncScope();
        var database = auditScope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var revokeAudit = await database.AuditRecords.SingleAsync(item => item.CorrelationId == correlationId);
        Assert.Equal("auth.sessions.revoked", revokeAudit.Action);
        Assert.Equal("user", revokeAudit.ResourceType);
        Assert.Null(revokeAudit.MetadataJson);
    }

    [Fact]
    public async Task TenantPermissionPoliciesAndRoleAssignmentsStayWithinTheVerifiedTenant()
    {
        var scenario = await SeedAuthorizationScenarioAsync(fixture.Services);
        using var client = fixture.CreateClient();
        var token = await GetCsrfTokenAsync(client);
        using var login = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/auth/login",
            new { email = scenario.UserEmail, password = scenario.Password },
            token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        client.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantId.ToString());
        using var roles = await client.GetAsync("/api/v1/tenant/roles");
        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);

        using var deniedMemberships = await client.GetAsync("/api/v1/tenant/memberships");
        Assert.Equal(HttpStatusCode.Forbidden, deniedMemberships.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var createRole = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/tenant/roles",
            new CreateTenantRoleRequest("Member Reader", [PermissionCodes.MembersRead]),
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.Created, createRole.StatusCode);
        var createdRole = await createRole.Content.ReadFromJsonAsync<TenantRoleSummary>();
        Assert.NotNull(createdRole);

        token = await GetCsrfTokenAsync(client);
        using var assignment = await PostEmptyWithCsrfAsync(
            client,
            $"/api/v1/tenant/memberships/{scenario.MembershipId}/roles/{createdRole.Id}",
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.NoContent, assignment.StatusCode);
        using var repeatedAssignment = await PostEmptyWithCsrfAsync(
            client,
            $"/api/v1/tenant/memberships/{scenario.MembershipId}/roles/{createdRole.Id}",
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.NoContent, repeatedAssignment.StatusCode);

        using var allowedMemberships = await client.GetAsync("/api/v1/tenant/memberships");
        Assert.Equal(HttpStatusCode.OK, allowedMemberships.StatusCode);

        using var crossTenantAssignment = await PostEmptyWithCsrfAsync(
            client,
            $"/api/v1/tenant/memberships/{scenario.OtherTenantMembershipId}/roles/{scenario.OtherTenantRoleId}",
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantAssignment.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Tenant-ID");
        using var missingTenant = await client.GetAsync("/api/v1/tenant/roles");
        Assert.Equal(HttpStatusCode.Forbidden, missingTenant.StatusCode);

        await using var databaseScope = fixture.Services.CreateAsyncScope();
        var database = databaseScope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        database.MembershipRoles.Add(new MembershipRole
        {
            TenantId = scenario.TenantId,
            MembershipId = scenario.OtherTenantMembershipId,
            RoleId = scenario.OtherTenantRoleId,
            AssignedAtUtc = DateTimeOffset.UtcNow,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task TenantMemberManagementEnforcesPermissionsAndScopesMemberLifecycleAndRoles()
    {
        var scenario = await SeedAuthorizationScenarioAsync(fixture.Services);
        const string targetEmail = "new-member@example.test";
        const string targetPassword = "CWP-Member-Management-Password-284!";
        const string readerEmail = "member-reader@example.test";
        const string readerPassword = "CWP-Member-Management-Password-285!";

        using var anonymousClient = fixture.CreateClient();
        using var anonymousMembers = await anonymousClient.GetAsync("/api/v1/tenant/members");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousMembers.StatusCode);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<PlatformUser>>();
            var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var targetUser = new PlatformUser
            {
                Id = Guid.NewGuid(),
                UserName = targetEmail,
                Email = targetEmail,
                DisplayName = "New Tenant Member",
                Status = "Active",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            var targetResult = await userManager.CreateAsync(targetUser, targetPassword);
            Assert.True(targetResult.Succeeded, string.Join("; ", targetResult.Errors.Select(error => error.Code)));

            var readerUser = new PlatformUser
            {
                Id = Guid.NewGuid(),
                UserName = readerEmail,
                Email = readerEmail,
                DisplayName = "Unprivileged Member",
                Status = "Active",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            var readerResult = await userManager.CreateAsync(readerUser, readerPassword);
            Assert.True(readerResult.Succeeded, string.Join("; ", readerResult.Errors.Select(error => error.Code)));
            database.Memberships.Add(CreateMembership(scenario.TenantId, readerUser.Id));

            var managerRole = await database.TenantRoles.SingleAsync(
                role => role.TenantId == scenario.TenantId && role.Name == "Role Manager");
            database.TenantRolePermissions.AddRange(
                new TenantRolePermission
                {
                    TenantId = scenario.TenantId,
                    RoleId = managerRole.Id,
                    PermissionCode = PermissionCodes.MembersRead,
                },
                new TenantRolePermission
                {
                    TenantId = scenario.TenantId,
                    RoleId = managerRole.Id,
                    PermissionCode = PermissionCodes.MembersManage,
                });
            await database.SaveChangesAsync();
        }

        using var client = fixture.CreateClient();
        var token = await GetCsrfTokenAsync(client);
        using var login = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/auth/login",
            new { email = scenario.UserEmail, password = scenario.Password },
            token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantId.ToString());

        using var initialMembers = await client.GetAsync("/api/v1/tenant/members");
        Assert.Equal(HttpStatusCode.OK, initialMembers.StatusCode);
        using (var body = JsonDocument.Parse(await initialMembers.Content.ReadAsStringAsync()))
        {
            Assert.Equal(2, body.RootElement.GetArrayLength());
        }

        using var ownMember = await client.GetAsync($"/api/v1/tenant/members/{scenario.MembershipId}");
        Assert.Equal(HttpStatusCode.OK, ownMember.StatusCode);
        using var crossTenantMember = await client.GetAsync($"/api/v1/tenant/members/{scenario.OtherTenantMembershipId}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantMember.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var added = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/tenant/memberships",
            new { email = targetEmail },
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        using var addedBody = JsonDocument.Parse(await added.Content.ReadAsStringAsync());
        var targetMembershipId = addedBody.RootElement.GetProperty("membershipId").GetGuid();

        token = await GetCsrfTokenAsync(client);
        using var duplicate = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/tenant/memberships",
            new { email = targetEmail },
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var invalidEmail = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/tenant/memberships",
            new { email = "not-an-email" },
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.BadRequest, invalidEmail.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var unknown = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/tenant/memberships",
            new { email = "unknown-member@example.test" },
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var selfSuspend = await PutJsonWithCsrfAsync(
            client,
            $"/api/v1/tenant/memberships/{scenario.MembershipId}/status",
            new { status = "Suspended" },
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.Conflict, selfSuspend.StatusCode);
        using (var selfSuspendBody = JsonDocument.Parse(await selfSuspend.Content.ReadAsStringAsync()))
        {
            Assert.Equal("cannot_suspend_self", selfSuspendBody.RootElement.GetProperty("code").GetString());
        }

        token = await GetCsrfTokenAsync(client);
        using var invalidStatus = await PutJsonWithCsrfAsync(
            client,
            $"/api/v1/tenant/memberships/{targetMembershipId}/status",
            new { status = "Paused" },
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var missingMemberStatus = await PutJsonWithCsrfAsync(
            client,
            $"/api/v1/tenant/memberships/{Guid.NewGuid()}/status",
            new { status = "Active" },
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.NotFound, missingMemberStatus.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var crossTenantUpdate = await PutJsonWithCsrfAsync(
            client,
            $"/api/v1/tenant/memberships/{scenario.OtherTenantMembershipId}/status",
            new { status = "Suspended" },
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantUpdate.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var suspend = await PutJsonWithCsrfAsync(
            client,
            $"/api/v1/tenant/memberships/{targetMembershipId}/status",
            new { status = "Suspended" },
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        using (var suspendedBody = JsonDocument.Parse(await suspend.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Suspended", suspendedBody.RootElement.GetProperty("status").GetString());
        }

        token = await GetCsrfTokenAsync(client);
        using var reactivate = await PutJsonWithCsrfAsync(
            client,
            $"/api/v1/tenant/memberships/{targetMembershipId}/status",
            new { status = "Active" },
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var createRole = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/tenant/roles",
            new CreateTenantRoleRequest("Member Manager Test", [PermissionCodes.MembersRead]),
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.Created, createRole.StatusCode);
        var createdRole = await createRole.Content.ReadFromJsonAsync<TenantRoleSummary>();
        Assert.NotNull(createdRole);

        token = await GetCsrfTokenAsync(client);
        using var assignment = await PostEmptyWithCsrfAsync(
            client,
            $"/api/v1/tenant/memberships/{targetMembershipId}/roles/{createdRole.Id}",
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.NoContent, assignment.StatusCode);

        using var targetAfterAssignment = await client.GetAsync($"/api/v1/tenant/members/{targetMembershipId}");
        using (var targetBody = JsonDocument.Parse(await targetAfterAssignment.Content.ReadAsStringAsync()))
        {
            Assert.Equal(createdRole.Id, targetBody.RootElement.GetProperty("roles")[0].GetProperty("id").GetGuid());
        }

        using var removeRole = await DeleteEmptyWithCsrfAsync(
            client,
            $"/api/v1/tenant/memberships/{targetMembershipId}/roles/{createdRole.Id}",
            token,
            scenario.TenantId);
        Assert.Equal(HttpStatusCode.NoContent, removeRole.StatusCode);

        using var readerClient = fixture.CreateClient();
        token = await GetCsrfTokenAsync(readerClient);
        using var readerLogin = await PostJsonWithCsrfAsync(
            readerClient,
            "/api/v1/auth/login",
            new { email = readerEmail, password = readerPassword },
            token);
        Assert.Equal(HttpStatusCode.OK, readerLogin.StatusCode);
        readerClient.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantId.ToString());
        using var deniedList = await readerClient.GetAsync("/api/v1/tenant/members");
        Assert.Equal(HttpStatusCode.Forbidden, deniedList.StatusCode);

        await using var auditScope = fixture.Services.CreateAsyncScope();
        var auditDatabase = auditScope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var actions = await auditDatabase.AuditRecords
            .Where(item => item.TenantId == scenario.TenantId && item.ActorUserId == scenario.UserId)
            .Select(item => item.Action)
            .ToListAsync();
        Assert.Contains("tenant.member.added", actions);
        Assert.Contains("tenant.member.status_changed", actions);
        Assert.Contains("tenant.role.created", actions);
        Assert.Contains("tenant.role.assigned", actions);
        Assert.Contains("tenant.role.removed", actions);
    }

    [Fact]
    public async Task TenantSettingsArePermissionControlledValidatedAuditedAndTenantScoped()
    {
        var scenario = await SeedAuthorizationScenarioAsync(fixture.Services);
        using var client = fixture.CreateClient();
        var token = await GetCsrfTokenAsync(client);
        using var login = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/auth/login",
            new { email = scenario.UserEmail, password = scenario.Password },
            token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantId.ToString());

        using var anonymousClient = fixture.CreateClient();
        using var anonymousSettings = await anonymousClient.GetAsync("/api/v1/tenant/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousSettings.StatusCode);

        using var deniedList = await client.GetAsync("/api/v1/tenant/settings");
        Assert.Equal(HttpStatusCode.Forbidden, deniedList.StatusCode);

        Guid otherTenantId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var managerRole = await database.TenantRoles.SingleAsync(
                role => role.TenantId == scenario.TenantId && role.Name == "Role Manager");
            var readerRole = new TenantRole
            {
                Id = Guid.NewGuid(),
                TenantId = scenario.TenantId,
                Name = "Settings Reader",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            database.TenantRoles.Add(readerRole);
            database.MembershipRoles.Add(new MembershipRole
            {
                TenantId = scenario.TenantId,
                MembershipId = scenario.MembershipId,
                RoleId = readerRole.Id,
                AssignedAtUtc = DateTimeOffset.UtcNow,
            });
            database.TenantRolePermissions.Add(new TenantRolePermission
            {
                TenantId = scenario.TenantId,
                RoleId = readerRole.Id,
                PermissionCode = PermissionCodes.SettingsRead,
            });

            otherTenantId = await database.Memberships
                .Where(membership => membership.Id == scenario.OtherTenantMembershipId)
                .Select(membership => membership.TenantId)
                .SingleAsync();
            database.TenantSettings.Add(new TenantSetting
            {
                TenantId = otherTenantId,
                Key = "ui.theme",
                Value = "external-value",
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedByUserId = Guid.NewGuid(),
            });
            await database.SaveChangesAsync();

            token = await GetCsrfTokenAsync(client);
            using var readOnlyWrite = await PostJsonWithCsrfAsync(
                client,
                "/api/v1/tenant/settings",
                new { key = "ui.theme", value = "light" },
                token);
            Assert.Equal(HttpStatusCode.Forbidden, readOnlyWrite.StatusCode);

            database.TenantRolePermissions.Add(new TenantRolePermission
            {
                TenantId = scenario.TenantId,
                RoleId = managerRole.Id,
                PermissionCode = PermissionCodes.SettingsManage,
            });
            await database.SaveChangesAsync();
        }

        using var emptyList = await client.GetAsync("/api/v1/tenant/settings");
        Assert.Equal(HttpStatusCode.OK, emptyList.StatusCode);
        using (var body = JsonDocument.Parse(await emptyList.Content.ReadAsStringAsync()))
        {
            Assert.Empty(body.RootElement.EnumerateArray());
        }

        token = await GetCsrfTokenAsync(client);
        using var created = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/tenant/settings",
            new { key = "ui.theme", value = "light" },
            token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/v1/tenant/settings/ui.theme", created.Headers.Location?.OriginalString);

        token = await GetCsrfTokenAsync(client);
        using var duplicate = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/tenant/settings",
            new { key = "UI.Theme", value = "dark" },
            token);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var sensitiveKey = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/tenant/settings",
            new { key = "auth.api-token", value = "not-a-secret" },
            token);
        Assert.Equal(HttpStatusCode.BadRequest, sensitiveKey.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var oversizedValue = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/tenant/settings",
            new { key = "ui.description", value = new string('x', 2001) },
            token);
        Assert.Equal(HttpStatusCode.BadRequest, oversizedValue.StatusCode);

        using var getSetting = await client.GetAsync("/api/v1/tenant/settings/ui.theme");
        Assert.Equal(HttpStatusCode.OK, getSetting.StatusCode);
        using (var body = JsonDocument.Parse(await getSetting.Content.ReadAsStringAsync()))
        {
            Assert.Equal("light", body.RootElement.GetProperty("value").GetString());
        }

        token = await GetCsrfTokenAsync(client);
        using var updated = await PutJsonWithCsrfAsync(
            client,
            "/api/v1/tenant/settings/ui.theme",
            new { value = "dark" },
            token);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var list = await client.GetAsync("/api/v1/tenant/settings");
        using (var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync()))
        {
            var setting = Assert.Single(body.RootElement.EnumerateArray());
            Assert.Equal("dark", setting.GetProperty("value").GetString());
        }

        token = await GetCsrfTokenAsync(client);
        using var deleted = await DeleteEmptyWithCsrfAsync(client, "/api/v1/tenant/settings/ui.theme", token);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await client.GetAsync("/api/v1/tenant/settings/ui.theme");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var otherTenantSetting = await database.TenantSettings.SingleAsync(
                setting => setting.TenantId == otherTenantId && setting.Key == "ui.theme");
            Assert.Equal("external-value", otherTenantSetting.Value);

            var expectedActions = new[]
            {
                (Response: created, Action: "tenant.setting.created"),
                (Response: updated, Action: "tenant.setting.updated"),
                (Response: deleted, Action: "tenant.setting.deleted"),
            };
            foreach (var (response, expectedAction) in expectedActions)
            {
                var correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
                var auditRecord = await database.AuditRecords.SingleAsync(record => record.CorrelationId == correlationId);
                Assert.Equal(scenario.TenantId, auditRecord.TenantId);
                Assert.Equal(expectedAction, auditRecord.Action);
                Assert.DoesNotContain("light", auditRecord.MetadataJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("dark", auditRecord.MetadataJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("external-value", auditRecord.MetadataJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task TenantAuditIsAppendOnlyPermissionControlledPaginatedAndTenantScoped()
    {
        var scenario = await SeedAuthorizationScenarioAsync(fixture.Services);
        using var client = fixture.CreateClient();
        var token = await GetCsrfTokenAsync(client);
        using var login = await PostJsonWithCsrfAsync(
            client,
            "/api/v1/auth/login",
            new { email = scenario.UserEmail, password = scenario.Password },
            token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantId.ToString());

        using var anonymousClient = fixture.CreateClient();
        using var anonymousAudit = await anonymousClient.GetAsync("/api/v1/tenant/audit");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousAudit.StatusCode);

        using var deniedAudit = await client.GetAsync("/api/v1/tenant/audit");
        Assert.Equal(HttpStatusCode.Forbidden, deniedAudit.StatusCode);

        foreach (var roleName in new[] { "Audit Test Reader", "Audit Test Reporter" })
        {
            token = await GetCsrfTokenAsync(client);
            using var createRole = await PostJsonWithCsrfAsync(
                client,
                "/api/v1/tenant/roles",
                new CreateTenantRoleRequest(roleName, [PermissionCodes.MembersRead]),
                token,
                scenario.TenantId);
            Assert.Equal(HttpStatusCode.Created, createRole.StatusCode);
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var managerRole = await database.TenantRoles.SingleAsync(
                role => role.TenantId == scenario.TenantId && role.Name == "Role Manager");
            database.TenantRolePermissions.Add(new TenantRolePermission
            {
                TenantId = scenario.TenantId,
                RoleId = managerRole.Id,
                PermissionCode = PermissionCodes.AuditRead,
            });

            var otherTenantId = await database.Memberships
                .Where(membership => membership.Id == scenario.OtherTenantMembershipId)
                .Select(membership => membership.TenantId)
                .SingleAsync();
            database.AuditRecords.Add(new AuditRecord
            {
                Id = Guid.NewGuid(),
                TenantId = otherTenantId,
                ActorUserId = Guid.NewGuid(),
                Action = "tenant.role.created",
                ResourceType = "tenant_role",
                ResourceId = Guid.NewGuid().ToString(),
                OccurredAtUtc = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid().ToString("N"),
                MetadataJson = """{"permissionCount":"1"}""",
            });
            await database.SaveChangesAsync();
        }

        using var invalidPage = await client.GetAsync("/api/v1/tenant/audit?skip=0&take=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);

        using var firstPage = await client.GetAsync("/api/v1/tenant/audit?skip=0&take=1");
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        using var firstBody = JsonDocument.Parse(await firstPage.Content.ReadAsStringAsync());
        var firstItem = Assert.Single(firstBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(scenario.TenantId, firstItem.GetProperty("tenantId").GetGuid());
        Assert.Equal("tenant.role.created", firstItem.GetProperty("action").GetString());
        Assert.Equal(scenario.UserId, firstItem.GetProperty("actorUserId").GetGuid());
        Assert.True(firstBody.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.False(firstItem.GetProperty("metadataJson").GetString()!.Contains("password", StringComparison.OrdinalIgnoreCase));

        using var secondPage = await client.GetAsync("/api/v1/tenant/audit?skip=1&take=1");
        Assert.Equal(HttpStatusCode.OK, secondPage.StatusCode);
        using var secondBody = JsonDocument.Parse(await secondPage.Content.ReadAsStringAsync());
        var secondItem = Assert.Single(secondBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(scenario.TenantId, secondItem.GetProperty("tenantId").GetGuid());
        Assert.False(secondBody.RootElement.GetProperty("hasMore").GetBoolean());

        using var correlationRecords = await client.GetAsync($"/api/v1/tenant/audit?skip=0&take=100");
        using var allBody = JsonDocument.Parse(await correlationRecords.Content.ReadAsStringAsync());
        Assert.Equal(2, allBody.RootElement.GetProperty("items").GetArrayLength());
        Assert.All(
            allBody.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.True(Guid.TryParseExact(item.GetProperty("correlationId").GetString(), "N", out _)));
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static async Task AssertPasswordIsHashedAsync(IServiceProvider services, string email, string password)
    {
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<PlatformUser>>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<PlatformUser>>();
        var user = await userManager.FindByEmailAsync(email);

        Assert.NotNull(user?.PasswordHash);
        Assert.NotEqual(password, user.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, user.PasswordHash, password));
    }

    private static async Task<AuthorizationScenario> SeedAuthorizationScenarioAsync(IServiceProvider services)
    {
        const string password = "CWP-Role-Policy-Password-483!";
        var userEmail = $"{Guid.NewGuid():N}@example.test";
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<PlatformUser>>();
        var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            UserName = userEmail,
            Email = userEmail,
            DisplayName = "Role Policy Test",
            Status = "Active",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        var createUser = await userManager.CreateAsync(user, password);
        Assert.True(createUser.Succeeded, string.Join("; ", createUser.Errors.Select(error => error.Code)));

        var otherEmail = $"{Guid.NewGuid():N}@example.test";
        var otherUser = new PlatformUser
        {
            Id = Guid.NewGuid(),
            UserName = otherEmail,
            Email = otherEmail,
            DisplayName = "Other Tenant User",
            Status = "Active",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        var createOtherUser = await userManager.CreateAsync(otherUser, password);
        Assert.True(createOtherUser.Succeeded, string.Join("; ", createOtherUser.Errors.Select(error => error.Code)));

        var tenant = CreateTenant("role-policy");
        var otherTenant = CreateTenant("other-role-policy");
        var membership = CreateMembership(tenant.Id, user.Id);
        var otherTenantMembership = CreateMembership(otherTenant.Id, otherUser.Id);
        var managerRole = new TenantRole
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Role Manager",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        var otherTenantRole = new TenantRole
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenant.Id,
            Name = "Other Tenant Role",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        database.Tenants.AddRange(tenant, otherTenant);
        database.Memberships.AddRange(membership, otherTenantMembership);
        database.TenantRoles.AddRange(managerRole, otherTenantRole);
        database.MembershipRoles.Add(new MembershipRole
        {
            TenantId = tenant.Id,
            MembershipId = membership.Id,
            RoleId = managerRole.Id,
            AssignedAtUtc = DateTimeOffset.UtcNow,
        });
        database.TenantRolePermissions.Add(new TenantRolePermission
        {
            TenantId = tenant.Id,
            RoleId = managerRole.Id,
            PermissionCode = PermissionCodes.RolesManage,
        });
        await database.SaveChangesAsync();

        return new AuthorizationScenario(
            tenant.Id,
            user.Id,
            membership.Id,
            otherTenantMembership.Id,
            otherTenantRole.Id,
            userEmail,
            password);
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

    private sealed record AuthorizationScenario(
        Guid TenantId,
        Guid UserId,
        Guid MembershipId,
        Guid OtherTenantMembershipId,
        Guid OtherTenantRoleId,
        string UserEmail,
        string Password);

    private static async Task<HttpResponseMessage> PostJsonWithCsrfAsync(HttpClient client, string path, object body, string token, Guid? tenantId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        if (tenantId.HasValue && !client.DefaultRequestHeaders.Contains("X-Tenant-ID"))
        {
            request.Headers.Add("X-Tenant-ID", tenantId.Value.ToString());
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostEmptyWithCsrfAsync(HttpClient client, string path, string token, Guid? tenantId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", token);
        if (tenantId.HasValue && !client.DefaultRequestHeaders.Contains("X-Tenant-ID"))
        {
            request.Headers.Add("X-Tenant-ID", tenantId.Value.ToString());
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PutJsonWithCsrfAsync(HttpClient client, string path, object body, string token, Guid? tenantId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        if (tenantId.HasValue && !client.DefaultRequestHeaders.Contains("X-Tenant-ID"))
        {
            request.Headers.Add("X-Tenant-ID", tenantId.Value.ToString());
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteEmptyWithCsrfAsync(HttpClient client, string path, string token, Guid? tenantId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Add("X-CSRF-TOKEN", token);
        if (tenantId.HasValue && !client.DefaultRequestHeaders.Contains("X-Tenant-ID"))
        {
            request.Headers.Add("X-Tenant-ID", tenantId.Value.ToString());
        }

        return await client.SendAsync(request);
    }
}
