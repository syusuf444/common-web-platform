using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.IntegrationTests;

public sealed class ApiContractTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task HealthEndpointReturnsStatusAndCorrelationId()
    {
        using var response = await fixture.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        Assert.Equal("ok", (await response.Content.ReadFromJsonAsync<HealthResponse>())?.Status);
    }

    [Fact]
    public async Task ReadinessEndpointConfirmsSqlServerConnection()
    {
        using var response = await fixture.Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ready", (await response.Content.ReadFromJsonAsync<HealthResponse>())?.Status);
    }

    [Fact]
    public async Task VersionedSampleEndpointIsReachable()
    {
        using var response = await fixture.Client.GetAsync("/api/v1/sample/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ready", (await response.Content.ReadFromJsonAsync<SampleResponse>())?.Message);
    }

    [Fact]
    public async Task TenantEndpointsRequireAuthenticationByDefault()
    {
        using var response = await fixture.Client.GetAsync("/api/v1/tenant/roles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidSampleRequestReturnsCreated()
    {
        using var response = await fixture.Client.PostAsJsonAsync("/api/v1/sample/", new { name = "  Dev Team  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Hello, Dev Team.", (await response.Content.ReadFromJsonAsync<SampleResponse>())?.Message);
    }

    [Fact]
    public async Task InvalidSampleRequestIncludesCorrelationIdInHeaderAndBody()
    {
        using var response = await fixture.Client.PostAsJsonAsync("/api/v1/sample/", new { name = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var headerId = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(headerId, body.RootElement.GetProperty("correlationId").GetString());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("Name", out _));
    }

    [Fact]
    public async Task OpenApiDocumentAndSwaggerUiAreAvailableInDevelopment()
    {
        using var documentResponse = await fixture.Client.GetAsync("/openapi/v1.json");
        using var uiResponse = await fixture.Client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, documentResponse.StatusCode);
        using var document = JsonDocument.Parse(await documentResponse.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToArray();
        Assert.Contains(paths, path => path.StartsWith("/api/v1/sample", StringComparison.Ordinal));
        Assert.Contains(paths, path => path == "/api/v1/auth/login");
        Assert.Contains(paths, path => path == "/api/v1/auth/me");
        Assert.Contains(paths, path => path == "/api/v1/tenant/roles");
        Assert.Contains(paths, path => path == "/api/v1/tenant/memberships");
        Assert.Contains(paths, path => path == "/api/v1/tenant/audit");
        Assert.Contains(paths, path => path == "/api/v1/tenant/settings");
        Assert.Contains(paths, path => path == "/api/v1/tenant/settings/{key}");
        Assert.Contains(paths, path => path == "/api/v1/tenant/notifications");
        Assert.Contains(paths, path => path == "/api/v1/tenant/notifications/{notificationId}");
        Assert.Contains(paths, path => path == "/api/v1/tenant/files");
        Assert.Contains(paths, path => path == "/api/v1/tenant/files/{fileId}");
        Assert.Contains(paths, path => path == "/api/v1/tenant/files/{fileId}/content");
        Assert.Contains(paths, path => path == "/api/v1/tenant/dashboard");
        Assert.Contains(paths, path => path == "/api/v1/tenant/reports/audit");
        Assert.Contains(paths, path => path == "/api/v1/tenant/reports/audit/export.csv");
        Assert.Contains(paths, path => path == "/api/v1/tenant/webhooks/subscriptions");
        Assert.Contains(paths, path => path == "/api/v1/tenant/webhooks/subscriptions/{subscriptionId}");
        Assert.Contains(paths, path => path == "/api/v1/tenant/webhooks/deliveries");
        Assert.Contains(paths, path => path == "/api/v1/tenant/role-access-requests");
        Assert.Contains(paths, path => path == "/api/v1/tenant/role-access-requests/mine");
        Assert.Contains(paths, path => path == "/api/v1/tenant/role-access-requests/pending");
        Assert.Contains(paths, path => path == "/api/v1/tenant/role-access-requests/{requestId}");
        Assert.Contains(paths, path => path == "/api/v1/tenant/role-access-requests/{requestId}/approve");
        Assert.Contains(paths, path => path == "/api/v1/tenant/role-access-requests/{requestId}/reject");
        var upload = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/tenant/files")
            .GetProperty("post");
        Assert.True(upload.GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _));
        Assert.Equal(HttpStatusCode.OK, uiResponse.StatusCode);
    }

    private sealed record HealthResponse(string Status);

    private sealed record SampleResponse(string Message);
}

public sealed class ApiFixture : IAsyncLifetime
{
    private WebApplication? _application;
    private HttpClient? _client;
    private string? _isolatedConnectionString;
    private string? _isolatedNotificationOutboxPath;
    private string? _isolatedFileStoragePath;

    public HttpClient Client => _client ?? throw new InvalidOperationException("The API fixture has not started.");

    public IServiceProvider Services => _application?.Services ?? throw new InvalidOperationException("The API fixture has not started.");

    public string FileStoragePath => _isolatedFileStoragePath ?? throw new InvalidOperationException("The API fixture has not started.");

    public HttpClient CreateClient() => new(new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() })
    {
        BaseAddress = Client.BaseAddress,
    };

    public async Task InitializeAsync()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PlatformDb")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=CommonWebPlatform;Integrated Security=True;TrustServerCertificate=True";
        var testRunId = Guid.NewGuid().ToString("N");
        _isolatedConnectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = $"CWP_Api_{testRunId}",
        }.ConnectionString;
        _isolatedNotificationOutboxPath = Path.Combine(Path.GetTempPath(), $"CWP_Api_{testRunId}", "notifications");
        _isolatedFileStoragePath = Path.Combine(Path.GetTempPath(), $"CWP_Api_{testRunId}", "files");
        _application = ApiApplication.Build(
            [
                $"--ConnectionStrings:PlatformDb={_isolatedConnectionString}",
                $"--Notifications:LocalOutboxPath={_isolatedNotificationOutboxPath}",
                $"--Files:LocalStoragePath={_isolatedFileStoragePath}",
                "--Webhooks:AllowedHosts:0=127.0.0.1",
                "--Webhooks:AllowedHosts:1=localhost",
                "--Webhooks:InitialRetryDelaySeconds=0.01",
            ],
            "Development",
            "http://127.0.0.1:0");
        await using (var scope = _application.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<Platform.Api.Data.PlatformDbContext>();
            await database.Database.MigrateAsync();
        }
        await _application.StartAsync();

        var server = _application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        _client = new HttpClient(new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() })
        {
            BaseAddress = new Uri(address),
        };
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();

        if (_application is not null)
        {
            await _application.StopAsync();
            await _application.DisposeAsync();
        }

        if (_isolatedConnectionString is not null)
        {
            var options = new DbContextOptionsBuilder<Platform.Api.Data.PlatformDbContext>()
                .UseSqlServer(_isolatedConnectionString)
                .Options;
            await using var database = new Platform.Api.Data.PlatformDbContext(options);
            await database.Database.EnsureDeletedAsync();
        }

        if (_isolatedFileStoragePath is not null)
        {
            var testRunDirectory = Path.GetDirectoryName(_isolatedFileStoragePath)!;
            if (Directory.Exists(testRunDirectory))
            {
                Directory.Delete(testRunDirectory, recursive: true);
            }
        }
    }
}
