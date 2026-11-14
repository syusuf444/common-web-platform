using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Data;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Domain;
using Platform.Modules.Webhooks.Application;
using Platform.Modules.Webhooks.Domain;

namespace Platform.IntegrationTests;

public sealed class WebhookIntegrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task SubscriptionsArePermissionControlledSignedRetriedAndAuditable()
    {
        await using var receiver = await TestWebhookReceiver.StartAsync();
        var scenario = await SeedScenarioAsync(fixture.Services);
        using var anonymous = fixture.CreateClient();
        using var anonymousResponse = await anonymous.GetAsync("/api/v1/tenant/webhooks/subscriptions");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var reader = await LoginAsync(scenario.ReaderEmail, scenario.Password);
        reader.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantAId.ToString());
        using var readerSubscriptions = await reader.GetAsync("/api/v1/tenant/webhooks/subscriptions");
        Assert.Equal(HttpStatusCode.OK, readerSubscriptions.StatusCode);
        using var readerCreate = await PostJsonWithCsrfAsync(
            reader,
            "/api/v1/tenant/webhooks/subscriptions",
            new CreateWebhookSubscriptionRequest
            {
                EventType = "tenant.member.added",
                EndpointUrl = receiver.EndpointUrl,
            },
            await GetCsrfTokenAsync(reader));
        Assert.Equal(HttpStatusCode.Forbidden, readerCreate.StatusCode);

        using var admin = await LoginAsync(scenario.AdminEmail, scenario.Password);
        admin.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantAId.ToString());
        var csrfToken = await GetCsrfTokenAsync(admin);
        using var disallowedHost = await PostJsonWithCsrfAsync(
            admin,
            "/api/v1/tenant/webhooks/subscriptions",
            new CreateWebhookSubscriptionRequest
            {
                EventType = "tenant.member.added",
                EndpointUrl = "https://example.com/webhook",
            },
            csrfToken);
        Assert.Equal(HttpStatusCode.BadRequest, disallowedHost.StatusCode);

        csrfToken = await GetCsrfTokenAsync(admin);
        using var created = await PostJsonWithCsrfAsync(
            admin,
            "/api/v1/tenant/webhooks/subscriptions",
            new CreateWebhookSubscriptionRequest
            {
                EventType = "tenant.member.added",
                EndpointUrl = receiver.EndpointUrl,
            },
            csrfToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var subscriptionId = createdBody.RootElement.GetProperty("id").GetGuid();
        var signingSecret = createdBody.RootElement.GetProperty("signingSecret").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(signingSecret));
        Assert.Equal($"/api/v1/tenant/webhooks/subscriptions/{subscriptionId}", created.Headers.Location?.OriginalString);

        using var listed = await admin.GetAsync("/api/v1/tenant/webhooks/subscriptions");
        using (var body = JsonDocument.Parse(await listed.Content.ReadAsStringAsync()))
        {
            var subscription = Assert.Single(body.RootElement.EnumerateArray());
            Assert.Equal(subscriptionId, subscription.GetProperty("id").GetGuid());
            Assert.False(subscription.TryGetProperty("signingSecret", out _));
            Assert.False(subscription.TryGetProperty("protectedSigningSecret", out _));
        }

        using var createdAuditScope = fixture.Services.CreateScope();
        var database = createdAuditScope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var audit = await database.AuditRecords.SingleAsync(record =>
            record.TenantId == scenario.TenantAId
            && record.Action == "tenant.webhook.subscription.created");
        Assert.DoesNotContain(signingSecret, audit.MetadataJson ?? string.Empty, StringComparison.Ordinal);
        var storedSecret = await database.WebhookSubscriptions
            .Where(item => item.Id == subscriptionId)
            .Select(item => item.ProtectedSigningSecret)
            .SingleAsync();
        Assert.NotEqual(signingSecret, storedSecret);
        Assert.DoesNotContain(signingSecret, storedSecret, StringComparison.Ordinal);

        const string payload = """{"memberId":"e3e34755-50a0-4519-8a11-84619b7c54a3","email":"member@example.test"}""";
        await using (var publishScope = fixture.Services.CreateAsyncScope())
        {
            var publisher = publishScope.ServiceProvider.GetRequiredService<IWebhookPublisher>();
            var queuedCount = await publisher.PublishAsync(
                new WebhookEvent(scenario.TenantAId, "tenant.member.added", payload, DateTimeOffset.UtcNow),
                CancellationToken.None);
            Assert.Equal(1, queuedCount);
        }

        var delivery = await WaitForDeliveryAsync(admin);
        Assert.Equal("Succeeded", delivery.GetProperty("status").GetString());
        Assert.Equal(2, delivery.GetProperty("attemptCount").GetInt32());
        var attempts = delivery.GetProperty("attempts").EnumerateArray().ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.Equal(503, attempts[0].GetProperty("httpStatusCode").GetInt32());
        Assert.False(attempts[0].GetProperty("succeeded").GetBoolean());
        Assert.Equal(200, attempts[1].GetProperty("httpStatusCode").GetInt32());
        Assert.True(attempts[1].GetProperty("succeeded").GetBoolean());

        var received = receiver.Requests.ToArray();
        Assert.Equal(2, received.Length);
        foreach (var request in received)
        {
            Assert.Equal(payload, request.Body);
            Assert.Equal("tenant.member.added", request.EventType);
            Assert.Equal(delivery.GetProperty("id").GetGuid().ToString("D"), request.DeliveryId);
            AssertSignature(signingSecret, request);
        }

        admin.DefaultRequestHeaders.Remove("X-Tenant-ID");
        admin.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantBId.ToString());
        using var otherTenantSubscriptions = await admin.GetAsync("/api/v1/tenant/webhooks/subscriptions");
        using (var body = JsonDocument.Parse(await otherTenantSubscriptions.Content.ReadAsStringAsync()))
        {
            Assert.Empty(body.RootElement.EnumerateArray());
        }

        using var crossTenantDeliveryList = await admin.GetAsync("/api/v1/tenant/webhooks/deliveries");
        using (var body = JsonDocument.Parse(await crossTenantDeliveryList.Content.ReadAsStringAsync()))
        {
            Assert.Empty(body.RootElement.GetProperty("items").EnumerateArray());
        }

        admin.DefaultRequestHeaders.Remove("X-Tenant-ID");
        admin.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantAId.ToString());
        csrfToken = await GetCsrfTokenAsync(admin);
        using var disabled = await DeleteWithCsrfAsync(
            admin,
            $"/api/v1/tenant/webhooks/subscriptions/{subscriptionId}",
            csrfToken);
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        using var details = await admin.GetAsync($"/api/v1/tenant/webhooks/subscriptions/{subscriptionId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var detailBody = JsonDocument.Parse(await details.Content.ReadAsStringAsync());
        Assert.False(detailBody.RootElement.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task WebhookPublisherDoesNotCrossTenantBoundariesAndRejectsInvalidEvents()
    {
        var scenario = await SeedScenarioAsync(fixture.Services);
        using (var scope = fixture.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var subscription = new Platform.Modules.Webhooks.Domain.WebhookSubscription
            {
                Id = Guid.NewGuid(),
                TenantId = scenario.TenantAId,
                EventType = "tenant.member.added",
                EndpointUrl = "http://127.0.0.1:1/webhook",
                ProtectedSigningSecret = "protected-value",
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            database.WebhookSubscriptions.Add(subscription);
            await database.SaveChangesAsync();
        }

        await using var publishScope = fixture.Services.CreateAsyncScope();
        var publisher = publishScope.ServiceProvider.GetRequiredService<IWebhookPublisher>();
        var otherTenantCount = await publisher.PublishAsync(
            new WebhookEvent(scenario.TenantBId, "tenant.member.added", """{"tenant":"b"}""", DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.Equal(0, otherTenantCount);
        await Assert.ThrowsAsync<ArgumentException>(() => publisher.PublishAsync(
            new WebhookEvent(scenario.TenantAId, "Bad Event", "{}", DateTimeOffset.UtcNow),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => publisher.PublishAsync(
            new WebhookEvent(scenario.TenantAId, "tenant.member.added", "not-json", DateTimeOffset.UtcNow),
            CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredFinalDeliveryLeaseIsMarkedFailed()
    {
        var scenario = await SeedScenarioAsync(fixture.Services);
        var subscriptionId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            database.WebhookSubscriptions.Add(new WebhookSubscription
            {
                Id = subscriptionId,
                TenantId = scenario.TenantAId,
                EventType = "tenant.member.added",
                EndpointUrl = "http://127.0.0.1:1/webhook",
                ProtectedSigningSecret = "protected-value",
                IsActive = true,
                CreatedAtUtc = now.AddMinutes(-10),
            });
            database.WebhookDeliveries.Add(new WebhookDelivery
            {
                Id = deliveryId,
                TenantId = scenario.TenantAId,
                SubscriptionId = subscriptionId,
                EventType = "tenant.member.added",
                PayloadJson = "{}",
                Status = WebhookDeliveryStatuses.Processing,
                AttemptCount = WebhookDeliveryStatuses.MaximumAttempts,
                CreatedAtUtc = now.AddMinutes(-10),
                NextAttemptAtUtc = now,
                LeaseExpiresAtUtc = now.AddSeconds(-1),
            });
            await database.SaveChangesAsync();
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWebhookStore>();
            await store.ClaimNextDeliveryAsync(now, now.AddMinutes(2), CancellationToken.None);
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var delivery = await database.WebhookDeliveries
                .AsNoTracking()
                .SingleAsync(item => item.TenantId == scenario.TenantAId && item.Id == deliveryId);
            Assert.Equal(WebhookDeliveryStatuses.Failed, delivery.Status);
            Assert.NotNull(delivery.CompletedAtUtc);
            Assert.Contains("lease expired", delivery.LastError, StringComparison.OrdinalIgnoreCase);
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

    private async Task<WebhookScenario> SeedScenarioAsync(IServiceProvider services)
    {
        const string password = "CWP-Webhook-Test-642!Secure";
        var adminEmail = $"{Guid.NewGuid():N}@example.test";
        var readerEmail = $"{Guid.NewGuid():N}@example.test";
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<PlatformUser>>();
        var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var admin = CreateUser(adminEmail, "Webhook Admin");
        var reader = CreateUser(readerEmail, "Webhook Reader");
        Assert.True((await userManager.CreateAsync(admin, password)).Succeeded);
        Assert.True((await userManager.CreateAsync(reader, password)).Succeeded);

        var tenantA = CreateTenant("webhook-a");
        var tenantB = CreateTenant("webhook-b");
        var adminMembershipA = CreateMembership(tenantA.Id, admin.Id);
        var adminMembershipB = CreateMembership(tenantB.Id, admin.Id);
        var readerMembership = CreateMembership(tenantA.Id, reader.Id);
        var adminRoleA = CreateRole(tenantA.Id, "Webhook Admin");
        var adminRoleB = CreateRole(tenantB.Id, "Webhook Admin");
        var readerRole = CreateRole(tenantA.Id, "Webhook Reader");

        database.Tenants.AddRange(tenantA, tenantB);
        database.Memberships.AddRange(adminMembershipA, adminMembershipB, readerMembership);
        database.TenantRoles.AddRange(adminRoleA, adminRoleB, readerRole);
        database.MembershipRoles.AddRange(
            CreateAssignment(tenantA.Id, adminMembershipA.Id, adminRoleA.Id),
            CreateAssignment(tenantB.Id, adminMembershipB.Id, adminRoleB.Id),
            CreateAssignment(tenantA.Id, readerMembership.Id, readerRole.Id));
        database.TenantRolePermissions.AddRange(
            CreatePermission(tenantA.Id, adminRoleA.Id, PermissionCodes.WebhooksRead),
            CreatePermission(tenantA.Id, adminRoleA.Id, PermissionCodes.WebhooksManage),
            CreatePermission(tenantB.Id, adminRoleB.Id, PermissionCodes.WebhooksRead),
            CreatePermission(tenantB.Id, adminRoleB.Id, PermissionCodes.WebhooksManage),
            CreatePermission(tenantA.Id, readerRole.Id, PermissionCodes.WebhooksRead));
        await database.SaveChangesAsync();
        return new WebhookScenario(tenantA.Id, tenantB.Id, adminEmail, readerEmail, password);
    }

    private async Task<JsonElement> WaitForDeliveryAsync(HttpClient client)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await client.GetAsync("/api/v1/tenant/webhooks/deliveries?skip=0&take=10");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (body.RootElement.GetProperty("items").GetArrayLength() > 0)
            {
                var delivery = body.RootElement.GetProperty("items")[0];
                var status = delivery.GetProperty("status").GetString();
                if (status is "Succeeded" or "Failed")
                {
                    return delivery.Clone();
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException("Webhook delivery did not reach a terminal state within the test timeout.");
    }

    private static void AssertSignature(string secret, ReceivedWebhookRequest request)
    {
        var base64 = secret.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        var key = Convert.FromBase64String(base64);
        var signedPayload = Encoding.UTF8.GetBytes($"{request.Timestamp}.{request.Body}");
        var expected = $"v1={Convert.ToHexString(HMACSHA256.HashData(key, signedPayload)).ToLowerInvariant()}";
        Assert.Equal(expected, request.Signature);
        Assert.True(long.TryParse(request.Timestamp, out _));
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

    private static async Task<HttpResponseMessage> DeleteWithCsrfAsync(HttpClient client, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private sealed record WebhookScenario(
        Guid TenantAId,
        Guid TenantBId,
        string AdminEmail,
        string ReaderEmail,
        string Password);

    private sealed record ReceivedWebhookRequest(
        string Body,
        string DeliveryId,
        string EventType,
        string Timestamp,
        string Signature);

    private sealed class TestWebhookReceiver : IAsyncDisposable
    {
        private readonly WebApplication _application;
        private int _requestCount;

        private TestWebhookReceiver(WebApplication application)
        {
            _application = application;
        }

        public string EndpointUrl { get; private set; } = string.Empty;

        public ConcurrentQueue<ReceivedWebhookRequest> Requests { get; } = new();

        public static async Task<TestWebhookReceiver> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var receiver = new TestWebhookReceiver(app);
            app.MapPost("/receive", async (HttpRequest request) =>
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                receiver.Requests.Enqueue(new ReceivedWebhookRequest(
                    body,
                    request.Headers["X-CWP-Webhook-Id"].ToString(),
                    request.Headers["X-CWP-Webhook-Event"].ToString(),
                    request.Headers["X-CWP-Webhook-Timestamp"].ToString(),
                    request.Headers["X-CWP-Webhook-Signature"].ToString()));
                var statusCode = Interlocked.Increment(ref receiver._requestCount) == 1
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status200OK;
                return Results.StatusCode(statusCode);
            });
            await app.StartAsync();
            var server = app.Services.GetRequiredService<IServer>();
            var address = new Uri(Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses));
            receiver.EndpointUrl = new Uri(address, "/receive").ToString();
            return receiver;
        }

        public async ValueTask DisposeAsync()
        {
            await _application.StopAsync();
            await _application.DisposeAsync();
        }
    }
}
