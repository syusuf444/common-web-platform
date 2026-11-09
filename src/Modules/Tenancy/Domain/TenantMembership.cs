namespace Platform.Modules.Tenancy.Domain;

public sealed class TenantMembership
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Status { get; set; } = MembershipStatus.Active.ToString();

    public DateTimeOffset JoinedAtUtc { get; set; }
}
