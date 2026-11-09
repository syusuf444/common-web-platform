using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Data;
using Platform.Modules.Files.Application;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Domain;

namespace Platform.IntegrationTests;

public sealed class TenantFileIntegrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task TenantFilesAreUploadedDownloadedAndIsolated()
    {
        var scenario = await SeedScenarioAsync(fixture.Services);
        using var anonymousClient = fixture.CreateClient();
        using var anonymousList = await anonymousClient.GetAsync("/api/v1/tenant/files");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);

        using var adminClient = await LoginAsync(scenario.AdminEmail, scenario.Password);
        adminClient.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantAId.ToString());
        var csrfToken = await GetCsrfTokenAsync(adminClient);

        using var missingCsrf = await UploadAsync(
            adminClient,
            scenario.TenantAId,
            null,
            "sample.pdf",
            "application/pdf",
            PdfContent());
        Assert.True(
            missingCsrf.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 400, got {(int)missingCsrf.StatusCode}: {await missingCsrf.Content.ReadAsStringAsync()}");

        using var uploaded = await UploadAsync(
            adminClient,
            scenario.TenantAId,
            csrfToken,
            @"..\..\escape.pdf",
            "application/pdf",
            PdfContent());
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        using var uploadedBody = JsonDocument.Parse(await uploaded.Content.ReadAsStringAsync());
        var metadata = uploadedBody.RootElement;
        var fileId = metadata.GetProperty("id").GetGuid();
        Assert.Equal("escape.pdf", metadata.GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", metadata.GetProperty("contentType").GetString());
        Assert.Equal(PdfContent().Length, metadata.GetProperty("sizeBytes").GetInt64());
        Assert.Equal($"/api/v1/tenant/files/{fileId}", uploaded.Headers.Location?.OriginalString);

        using var listed = await adminClient.GetAsync("/api/v1/tenant/files");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using (var body = JsonDocument.Parse(await listed.Content.ReadAsStringAsync()))
        {
            var file = Assert.Single(body.RootElement.EnumerateArray());
            Assert.Equal(fileId, file.GetProperty("id").GetGuid());
        }

        using var metadataResponse = await adminClient.GetAsync($"/api/v1/tenant/files/{fileId}");
        Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);

        using var download = await adminClient.GetAsync($"/api/v1/tenant/files/{fileId}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PdfContent(), await download.Content.ReadAsByteArrayAsync());

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var persisted = await database.TenantFiles.AsNoTracking().SingleAsync(file => file.Id == fileId);
            Assert.Equal(scenario.TenantAId, persisted.TenantId);
            Assert.Equal(scenario.AdminUserId, persisted.UploadedByUserId);
        }

        var storageDirectory = Path.Combine(fixture.FileStoragePath, scenario.TenantAId.ToString("N"));
        var storedFile = Assert.Single(Directory.GetFiles(storageDirectory, "*.bin"));
        var canonicalRoot = Path.GetFullPath(fixture.FileStoragePath) + Path.DirectorySeparatorChar;
        Assert.StartsWith(canonicalRoot, Path.GetFullPath(storedFile), StringComparison.OrdinalIgnoreCase);

        adminClient.DefaultRequestHeaders.Remove("X-Tenant-ID");
        adminClient.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantBId.ToString());
        using var crossTenantMetadata = await adminClient.GetAsync($"/api/v1/tenant/files/{fileId}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantMetadata.StatusCode);
        using var crossTenantDownload = await adminClient.GetAsync($"/api/v1/tenant/files/{fileId}/content");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantDownload.StatusCode);
        using var otherTenantFiles = await adminClient.GetAsync("/api/v1/tenant/files");
        Assert.Equal(HttpStatusCode.OK, otherTenantFiles.StatusCode);
        using var otherTenantBody = JsonDocument.Parse(await otherTenantFiles.Content.ReadAsStringAsync());
        Assert.Empty(otherTenantBody.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task TenantFileUploadsEnforcePermissionsTypesAndSizeLimits()
    {
        var scenario = await SeedScenarioAsync(fixture.Services);

        using var readerClient = await LoginAsync(scenario.ReaderEmail, scenario.Password);
        readerClient.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantAId.ToString());
        using var deniedList = await readerClient.GetAsync("/api/v1/tenant/files");
        Assert.Equal(HttpStatusCode.Forbidden, deniedList.StatusCode);
        var csrfToken = await GetCsrfTokenAsync(readerClient);
        using var deniedUpload = await UploadAsync(
            readerClient,
            scenario.TenantAId,
            csrfToken,
            "sample.pdf",
            "application/pdf",
            PdfContent());
        Assert.Equal(HttpStatusCode.Forbidden, deniedUpload.StatusCode);

        using var adminClient = await LoginAsync(scenario.AdminEmail, scenario.Password);
        adminClient.DefaultRequestHeaders.Add("X-Tenant-ID", scenario.TenantAId.ToString());
        csrfToken = await GetCsrfTokenAsync(adminClient);
        using var unsupportedType = await UploadAsync(
            adminClient,
            scenario.TenantAId,
            csrfToken,
            "script.exe",
            "application/octet-stream",
            [0x4D, 0x5A]);
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedType.StatusCode);

        csrfToken = await GetCsrfTokenAsync(adminClient);
        using var mismatchedSignature = await UploadAsync(
            adminClient,
            scenario.TenantAId,
            csrfToken,
            "fake.pdf",
            "application/pdf",
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Assert.Equal(HttpStatusCode.BadRequest, mismatchedSignature.StatusCode);

        var oversizedContent = new byte[TenantFileRequestValidator.MaxFileSizeBytes + 1];
        PdfContent().CopyTo(oversizedContent, 0);
        csrfToken = await GetCsrfTokenAsync(adminClient);
        using var oversized = await UploadAsync(
            adminClient,
            scenario.TenantAId,
            csrfToken,
            "oversized.pdf",
            "application/pdf",
            oversizedContent);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
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

    private async Task<FileScenario> SeedScenarioAsync(IServiceProvider services)
    {
        const string password = "CWP-File-Test-642!Secure";
        var adminEmail = $"{Guid.NewGuid():N}@example.test";
        var readerEmail = $"{Guid.NewGuid():N}@example.test";
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<PlatformUser>>();
        var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var admin = CreateUser(adminEmail, "File Admin");
        var reader = CreateUser(readerEmail, "File Reader");
        Assert.True((await userManager.CreateAsync(admin, password)).Succeeded);
        Assert.True((await userManager.CreateAsync(reader, password)).Succeeded);

        var tenantA = CreateTenant("file-a");
        var tenantB = CreateTenant("file-b");
        var adminMembershipA = CreateMembership(tenantA.Id, admin.Id);
        var adminMembershipB = CreateMembership(tenantB.Id, admin.Id);
        var readerMembership = CreateMembership(tenantA.Id, reader.Id);
        var roleA = CreateRole(tenantA.Id, "File Admin");
        var roleB = CreateRole(tenantB.Id, "File Admin");

        database.Tenants.AddRange(tenantA, tenantB);
        database.Memberships.AddRange(adminMembershipA, adminMembershipB, readerMembership);
        database.TenantRoles.AddRange(roleA, roleB);
        database.MembershipRoles.AddRange(
            CreateAssignment(tenantA.Id, adminMembershipA.Id, roleA.Id),
            CreateAssignment(tenantB.Id, adminMembershipB.Id, roleB.Id));
        database.TenantRolePermissions.AddRange(
            CreatePermission(tenantA.Id, roleA.Id, PermissionCodes.FilesRead),
            CreatePermission(tenantA.Id, roleA.Id, PermissionCodes.FilesUpload),
            CreatePermission(tenantB.Id, roleB.Id, PermissionCodes.FilesRead),
            CreatePermission(tenantB.Id, roleB.Id, PermissionCodes.FilesUpload));
        await database.SaveChangesAsync();

        return new FileScenario(tenantA.Id, tenantB.Id, admin.Id, adminEmail, readerEmail, password);
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

    private static TenantRolePermission CreatePermission(Guid tenantId, Guid roleId, string permissionCode)
    {
        return new TenantRolePermission
        {
            TenantId = tenantId,
            RoleId = roleId,
            PermissionCode = permissionCode,
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

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        Guid tenantId,
        string? csrfToken,
        string fileName,
        string contentType,
        byte[] content)
    {
        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(fileContent, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenant/files")
        {
            Content = multipart,
        };
        request.Headers.Add("X-Tenant-ID", tenantId.ToString());
        if (csrfToken is not null)
        {
            request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        }

        return await client.SendAsync(request);
    }

    private static byte[] PdfContent()
    {
        return Encoding.ASCII.GetBytes("%PDF-1.7\nsample file content");
    }

    private sealed record FileScenario(
        Guid TenantAId,
        Guid TenantBId,
        Guid AdminUserId,
        string AdminEmail,
        string ReaderEmail,
        string Password);
}
