namespace Platform.Modules.Identity.Domain;

public sealed class MembershipRole
{
    public Guid TenantId { get; set; }

    public Guid MembershipId { get; set; }

    public Guid RoleId { get; set; }

    public DateTimeOffset AssignedAtUtc { get; set; }
}
