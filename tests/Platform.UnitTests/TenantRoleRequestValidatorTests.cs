using Platform.Modules.Identity.Application;

namespace Platform.UnitTests;

public sealed class TenantRoleRequestValidatorTests
{
    [Fact]
    public void AcceptsKnownPermissionCodesAndRoleName()
    {
        var errors = TenantRoleRequestValidator.Validate(
            new CreateTenantRoleRequest("  Tenant Admin  ", ["roles.manage", "members.read"]));

        Assert.Empty(errors);
    }

    [Fact]
    public void RejectsUnknownPermissionCodes()
    {
        var errors = TenantRoleRequestValidator.Validate(
            new CreateTenantRoleRequest("Auditor", ["members.read", "platform.superuser"]));

        Assert.Contains(nameof(CreateTenantRoleRequest.PermissionCodes), errors.Keys);
    }

    [Fact]
    public void RejectsMissingRoleNameAndPermissionList()
    {
        var errors = TenantRoleRequestValidator.Validate(new CreateTenantRoleRequest(" ", null!));

        Assert.Contains(nameof(CreateTenantRoleRequest.Name), errors.Keys);
        Assert.Contains(nameof(CreateTenantRoleRequest.PermissionCodes), errors.Keys);
    }
}
