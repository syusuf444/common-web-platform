using System.Text.RegularExpressions;
using System.Text;

namespace Platform.Modules.Webhooks.Application;

public static partial class WebhookRequestValidator
{
    public const int MaximumEventTypeLength = 100;
    public const int MaximumPayloadLength = 64 * 1024;
    public const int MaximumEndpointLength = 2048;

    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex EventTypePattern();

    public static Dictionary<string, string[]> Validate(
        string? eventType,
        string? endpointUrl,
        string? payloadJson = null)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(eventType)
            || eventType.Length > MaximumEventTypeLength
            || !EventTypePattern().IsMatch(eventType))
        {
            errors[nameof(CreateWebhookSubscriptionRequest.EventType)] = ["Event type must be a dot, dash, or underscore separated lowercase identifier up to 100 characters."];
        }

        if (string.IsNullOrWhiteSpace(endpointUrl)
            || endpointUrl.Length > MaximumEndpointLength
            || !Uri.TryCreate(endpointUrl, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("https" or "http")
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            errors[nameof(CreateWebhookSubscriptionRequest.EndpointUrl)] = ["Endpoint must be an absolute HTTP(S) URL without user info or a fragment and be at most 2048 characters."];
        }

        if (payloadJson is not null)
        {
            if (Encoding.UTF8.GetByteCount(payloadJson) > MaximumPayloadLength)
            {
                errors[nameof(WebhookEvent.PayloadJson)] = [$"Payload cannot exceed {MaximumPayloadLength} bytes."];
            }
            else
            {
                try
                {
                    using var document = System.Text.Json.JsonDocument.Parse(payloadJson);
                }
                catch (System.Text.Json.JsonException)
                {
                    errors[nameof(WebhookEvent.PayloadJson)] = ["Payload must be valid JSON."];
                }
            }
        }

        return errors;
    }
}
