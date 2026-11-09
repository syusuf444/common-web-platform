using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Api.Data;
using Platform.Api.Notifications;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Notifications.Application;
using Platform.Modules.Tenancy.Domain;

namespace Platform.IntegrationTests;

public sealed class NotificationIntegrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task TenantNotificationsAreValidatedAuthorizedAndIsolated()
    {
        var scenario = await SeedScenarioAsync(fixture.Services);
        using var anonymousClient = fixture.CreateClient();
        using var anonymousList = await anonymousClient.GetAsync("/api/v1/tenant/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);

        using var adminClient = fixture.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(adminClient);
        using var adminLogin = await PostJsonWithCsrfAsync(
            adminClient,
            "/api/v1/auth/login",
            new { email = scenario.AdminEmail, password = scenario.Password },
            csrfToken);
        Assert.Equal(HttpStatusCode.OK, adminLogin.StatusCode);

        using var unprivilegedClient = fixture.CreateClient();
        csrfToken = await GetCsrfTokenAsync(unprivilegedClient);
        using var unprivilegedLogin = await PostJsonWithCsrfAsync(
            unprivilegedClient,
            "/api/v1/auth/login",
            new { email = scenario.ReaderEmail, password = scenario.Password },
            csrfToken);
        Assert.Equal(HttpStatusCode.OK, unprivilegedLogin.StatusCode);
        unprivilegedClient.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantAId.ToString());
        using var forbiddenList = await unprivilegedClient.GetAsync("/api/v1/tenant/notifications");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenList.StatusCode);

        adminClient.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantAId.ToString());
        using var missingCsrf = await adminClient.PostAsJsonAsync(
            "/api/v1/tenant/notifications",
            new { recipient = "client@example.test", subject = "Test", body = "Test message" });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        using (var body = JsonDocument.Parse(await missingCsrf.Content.ReadAsStringAsync()))
        {
            Assert.Equal("antiforgery_validation_failed", body.RootElement.GetProperty("code").GetString());
        }

        csrfToken = await GetCsrfTokenAsync(unprivilegedClient);
        using var deniedWrite = await PostJsonWithCsrfAsync(
            unprivilegedClient,
            "/api/v1/tenant/notifications",
            new { recipient = "client@example.test", subject = "Test", body = "Test message" },
            csrfToken);
        Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);

        csrfToken = await GetCsrfTokenAsync(adminClient);
        using var invalidRequest = await PostJsonWithCsrfAsync(
            adminClient,
            "/api/v1/tenant/notifications",
            new { recipient = "not-an-email", subject = " ", body = "Test message" },
            csrfToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRequest.StatusCode);

        csrfToken = await GetCsrfTokenAsync(adminClient);
        using var sent = await PostJsonWithCsrfAsync(
            adminClient,
            "/api/v1/tenant/notifications",
            new
            {
                recipient = "client@example.test",
                subject = "Demo update",
                body = "Your local demo notification is ready.",
            },
            csrfToken);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        var location = Assert.IsType<Uri>(sent.Headers.Location);
        using var receiptBody = JsonDocument.Parse(await sent.Content.ReadAsStringAsync());
        var notificationId = receiptBody.RootElement.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/tenant/notifications/{notificationId}", location.OriginalString);
        Assert.Equal("WrittenToLocalOutbox", receiptBody.RootElement.GetProperty("status").GetString());

        using var listed = await adminClient.GetAsync("/api/v1/tenant/notifications?take=10");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using (var body = JsonDocument.Parse(await listed.Content.ReadAsStringAsync()))
        {
            var item = Assert.Single(body.RootElement.EnumerateArray());
            Assert.Equal(notificationId, item.GetProperty("id").GetGuid());
            Assert.Equal(scenario.TenantAId, item.GetProperty("tenantId").GetGuid());
            Assert.Equal("client@example.test", item.GetProperty("recipient").GetString());
        }

        using var read = await adminClient.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        adminClient.DefaultRequestHeaders.Remove("X-Tenant-ID");
        adminClient.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantBId.ToString());
        using var crossTenant = await adminClient.GetAsync(location);
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
        using var otherTenantList = await adminClient.GetAsync("/api/v1/tenant/notifications");
        Assert.Equal(HttpStatusCode.OK, otherTenantList.StatusCode);
        using (var body = JsonDocument.Parse(await otherTenantList.Content.ReadAsStringAsync()))
        {
            Assert.Empty(body.RootElement.EnumerateArray());
        }
    }

    [Fact]
    public async Task LocalNotificationOutboxFailureIsLoggedAndPropagated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"CWP_NotificationFailure_{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(path, "This file intentionally blocks directory creation.");
        var logger = new RecordingLogger<LocalNotificationOutbox>();
        var outbox = new LocalNotificationOutbox(
            Options.Create(new LocalNotificationOutboxOptions { DirectoryPath = path }),
            TimeProvider.System,
            logger);

        try
        {
            await Assert.ThrowsAnyAsync<IOException>(() => outbox.DeliverAsync(
                new NotificationMessage(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "client@example.test",
                    "Failure test",
                    "This message should not be written.",
                    DateTimeOffset.UtcNow),
                CancellationToken.None));

            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NotificationRequestValidatorRejectsHeaderInjectionAndOversizedMessages()
    {
        var errors = NotificationRequestValidator.Validate(new SendNotificationRequest
        {
            Recipient = "client@example.test",
            Subject = "Subject\r\nBcc: other@example.test",
            Body = new string('x', 10001),
        });

        Assert.Contains(nameof(SendNotificationRequest.Subject), errors.Keys);
        Assert.Contains(nameof(SendNotificationRequest.Body), errors.Keys);
    }

    private static async Task<NotificationScenario> SeedScenarioAsync(IServiceProvider services)
    {
        const string password = "CWP-Notification-Test-642!";
        var adminEmail = $"{Guid.NewGuid():N}@example.test";
        var readerEmail = $"{Guid.NewGuid():N}@example.test";
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<PlatformUser>>();
        var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var admin = CreateUser(adminEmail, "Notification Admin");
        var reader = CreateUser(readerEmail, "Notification Reader");
        Assert.True((await userManager.CreateAsync(admin, password)).Succeeded);
        Assert.True((await userManager.CreateAsync(reader, password)).Succeeded);

        var tenantA = CreateTenant("notification-a");
        var tenantB = CreateTenant("notification-b");
        var adminMembershipA = CreateMembership(tenantA.Id, admin.Id);
        var adminMembershipB = CreateMembership(tenantB.Id, admin.Id);
        var readerMembership = CreateMembership(tenantA.Id, reader.Id);
        var roleA = CreateRole(tenantA.Id, "Notification Admin");
        var roleB = CreateRole(tenantB.Id, "Notification Admin");

        database.Tenants.AddRange(tenantA, tenantB);
        database.Memberships.AddRange(adminMembershipA, adminMembershipB, readerMembership);
        database.TenantRoles.AddRange(roleA, roleB);
        database.MembershipRoles.AddRange(
            CreateAssignment(tenantA.Id, adminMembershipA.Id, roleA.Id),
            CreateAssignment(tenantB.Id, adminMembershipB.Id, roleB.Id));
        database.TenantRolePermissions.AddRange(
            CreatePermission(tenantA.Id, roleA.Id, PermissionCodes.NotificationsRead),
            CreatePermission(tenantA.Id, roleA.Id, PermissionCodes.NotificationsSend),
            CreatePermission(tenantB.Id, roleB.Id, PermissionCodes.NotificationsRead),
            CreatePermission(tenantB.Id, roleB.Id, PermissionCodes.NotificationsSend));
        await database.SaveChangesAsync();

        return new NotificationScenario(tenantA.Id, tenantB.Id, adminEmail, readerEmail, password);
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

    private static TenantRolePermission CreatePermission(Guid tenantId, Guid roleId, string permissionCode)
    {
        return new TenantRolePermission
        {
            TenantId = tenantId,
            RoleId = roleId,
            PermissionCode = permissionCode,
        };
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
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

    private sealed record NotificationScenario(
        Guid TenantAId,
        Guid TenantBId,
        string AdminEmail,
        string ReaderEmail,
        string Password);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
