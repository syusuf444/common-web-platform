using Microsoft.AspNetCore.Identity;

namespace Platform.Modules.Identity.Domain;

public sealed class PlatformUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public string Status { get; set; } = "Active";

    public DateTimeOffset CreatedAtUtc { get; set; }
}
