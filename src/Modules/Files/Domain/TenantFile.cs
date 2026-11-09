namespace Platform.Modules.Files.Domain;

public sealed class TenantFile
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UploadedByUserId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTimeOffset UploadedAtUtc { get; set; }
}
