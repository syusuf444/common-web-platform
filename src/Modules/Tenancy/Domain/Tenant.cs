namespace Platform.Modules.Tenancy.Domain;

public sealed class Tenant
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Status { get; set; } = "Active";

    public DateTimeOffset CreatedAtUtc { get; set; }
}
