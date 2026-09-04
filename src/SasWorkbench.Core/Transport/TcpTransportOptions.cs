using System.Net;

namespace SasWorkbench.Core.Transport;

/// <summary>Which end of the socket this process is.</summary>
public enum TcpRole
{
    /// <summary>Bind the endpoint and wait for the peer to arrive.</summary>
    Listen,

    /// <summary>Dial the peer, retrying until it appears.</summary>
    Connect,
}

/// <summary>How to reach the peer over TCP.</summary>
/// <param name="Endpoint">Address and port of the link.</param>
/// <param name="Role">Whether to wait for the peer or dial it.</param>
/// <param name="ReconnectInterval">How long to wait between attempts while no peer is attached.</param>
public sealed record TcpTransportOptions(
    IPEndPoint Endpoint,
    TcpRole Role = TcpRole.Listen,
    TimeSpan? ReconnectInterval = null)
{
    /// <summary>The port the transport shim dials by default.</summary>
    public const int DefaultPort = 9760;

    /// <summary>Interval between connection attempts when none was given.</summary>
    public TimeSpan Reconnect => ReconnectInterval ?? TimeSpan.FromMilliseconds(250);

    /// <summary>Parses an endpoint written as address and port, address alone, or colon and port.</summary>
    public static bool TryParseEndpoint(string? text, out IPEndPoint endpoint)
    {
        endpoint = new IPEndPoint(IPAddress.Loopback, DefaultPort);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string trimmed = text.Trim();
        if (IPEndPoint.TryParse(trimmed, out IPEndPoint? parsed) && parsed.Port != 0)
        {
            endpoint = parsed;
            return true;
        }

        if (trimmed.Length > 1 && trimmed[0] == ':'
            && int.TryParse(trimmed[1..], out int portOnly)
            && portOnly is > 0 and <= 65535)
        {
            endpoint = new IPEndPoint(IPAddress.Loopback, portOnly);
            return true;
        }

        if (IPAddress.TryParse(trimmed, out IPAddress? hostOnly))
        {
            endpoint = new IPEndPoint(hostOnly, DefaultPort);
            return true;
        }

        return false;
    }
}
