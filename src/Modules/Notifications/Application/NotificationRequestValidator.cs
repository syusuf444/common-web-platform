using System.Net.Mail;

namespace Platform.Modules.Notifications.Application;

public static class NotificationRequestValidator
{
    public static Dictionary<string, string[]> Validate(SendNotificationRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var recipient = request.Recipient?.Trim();
        if (string.IsNullOrEmpty(recipient)
            || recipient.Length > 320
            || !MailAddress.TryCreate(recipient, out var address)
            || !string.Equals(address.Address, recipient, StringComparison.OrdinalIgnoreCase))
        {
            errors[nameof(SendNotificationRequest.Recipient)] = ["Recipient must be a single valid email address (maximum 320 characters)."];
        }

        var subject = request.Subject?.Trim();
        if (string.IsNullOrEmpty(subject)
            || subject.Length > 200
            || subject.Any(char.IsControl))
        {
            errors[nameof(SendNotificationRequest.Subject)] = ["Subject is required, cannot contain control characters, and must be 200 characters or fewer."];
        }

        if (string.IsNullOrWhiteSpace(request.Body)
            || request.Body.Length > 10000
            || request.Body.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
        {
            errors[nameof(SendNotificationRequest.Body)] = ["Body is required, must be plain text of 10000 characters or fewer, and cannot contain control characters."];
        }

        return errors;
    }
}
