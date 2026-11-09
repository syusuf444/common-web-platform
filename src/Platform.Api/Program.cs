using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Platform.Api;
using Platform.Api.Auditing;
using Platform.Api.Data;
using Platform.Api.Identity;
using Platform.Api.Settings;
using Platform.Modules.Identity.Application;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Application;

var app = ApiApplication.Build(args);
app.Run();

public partial class Program { }

public static class ApiApplication
{
    public static WebApplication Build(string[] args, string? environmentName = null, string? url = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            EnvironmentName = environmentName,
        });

        if (url is not null)
        {
            builder.WebHost.UseUrls(url);
        }

        builder.Services.AddOpenApi();
        var connectionString = builder.Configuration.GetConnectionString("PlatformDb")
            ?? (builder.Environment.IsDevelopment()
                ? "Server=(localdb)\\MSSQLLocalDB;Database=CommonWebPlatform;Integrated Security=True;TrustServerCertificate=True"
                : throw new InvalidOperationException("The PlatformDb connection string is required."));
        builder.Services.AddDbContext<PlatformDbContext>(options => options.UseSqlServer(connectionString));
        if (builder.Environment.IsDevelopment())
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            builder.Services.AddCors(options => options.AddPolicy("frontend-evaluation", policy => policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()));
        }
        builder.Services.AddIdentityCore<PlatformUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<PlatformDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddIdentityCookies();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "CWP.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy(TenantPermissionPolicies.MembersRead, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantPermissionRequirement(Platform.Modules.Identity.Domain.PermissionCodes.MembersRead)));
            options.AddPolicy(TenantPermissionPolicies.MembersManage, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantPermissionRequirement(Platform.Modules.Identity.Domain.PermissionCodes.MembersManage)));
            options.AddPolicy(TenantPermissionPolicies.RolesManage, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantPermissionRequirement(Platform.Modules.Identity.Domain.PermissionCodes.RolesManage)));
            options.AddPolicy(TenantPermissionPolicies.AuditRead, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantPermissionRequirement(Platform.Modules.Identity.Domain.PermissionCodes.AuditRead)));
            options.AddPolicy(TenantPermissionPolicies.SettingsRead, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantPermissionRequirement(Platform.Modules.Identity.Domain.PermissionCodes.SettingsRead)));
            options.AddPolicy(TenantPermissionPolicies.SettingsManage, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantPermissionRequirement(Platform.Modules.Identity.Domain.PermissionCodes.SettingsManage)));
        });
        builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "CWP.AntiForgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("identity-login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        });
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAuditRecordService, AuditRecordService>();
        builder.Services.AddScoped<ITenantSettingService, TenantSettingService>();
        builder.Services.AddScoped<ITenantMembershipRepository, EfTenantMembershipRepository>();
        builder.Services.AddScoped<ITenantRoleRepository, EfTenantRoleRepository>();
        builder.Services.AddScoped<ITenantMemberManagementService, TenantMemberManagementService>();
        builder.Services.AddScoped<TenantRoleService>();
        builder.Services.AddScoped<IAuthorizationHandler, TenantPermissionAuthorizationHandler>();
        builder.Services.AddScoped<ITenantContextResolver, TenantContextResolver>();
        builder.Services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        builder.Services.AddScoped<TenantMembershipService>();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "Common Web Platform API v1");
            });
            app.UseCors("frontend-evaluation");
        }

        app.Use(async (context, next) =>
        {
            var correlationId = Guid.NewGuid().ToString("N");
            context.Items[CorrelationId.ItemKey] = correlationId;
            context.Response.Headers[CorrelationId.HeaderName] = correlationId;

            try
            {
                await next(context);
            }
            catch (TenantContextRequiredException) when (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.Headers[CorrelationId.HeaderName] = correlationId;

                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "A verified tenant context is required.",
                    Extensions =
                    {
                        ["code"] = "tenant_context_required",
                        ["correlationId"] = correlationId,
                    },
                });
            }
            catch (Exception exception) when (!context.Response.HasStarted)
            {
                context.RequestServices.GetRequiredService<ILogger<Program>>().LogError(
                    exception,
                    "Unhandled API error for {Method} {Path} (correlation ID {CorrelationId})",
                    context.Request.Method,
                    context.Request.Path,
                    correlationId);
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.Headers[CorrelationId.HeaderName] = correlationId;

                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred.",
                    Extensions =
                    {
                        ["code"] = "internal_error",
                        ["correlationId"] = correlationId,
                    },
                });
            }
        });

        app.UseAuthentication();
        app.UseMiddleware<TenantContextMiddleware>();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.UseRateLimiter();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();
        }

        app.MapGet("/health", () => Results.Ok(new HealthResponse("ok")))
            .WithName("GetHealth")
            .WithTags("Health")
            .WithSummary("Check API availability")
            .WithDescription("Returns the API process health without requiring database connectivity.")
            .AllowAnonymous()
            .Produces<HealthResponse>();

        app.MapGet("/health/ready", async (PlatformDbContext database, HttpContext context, CancellationToken cancellationToken) =>
        {
            var canConnect = await database.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? Results.Ok(new HealthResponse("ready"))
                : Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "The database is unavailable.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "database_unavailable",
                        ["correlationId"] = context.Items[CorrelationId.ItemKey],
                    });
        })
            .WithName("GetReadiness")
            .WithTags("Health")
            .WithSummary("Check API and database readiness")
            .WithDescription("Checks whether the configured SQL Server database can be reached.")
            .AllowAnonymous()
            .Produces<HealthResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        var sample = app.MapGroup("/api/v1/sample").WithTags("Sample").AllowAnonymous();

        sample.MapGet("/", () => Results.Ok(new SampleResponse("Common Web Platform API is ready.")))
            .WithName("GetSample")
            .WithSummary("Read the versioned sample resource")
            .Produces<SampleResponse>();

        sample.MapPost("/", (SampleRequest request, HttpContext context) =>
        {
            var errors = SampleRequestValidator.Validate(request);
            var name = request.Name?.Trim();

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(
                    errors,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "The request is invalid.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "validation_failed",
                        ["correlationId"] = context.Items[CorrelationId.ItemKey],
                    });
            }

            return Results.Created("/api/v1/sample/", new SampleResponse($"Hello, {name}."));
        })
            .WithName("CreateSample")
            .WithSummary("Validate and accept a sample request")
            .Produces<SampleResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        app.MapIdentityEndpoints(app.Environment.IsDevelopment());
        app.MapTenantAuthorizationEndpoints();
        app.MapTenantMemberManagementEndpoints();
        app.MapAuditEndpoints();
        app.MapTenantSettingEndpoints();

        return app;
    }
}

internal static class CorrelationId
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";
}

public sealed record HealthResponse(string Status);

public sealed record SampleRequest([property: MaxLength(100)] string? Name);

public sealed record SampleResponse(string Message);

public static class SampleRequestValidator
{
    public static Dictionary<string, string[]> Validate(SampleRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors[nameof(request.Name)] = ["Name is required."];
        }
        else if (name.Length > 100)
        {
            errors[nameof(request.Name)] = ["Name must be 100 characters or fewer."];
        }

        return errors;
    }
}
