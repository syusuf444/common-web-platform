using Microsoft.AspNetCore.Mvc;
using Platform.Api.Identity;
using Platform.Modules.Files.Application;
using Platform.Modules.Tenancy.Application;

namespace Platform.Api.Files;

public static class TenantFileEndpointMappings
{
    public static IEndpointRouteBuilder MapTenantFileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var files = endpoints.MapGroup("/api/v1/tenant/files")
            .WithTags("Tenant Files");

        files.MapGet("", async (
                ITenantContextAccessor tenantContext,
                ITenantFileService service,
                CancellationToken cancellationToken) =>
            {
                var tenantId = tenantContext.RequireCurrent().TenantId;
                return Results.Ok(await service.ListAsync(tenantId, cancellationToken));
            })
            .WithName("ListTenantFiles")
            .WithSummary("List file metadata in the active tenant")
            .RequireAuthorization(TenantPermissionPolicies.FilesRead)
            .Produces<IReadOnlyList<TenantFileResponse>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        files.MapGet("/{fileId:guid}", async (
                Guid fileId,
                ITenantContextAccessor tenantContext,
                ITenantFileService service,
                CancellationToken cancellationToken) =>
            {
                var tenantId = tenantContext.RequireCurrent().TenantId;
                var file = await service.GetAsync(tenantId, fileId, cancellationToken);
                return file is null ? Results.NotFound() : Results.Ok(file);
            })
            .WithName("GetTenantFile")
            .WithSummary("Read tenant-scoped file metadata")
            .RequireAuthorization(TenantPermissionPolicies.FilesRead)
            .Produces<TenantFileResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        files.MapGet("/{fileId:guid}/content", async (
                Guid fileId,
                ITenantContextAccessor tenantContext,
                ITenantFileService service,
                CancellationToken cancellationToken) =>
            {
                var tenantId = tenantContext.RequireCurrent().TenantId;
                var download = await service.OpenDownloadAsync(tenantId, fileId, cancellationToken);
                if (download is null)
                {
                    return Results.NotFound();
                }

                return Results.File(
                    download.Content,
                    download.Metadata.ContentType,
                    download.Metadata.FileName);
            })
            .WithName("DownloadTenantFile")
            .WithSummary("Download a file belonging to the active tenant")
            .RequireAuthorization(TenantPermissionPolicies.FilesRead)
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        files.MapPost("", async (
                HttpRequest request,
                HttpContext context,
                ITenantContextAccessor tenantContext,
                ITenantFileService service,
                CancellationToken cancellationToken) =>
            {
                if (!request.HasFormContentType)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["file"] = ["A multipart form upload is required."] },
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The file upload is invalid.",
                        extensions: ErrorExtensions(context));
                }

                IFormCollection form;
                try
                {
                    form = await request.ReadFormAsync(cancellationToken);
                }
                catch (InvalidDataException)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["file"] = ["The multipart form data is malformed."] },
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The file upload is invalid.",
                        extensions: ErrorExtensions(context));
                }

                var file = form.Files.GetFile("file");
                if (file is null)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["file"] = ["A file is required."] },
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The file upload is invalid.",
                        extensions: ErrorExtensions(context));
                }

                var tenant = tenantContext.RequireCurrent();
                await using var content = file.OpenReadStream();
                try
                {
                    var created = await service.UploadAsync(
                        tenant.TenantId,
                        tenant.UserId,
                        file.FileName,
                        file.ContentType,
                        file.Length,
                        content,
                        cancellationToken);
                    return Results.Created($"/api/v1/tenant/files/{created.Id}", created);
                }
                catch (TenantFileRequestValidationException exception)
                {
                    return Results.ValidationProblem(
                        exception.Errors,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The file upload is invalid.",
                        extensions: ErrorExtensions(context));
                }
            })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithMetadata(new RequestSizeLimitAttribute(TenantFileRequestValidator.MaxFileSizeBytes + 64 * 1024))
            .WithName("UploadTenantFile")
            .WithSummary("Upload a supported file to the active tenant's local storage")
            .WithDescription("Accepts PDF, PNG, and JPEG files up to 10 MiB. File content is signature-checked; stored names are generated server-side.")
            .RequireAuthorization(TenantPermissionPolicies.FilesUpload)
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<TenantFileResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status413PayloadTooLarge);

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
