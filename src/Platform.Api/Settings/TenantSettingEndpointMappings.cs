using Microsoft.AspNetCore.Mvc;
using Platform.Api.Auditing;
using Platform.Api.Identity;

namespace Platform.Api.Settings;

public static class TenantSettingEndpointMappings
{
    public static IEndpointRouteBuilder MapTenantSettingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var settings = endpoints.MapGroup("/api/v1/tenant/settings")
            .WithTags("Tenant Settings");

        settings.MapGet("", async (ITenantSettingService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.ListAsync(cancellationToken)))
            .WithName("ListTenantSettings")
            .WithSummary("List non-secret settings for the active tenant")
            .WithDescription("Returns settings for the verified tenant only. Secret values are not supported.")
            .RequireAuthorization(TenantPermissionPolicies.SettingsRead)
            .Produces<IReadOnlyList<TenantSettingResponse>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        settings.MapGet("/{key}", async (
                string key,
                HttpContext context,
                ITenantSettingService service,
                CancellationToken cancellationToken) =>
            {
                var errors = TenantSettingValidator.ValidateKey(key);
                if (errors.Count != 0)
                {
                    return Results.ValidationProblem(
                        errors,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The setting key is invalid.",
                        extensions: ErrorExtensions(context));
                }

                var setting = await service.GetAsync(key, cancellationToken);
                return setting is null ? Results.NotFound() : Results.Ok(setting);
            })
            .WithName("GetTenantSetting")
            .WithSummary("Get a non-secret setting from the active tenant")
            .WithDescription("Looks up a setting by its normalized key within the verified tenant.")
            .RequireAuthorization(TenantPermissionPolicies.SettingsRead)
            .Produces<TenantSettingResponse>()
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        settings.MapPost("", async (
                CreateTenantSettingRequest request,
                HttpContext context,
                ITenantSettingService service,
                IAuditRecordService auditRecords,
                CancellationToken cancellationToken) =>
            {
                var errors = TenantSettingValidator.ValidateCreate(request);
                if (errors.Count != 0)
                {
                    return Results.ValidationProblem(
                        errors,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The setting request is invalid.",
                        extensions: ErrorExtensions(context));
                }

                var (outcome, setting) = await service.CreateAsync(request.Key, request.Value, cancellationToken);
                if (outcome == TenantSettingWriteOutcome.AlreadyExists)
                {
                    return Results.Conflict(new ProblemDetails
                    {
                        Status = StatusCodes.Status409Conflict,
                        Title = "A setting with this key already exists in the active tenant.",
                        Extensions = { ["code"] = "setting_exists", ["correlationId"] = context.Items[CorrelationId.ItemKey] },
                    });
                }

                var savedSetting = setting!;
                await auditRecords.RecordAsync(
                    new AuditEvent(
                        "tenant.setting.created",
                        "tenant_setting",
                        savedSetting.Key,
                        Metadata: new Dictionary<string, string> { ["key"] = savedSetting.Key }),
                    cancellationToken);
                return Results.Created($"/api/v1/tenant/settings/{Uri.EscapeDataString(savedSetting.Key)}", savedSetting);
            })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("CreateTenantSetting")
            .WithSummary("Create a non-secret setting for the active tenant")
            .WithDescription("Values are plain text. Keys suggesting passwords, tokens, credentials, or cryptographic/connection secrets are rejected.")
            .RequireAuthorization(TenantPermissionPolicies.SettingsManage)
            .Produces<TenantSettingResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        settings.MapPut("/{key}", async (
                string key,
                UpdateTenantSettingRequest request,
                HttpContext context,
                ITenantSettingService service,
                IAuditRecordService auditRecords,
                CancellationToken cancellationToken) =>
            {
                var keyErrors = TenantSettingValidator.ValidateKey(key);
                var valueErrors = TenantSettingValidator.ValidateValue(request.Value);
                foreach (var error in valueErrors)
                {
                    keyErrors[error.Key] = error.Value;
                }

                if (keyErrors.Count != 0)
                {
                    return Results.ValidationProblem(
                        keyErrors,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The setting request is invalid.",
                        extensions: ErrorExtensions(context));
                }

                var setting = await service.UpdateAsync(key, request.Value, cancellationToken);
                if (setting is null)
                {
                    return Results.NotFound();
                }

                await auditRecords.RecordAsync(
                    new AuditEvent(
                        "tenant.setting.updated",
                        "tenant_setting",
                        setting.Key,
                        Metadata: new Dictionary<string, string> { ["key"] = setting.Key }),
                    cancellationToken);
                return Results.Ok(setting);
            })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("UpdateTenantSetting")
            .WithSummary("Update a non-secret setting in the active tenant")
            .WithDescription("Replaces the setting value. Values are plain text and limited to 2000 characters.")
            .RequireAuthorization(TenantPermissionPolicies.SettingsManage)
            .Produces<TenantSettingResponse>()
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        settings.MapDelete("/{key}", async (
                string key,
                HttpContext context,
                ITenantSettingService service,
                IAuditRecordService auditRecords,
                CancellationToken cancellationToken) =>
            {
                var errors = TenantSettingValidator.ValidateKey(key);
                if (errors.Count != 0)
                {
                    return Results.ValidationProblem(
                        errors,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The setting key is invalid.",
                        extensions: ErrorExtensions(context));
                }

                var canonicalKey = TenantSettingValidator.CanonicalizeKey(key);
                if (!await service.DeleteAsync(key, cancellationToken))
                {
                    return Results.NotFound();
                }

                await auditRecords.RecordAsync(
                    new AuditEvent(
                        "tenant.setting.deleted",
                        "tenant_setting",
                        canonicalKey,
                        Metadata: new Dictionary<string, string> { ["key"] = canonicalKey }),
                    cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("DeleteTenantSetting")
            .WithSummary("Delete a setting from the active tenant")
            .WithDescription("Deletes the setting with this key from the verified tenant.")
            .RequireAuthorization(TenantPermissionPolicies.SettingsManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static Dictionary<string, object?> ErrorExtensions(HttpContext context)
    {
        return new Dictionary<string, object?>
        {
            ["code"] = "validation_failed",
            ["correlationId"] = context.Items[CorrelationId.ItemKey],
        };
    }
}
