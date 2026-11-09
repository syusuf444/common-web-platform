namespace Platform.Api.Data;

public sealed class TenantSetting
{
    public Guid TenantId { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Guid UpdatedByUserId { get; set; }
}
