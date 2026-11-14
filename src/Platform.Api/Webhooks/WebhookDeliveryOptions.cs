using System.Net;

namespace Platform.Api.Webhooks;

public sealed class WebhookDeliveryOptions
{
    public string[] AllowedHosts { get; set; } = [];

    public double InitialRetryDelaySeconds { get; set; } = 15;

    public double MaximumRetryDelaySeconds { get; set; } = 900;

    public bool BackgroundWorkerEnabled { get; set; } = true;
}

public sealed class WebhookEndpointPolicy(
    Microsoft.Extensions.Options.IOptions<WebhookDeliveryOptions> options,
    IHostEnvironment environment)
{
    private readonly HashSet<string> _allowedHosts = options.Value.AllowedHosts
        .Where(host => !string.IsNullOrWhiteSpace(host))
        .Select(host => host.Trim().TrimEnd('.'))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool IsAllowed(Uri endpoint)
    {
        return endpoint.IsAbsoluteUri
            && (endpoint.Scheme == Uri.UriSchemeHttps
                || (environment.IsDevelopment() && endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback))
            && string.IsNullOrEmpty(endpoint.UserInfo)
            && string.IsNullOrEmpty(endpoint.Fragment)
            && IsHostAllowed(endpoint.IdnHost);
    }

    public bool IsHostAllowed(string host) => _allowedHosts.Contains(host.TrimEnd('.'));

    public bool IsLoopbackHost(string host)
    {
        return IPAddress.TryParse(host, out var address)
            ? IPAddress.IsLoopback(address)
            : string.Equals(host.TrimEnd('.'), "localhost", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsDevelopment => environment.IsDevelopment();
}
