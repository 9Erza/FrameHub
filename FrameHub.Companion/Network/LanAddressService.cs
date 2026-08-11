using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FrameHub.Companion.Models;

namespace FrameHub.Companion.Network;

public static class LanAddressService
{
    public static bool IsRfc1918Private(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return false;

        byte[] bytes = ip.GetAddressBytes();
        if (bytes[0] == 10)
            return true;

        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            return true;

        if (bytes[0] == 192 && bytes[1] == 168)
            return true;

        return false;
    }

    public static List<LanCandidateIp> GetAvailableLanAddresses()
    {
        var candidates = new List<LanCandidateIp>();

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                IPInterfaceProperties props;
                try
                {
                    props = ni.GetIPProperties();
                }
                catch
                {
                    continue;
                }

                foreach (var unicast in props.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var ip = unicast.Address;
                        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.None))
                            continue;

                        if (!IsRfc1918Private(ip))
                            continue;

                        string ipStr = ip.ToString();
                        if (!candidates.Any(c => c.IpAddress.Equals(ipStr, StringComparison.OrdinalIgnoreCase)))
                        {
                            candidates.Add(new LanCandidateIp(ipStr, ni.Name, ni.Description));
                        }
                    }
                }
            }
        }
        catch
        {
            // Defensive cleanup: return whatever candidates were gathered or empty list
        }

        return candidates;
    }

    public static bool IsValidLanAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return false;

        if (!IPAddress.TryParse(ipAddress.Trim(), out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            return false;

        if (!IsRfc1918Private(parsed))
            return false;

        if (IPAddress.IsLoopback(parsed) || parsed.Equals(IPAddress.Any) || parsed.Equals(IPAddress.None))
            return false;

        var available = GetAvailableLanAddresses();
        return available.Any(c => c.IpAddress.Equals(ipAddress.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
