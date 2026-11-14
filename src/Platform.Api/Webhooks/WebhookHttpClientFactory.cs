using System.Net;
using System.Net.Sockets;

namespace Platform.Api.Webhooks;

public static class WebhookHttpClientNames
{
    public const string Delivery = "WebhookDelivery";
}

public static class WebhookHttpClientFactory
{
    public static SocketsHttpHandler CreateHandler(WebhookEndpointPolicy endpointPolicy)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 8,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var endpoint = context.DnsEndPoint;
                if (!endpointPolicy.IsHostAllowed(endpoint.Host))
                {
                    throw new HttpRequestException("Webhook destination is not allowed.");
                }

                IPAddress[] addresses = IPAddress.TryParse(endpoint.Host, out var address)
                    ? [address]
                    : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);
                var permittedAddresses = addresses
                    .Where(address => IsPermittedAddress(
                        address,
                        endpointPolicy.IsDevelopment && endpointPolicy.IsLoopbackHost(endpoint.Host)))
                    .ToArray();
                if (permittedAddresses.Length == 0)
                {
                    throw new HttpRequestException("Webhook destination resolved to no permitted IP addresses.");
                }

                SocketException? lastSocketError = null;
                foreach (var resolvedAddress in permittedAddresses)
                {
                    var socket = new Socket(resolvedAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(resolvedAddress, endpoint.Port, cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException exception)
                    {
                        socket.Dispose();
                        lastSocketError = exception;
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }

                throw new HttpRequestException("Could not connect to the webhook host.", lastSocketError);
            },
        };
    }

    private static bool IsPermittedAddress(IPAddress address, bool allowLoopback)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return allowLoopback;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var ipv6Bytes = address.GetAddressBytes();
            return (ipv6Bytes[0] & 0xE0) == 0x20
                && !(ipv6Bytes[0] == 0x20 && ipv6Bytes[1] == 0x01 && ipv6Bytes[2] == 0x0D && ipv6Bytes[3] == 0xB8)
                && !address.IsIPv6LinkLocal
                && !address.IsIPv6SiteLocal
                && !address.IsIPv6UniqueLocal
                && !address.IsIPv6Multicast;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] != 0
            && bytes[0] != 10
            && bytes[0] != 127
            && bytes[0] < 224
            && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            && !(bytes[0] == 169 && bytes[1] == 254)
            && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            && !(bytes[0] == 192 && bytes[1] == 168)
            && !(bytes[0] == 192 && bytes[1] == 0)
            && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
            && !(bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99)
            && !(bytes[0] == 198 && bytes[1] is 18 or 19)
            && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
            && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
    }
}
