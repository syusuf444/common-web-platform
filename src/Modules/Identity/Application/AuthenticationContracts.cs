namespace Platform.Modules.Identity.Application;

public sealed record RegisterRequest(string Email, string DisplayName, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthenticatedUserResponse(Guid Id, string Email, string DisplayName);

public sealed record CsrfTokenResponse(string RequestToken);
