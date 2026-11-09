using Microsoft.EntityFrameworkCore;
using Platform.Modules.Identity.Application;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Api.Data;

public sealed class EfTenantRoleRepository(PlatformDbContext database) : ITenantRoleRepository
{
    public async Task<IReadOnlyList<TenantRoleSummary>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var roles = await database.TenantRoles
            .AsNoTracking()
            .Where(role => role.TenantId == tenantId)
            .OrderBy(role => role.Name)
            .Select(role => new
            {
                role.Id,
                role.Name,
                role.IsSystem,
                Permissions = database.TenantRolePermissions
                    .Where(permission => permission.TenantId == tenantId && permission.RoleId == role.Id)
                    .Select(permission => permission.PermissionCode)
                    .OrderBy(code => code)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return roles.Select(role => new TenantRoleSummary(role.Id, role.Name, role.IsSystem, role.Permissions.ToArray())).ToArray();
    }

    public async Task<TenantRoleSummary> CreateAsync(Guid tenantId, string name, IReadOnlyList<string> permissionCodes, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default)
    {
        var role = new TenantRole
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            IsSystem = false,
            CreatedAtUtc = createdAtUtc,
        };
        database.TenantRoles.Add(role);
        foreach (var code in permissionCodes)
        {
            database.TenantRolePermissions.Add(new TenantRolePermission
            {
                TenantId = tenantId,
                RoleId = role.Id,
                PermissionCode = code,
            });
        }

        await database.SaveChangesAsync(cancellationToken);
        return new TenantRoleSummary(role.Id, role.Name, role.IsSystem, permissionCodes);
    }

    public async Task<bool> AssignAsync(Guid tenantId, Guid membershipId, Guid roleId, DateTimeOffset assignedAtUtc, CancellationToken cancellationToken = default)
    {
        var membershipExists = await database.Memberships.AsNoTracking().AnyAsync(
            membership => membership.TenantId == tenantId
                && membership.Id == membershipId
                && membership.Status == MembershipStatus.Active.ToString(),
            cancellationToken);
        var roleExists = await database.TenantRoles.AsNoTracking().AnyAsync(
            role => role.TenantId == tenantId && role.Id == roleId,
            cancellationToken);
        if (!membershipExists || !roleExists)
        {
            return false;
        }

        if (await database.MembershipRoles.AnyAsync(
                item => item.TenantId == tenantId && item.MembershipId == membershipId && item.RoleId == roleId,
                cancellationToken))
        {
            return true;
        }

        var assignment = new MembershipRole
        {
            TenantId = tenantId,
            MembershipId = membershipId,
            RoleId = roleId,
            AssignedAtUtc = assignedAtUtc,
        };
        database.MembershipRoles.Add(assignment);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveAsync(Guid tenantId, Guid membershipId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var assignment = await database.MembershipRoles.SingleOrDefaultAsync(
            item => item.TenantId == tenantId && item.MembershipId == membershipId && item.RoleId == roleId,
            cancellationToken);
        if (assignment is null)
        {
            return false;
        }

        database.MembershipRoles.Remove(assignment);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<bool> HasPermissionAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken cancellationToken = default)
    {
        return (from membership in database.Memberships.AsNoTracking()
                join assignment in database.MembershipRoles.AsNoTracking()
                    on new { membership.TenantId, MembershipId = membership.Id } equals new { assignment.TenantId, assignment.MembershipId }
                join permission in database.TenantRolePermissions.AsNoTracking()
                    on new { assignment.TenantId, assignment.RoleId } equals new { permission.TenantId, permission.RoleId }
                where membership.TenantId == tenantId
                    && membership.UserId == userId
                    && membership.Status == MembershipStatus.Active.ToString()
                    && permission.PermissionCode == permissionCode
                select membership.Id).AnyAsync(cancellationToken);
    }
}
