using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Data;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Application;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Api.Identity;

/// <summary>Represents a tenant member and the tenant roles assigned to the membership.</summary>
public sealed record TenantMemberResponse(
    Guid MembershipId,
    Guid UserId,
    string Email,
    string DisplayName,
    string Status,
    DateTimeOffset JoinedAtUtc,
    IReadOnlyList<TenantMemberRoleResponse> Roles);

/// <summary>Represents a tenant role assigned to a membership.</summary>
public sealed record TenantMemberRoleResponse(Guid Id, string Name);

/// <summary>Payload for adding an existing account to the active tenant.</summary>
public sealed record AddTenantMemberRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }
}

/// <summary>Payload for changing a tenant membership's active or suspended status.</summary>
public sealed record UpdateTenantMembershipStatusRequest
{
    [Required]
    public required string Status { get; init; }
}

public enum AddTenantMemberOutcome
{
    Added,
    AccountNotFound,
    AlreadyMember,
}

public interface ITenantMemberManagementService
{
    Task<IReadOnlyList<TenantMemberResponse>> ListAsync(CancellationToken cancellationToken = default);

    Task<TenantMemberResponse?> GetAsync(Guid membershipId, CancellationToken cancellationToken = default);

    Task<(AddTenantMemberOutcome Outcome, TenantMemberResponse? Member)> AddAsync(string email, CancellationToken cancellationToken = default);

    Task<TenantMemberResponse?> UpdateStatusAsync(Guid membershipId, MembershipStatus status, CancellationToken cancellationToken = default);
}

public sealed class TenantMemberManagementService(
    PlatformDbContext database,
    UserManager<PlatformUser> users,
    ITenantContextAccessor tenantContext,
    TimeProvider timeProvider) : ITenantMemberManagementService
{
    public async Task<IReadOnlyList<TenantMemberResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var members = await (from membership in database.Memberships.AsNoTracking()
                             join user in database.Users.AsNoTracking() on membership.UserId equals user.Id
                             where membership.TenantId == tenantId
                             orderby user.DisplayName, user.Email
                             select new MemberProjection(
                                 membership.Id,
                                 user.Id,
                                 user.Email ?? string.Empty,
                                 user.DisplayName,
                                 membership.Status,
                                 membership.JoinedAtUtc))
            .ToListAsync(cancellationToken);

        return await AddRolesAsync(tenantId, members, cancellationToken);
    }

    public async Task<TenantMemberResponse?> GetAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var members = await (from membership in database.Memberships.AsNoTracking()
                             join user in database.Users.AsNoTracking() on membership.UserId equals user.Id
                             where membership.TenantId == tenantId && membership.Id == membershipId
                             select new MemberProjection(
                                 membership.Id,
                                 user.Id,
                                 user.Email ?? string.Empty,
                                 user.DisplayName,
                                 membership.Status,
                                 membership.JoinedAtUtc))
            .ToListAsync(cancellationToken);
        var withRoles = await AddRolesAsync(tenantId, members, cancellationToken);
        return withRoles.SingleOrDefault();
    }

    public async Task<(AddTenantMemberOutcome Outcome, TenantMemberResponse? Member)> AddAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var normalizedEmail = users.NormalizeEmail(email.Trim());
        var user = await users.Users.SingleOrDefaultAsync(
            item => item.NormalizedEmail == normalizedEmail,
            cancellationToken);

        if (user is null || user.Status != "Active")
        {
            return (AddTenantMemberOutcome.AccountNotFound, null);
        }

        var existingMembership = await database.Memberships
            .AsNoTracking()
            .AnyAsync(item => item.TenantId == tenantId && item.UserId == user.Id, cancellationToken);
        if (existingMembership)
        {
            return (AddTenantMemberOutcome.AlreadyMember, null);
        }

        var membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            Status = MembershipStatus.Active.ToString(),
            JoinedAtUtc = timeProvider.GetUtcNow(),
        };
        database.Memberships.Add(membership);
        await database.SaveChangesAsync(cancellationToken);

        var member = new MemberProjection(
            membership.Id,
            user.Id,
            user.Email ?? email.Trim(),
            user.DisplayName,
            membership.Status,
            membership.JoinedAtUtc);
        var withRoles = await AddRolesAsync(tenantId, [member], cancellationToken);
        return (AddTenantMemberOutcome.Added, withRoles.Single());
    }

    public async Task<TenantMemberResponse?> UpdateStatusAsync(
        Guid membershipId,
        MembershipStatus status,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var membership = await database.Memberships.SingleOrDefaultAsync(
            item => item.TenantId == tenantId && item.Id == membershipId,
            cancellationToken);
        if (membership is null)
        {
            return null;
        }

        membership.Status = status.ToString();
        await database.SaveChangesAsync(cancellationToken);
        return await GetAsync(membershipId, cancellationToken);
    }

    private async Task<IReadOnlyList<TenantMemberResponse>> AddRolesAsync(
        Guid tenantId,
        IReadOnlyList<MemberProjection> members,
        CancellationToken cancellationToken)
    {
        if (members.Count == 0)
        {
            return [];
        }

        var membershipIds = members.Select(item => item.MembershipId).ToArray();
        var assignments = await (from assignment in database.MembershipRoles.AsNoTracking()
                                 join role in database.TenantRoles.AsNoTracking()
                                     on new { assignment.TenantId, assignment.RoleId }
                                     equals new { role.TenantId, RoleId = role.Id }
                                 where assignment.TenantId == tenantId && membershipIds.Contains(assignment.MembershipId)
                                 select new
                                 {
                                     assignment.MembershipId,
                                     Role = new TenantMemberRoleResponse(role.Id, role.Name),
                                 })
            .ToListAsync(cancellationToken);
        var rolesByMembership = assignments
            .GroupBy(item => item.MembershipId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TenantMemberRoleResponse>)group.Select(item => item.Role).ToArray());

        return members.Select(member => new TenantMemberResponse(
            member.MembershipId,
            member.UserId,
            member.Email,
            member.DisplayName,
            member.Status,
            member.JoinedAtUtc,
            rolesByMembership.GetValueOrDefault(member.MembershipId, []))).ToArray();
    }

    private sealed record MemberProjection(
        Guid MembershipId,
        Guid UserId,
        string Email,
        string DisplayName,
        string Status,
        DateTimeOffset JoinedAtUtc);
}
