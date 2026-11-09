using System.ComponentModel.DataAnnotations;

namespace Platform.Modules.Identity.Application;

public static class RegistrationRequestValidator
{
    public static Dictionary<string, string[]> Validate(RegisterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!new EmailAddressAttribute().IsValid(request.Email))
        {
            errors[nameof(request.Email)] = ["A valid email address is required."];
        }

        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 120)
        {
            errors[nameof(request.DisplayName)] = ["Display name must be between 1 and 120 characters."];
        }

        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 12)
        {
            errors[nameof(request.Password)] = ["Password must be at least 12 characters."];
        }

        return errors;
    }
}
