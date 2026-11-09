namespace Platform.Modules.Identity.Domain;

public sealed class TenantRole
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
