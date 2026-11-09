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
}
