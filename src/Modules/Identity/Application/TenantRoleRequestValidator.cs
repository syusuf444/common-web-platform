using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Application;

public static class TenantRoleRequestValidator
{
    public static Dictionary<string, string[]> Validate(CreateTenantRoleRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            errors[nameof(CreateTenantRoleRequest.Name)] = ["Role name must be between 1 and 100 characters."];
        }

        var permissions = request?.PermissionCodes;
        if (permissions is null)
        {
            errors[nameof(CreateTenantRoleRequest.PermissionCodes)] = ["Permission codes are required."];
        }
        else if (permissions.Any(code => string.IsNullOrWhiteSpace(code) || !PermissionCodes.All.Contains(code.Trim(), StringComparer.Ordinal)))
        {
            errors[nameof(CreateTenantRoleRequest.PermissionCodes)] = ["Every permission code must be a supported permission."];
        }

        return errors;
    }
}
