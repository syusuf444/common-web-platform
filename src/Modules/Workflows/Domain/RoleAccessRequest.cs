namespace Platform.Modules.Workflows.Domain;

public sealed class RoleAccessRequest
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid RequesterMembershipId { get; set; }

    public Guid RequesterUserId { get; set; }

    public Guid RoleId { get; set; }

    public string Status { get; set; } = RoleAccessRequestStatuses.Pending;

    public DateTimeOffset RequestedAtUtc { get; set; }

    public DateTimeOffset? DecidedAtUtc { get; set; }

    public Guid? DecidedByUserId { get; set; }
}

public static class RoleAccessRequestStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}
