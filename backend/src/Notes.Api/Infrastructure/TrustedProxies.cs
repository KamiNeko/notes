using System.Net;

namespace Notes.Api.Infrastructure;

/// <summary>Parses <see cref="NotesOptions.TrustedProxies"/>.</summary>
public static class TrustedProxies
{
    /// <summary>Loopback and the private ranges: a reverse proxy on the same host, in Docker or on the LAN.</summary>
    public static readonly string[] Defaults = ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];

    /// <summary>The networks in a comma-separated list of addresses and CIDR ranges; the defaults when it is empty.</summary>
    public static List<System.Net.IPNetwork> Parse(string? value)
    {
        var entries = string.IsNullOrWhiteSpace(value)
            ? Defaults
            : value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var networks = new List<System.Net.IPNetwork>();
        foreach (var entry in entries)
        {
            if (System.Net.IPNetwork.TryParse(entry, out var network))
            {
                networks.Add(network);
            }
            else if (IPAddress.TryParse(entry, out var address))
            {
                networks.Add(new System.Net.IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
            }
            else
            {
                throw new InvalidOperationException($"Notes__TrustedProxies: '{entry}' is neither an IP address nor a CIDR range");
            }
        }
        return networks;
    }
}
