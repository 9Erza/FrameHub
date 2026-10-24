using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace FrameHub.Core.Services.Network;

/// <summary>
/// DNS diagnostics and change preview (dyrektywa §C / Researcher R2, FAZA 1).
/// <para>
/// Read-only by design: adapter enumeration, current per-interface DNS servers
/// (DHCP vs static), per-resolver latency, and the exact commands a future change
/// would run (preview only). There is deliberately NO mutation API — applying
/// DNS changes system-wide requires explicit user action, elevation, a verified
/// per-interface rollback and connectivity self-checks (FAZA 2, not implemented).
/// Nothing in this service touches the registry, netsh, or any network adapter
/// configuration; QA acceptance for Faza 1 is "no system change after use".
/// </para>
/// <para>Resolver addresses below are the vendors' published public anycast
/// endpoints (Cloudflare 1.1.1.1, Google 8.8.8.8, Quad9 9.9.9.9, OpenDNS
/// 208.67.222.222) with their documented DNS-over-HTTPS templates. No claim is
/// made that changing DNS improves FPS or game ping — it does not.</para>
/// </summary>
public sealed class DnsDiagnosticsService
{
    public sealed record DnsPreset(
        string Id,
        string DisplayName,
        string[] Ipv4Servers,
        string[] Ipv6Servers,
        string? DohTemplate);

    public static readonly IReadOnlyList<DnsPreset> Presets =
    [
        new("cloudflare", "Cloudflare",
            ["1.1.1.1", "1.0.0.1"],
            ["2606:4700:4700::1111", "2606:4700:4700::1001"],
            "https://cloudflare-dns.com/dns-query"),
        new("google", "Google Public DNS",
            ["8.8.8.8", "8.8.4.4"],
            ["2001:4860:4860::8888", "2001:4860:4860::8844"],
            "https://dns.google/dns-query"),
        new("quad9", "Quad9",
            ["9.9.9.9", "149.112.112.112"],
            ["2620:fe::fe", "2620:fe::9"],
            "https://dns.quad9.net/dns-query"),
        new("opendns", "OpenDNS",
            ["208.67.222.222", "208.67.220.220"],
            ["2620:119:35::35", "2620:119:53::53"],
            "https://doh.opendns.com/dns-query")
    ];

    public sealed record AdapterDnsInfo(
        string Name,
        string Description,
        OperationalStatus Status,
        bool IsUp,
        string[] Ipv4Addresses,
        string[] Ipv6Addresses,
        string[] DnsServers,
        bool DnsFromDhcp);

    public sealed record ResolverLatencyResult(
        string Resolver,
        string Hostname,
        bool Success,
        double LatencyMs,
        string? Error);

    /// <summary>Read-only adapter + DNS snapshot. No elevation required.</summary>
    public IReadOnlyList<AdapterDnsInfo> GetAdapters()
    {
        var result = new List<AdapterDnsInfo>();
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            IPInterfaceProperties properties;
            bool dnsFromDhcp;
            try
            {
                properties = adapter.GetIPProperties();
                dnsFromDhcp = adapter.OperationalStatus == OperationalStatus.Up
                    && SafeIsDhcpEnabled(properties);
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            string[] dnsServers = properties.DnsAddresses.Select(address => address.ToString()).ToArray();

            result.Add(new AdapterDnsInfo(
                adapter.Name,
                adapter.Description,
                adapter.OperationalStatus,
                adapter.OperationalStatus == OperationalStatus.Up,
                properties.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .ToArray(),
                properties.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal)
                    .Select(a => a.Address.ToString())
                    .ToArray(),
                dnsServers,
                dnsFromDhcp));
        }

        return result;
    }

    private static bool SafeIsDhcpEnabled(IPInterfaceProperties properties)
    {
        try
        {
            return properties.GetIPv4Properties() is { } ipv4 && ipv4.IsDhcpEnabled;
        }
        catch (NetworkInformationException)
        {
            // Some virtual/tunnel adapters expose no IPv4 stack: treat DHCP state as unknown.
            return false;
        }
    }

    /// <summary>
    /// Builds the exact commands a future (Faza 2) change would execute, for user
    /// review. Preview only — nothing is executed here.
    /// </summary>
    public static IReadOnlyList<string> BuildPreviewCommands(string interfaceAlias, DnsPreset preset)
    {
        string validatedAlias = (interfaceAlias ?? string.Empty).Trim();
        if (validatedAlias.Length == 0)
        {
            throw new ArgumentException("Interface alias must not be empty.", nameof(interfaceAlias));
        }

        ArgumentNullException.ThrowIfNull(preset);
        var commands = new List<string>();
        if (preset.Ipv4Servers.Length > 0)
        {
            commands.Add($"Set-DnsClientServerAddress -InterfaceAlias \"{validatedAlias}\" -ServerAddresses (\"{string.Join("\", \"", preset.Ipv4Servers)}\")  # IPv4, overwrites DHCP");
        }

        if (preset.Ipv6Servers.Length > 0)
        {
            commands.Add($"# IPv6 via netsh (per-interface, validated alias): netsh interface ipv6 set dnsservers \"{validatedAlias}\" static {preset.Ipv6Servers[0]} primary");
        }

        if (!string.IsNullOrWhiteSpace(preset.DohTemplate) && preset.Ipv4Servers.Length > 0)
        {
            foreach (string server in preset.Ipv4Servers)
            {
                commands.Add($"Add-DnsClientDohServerAddress -ServerAddress \"{server}\" -DohTemplate \"{preset.DohTemplate}\" -AllowFallbackToUdp $false");
            }
        }

        commands.Add($"# Rollback to automatic (DHCP): Set-DnsClientServerAddress -InterfaceAlias \"{validatedAlias}\" -ResetServerAddresses");
        return commands;
    }

    /// <summary>
    /// Measures one DNS A-query round trip against a specific resolver over UDP
    /// (default DNS port 53; <paramref name="port"/> exists for loopback test
    /// stubs). Read-only: sends a single standard query, no system changes.
    /// </summary>
    public async Task<ResolverLatencyResult> MeasureResolverLatencyAsync(
        string hostname,
        string resolverIp,
        TimeSpan timeout,
        int port = 53,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return new ResolverLatencyResult(resolverIp, hostname, false, 0, "Hostname must not be empty.");
        }

        if (!IPAddress.TryParse(resolverIp, out IPAddress? resolver))
        {
            return new ResolverLatencyResult(resolverIp, hostname, false, 0, "Resolver is not a valid IP address.");
        }

        byte[] query;
        ushort transactionId;
        try
        {
            (query, transactionId) = BuildQuery(hostname);
        }
        catch (ArgumentException ex)
        {
            return new ResolverLatencyResult(resolverIp, hostname, false, 0, ex.Message);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var udp = new UdpClient(resolver.AddressFamily);
            udp.Connect(new IPEndPoint(resolver, port));
            await udp.SendAsync(query, cancellationToken).ConfigureAwait(false);

            Task<UdpReceiveResult> receiveTask = udp.ReceiveAsync(cancellationToken).AsTask();
            Task completed = await Task.WhenAny(receiveTask, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
            if (completed != receiveTask)
            {
                return new ResolverLatencyResult(resolverIp, hostname, false, stopwatch.Elapsed.TotalMilliseconds, "Timed out waiting for a DNS response.");
            }

            byte[] response = receiveTask.GetAwaiter().GetResult().Buffer;
            string? error = ValidateResponse(response, transactionId);
            return error == null
                ? new ResolverLatencyResult(resolverIp, hostname, true, stopwatch.Elapsed.TotalMilliseconds, null)
                : new ResolverLatencyResult(resolverIp, hostname, false, stopwatch.Elapsed.TotalMilliseconds, error);
        }
        catch (OperationCanceledException)
        {
            return new ResolverLatencyResult(resolverIp, hostname, false, stopwatch.Elapsed.TotalMilliseconds, "Operation was cancelled.");
        }
        catch (Exception ex) when (ex is SocketException or NetworkInformationException)
        {
            return new ResolverLatencyResult(resolverIp, hostname, false, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
        }
    }

    internal static (byte[] Query, ushort TransactionId) BuildQuery(string hostname)
    {
        string[] labels = hostname.Trim().TrimEnd('.').Split('.');
        if (labels.Length == 0 || labels.Any(label => label.Length == 0 || label.Length > 63))
        {
            throw new ArgumentException("Hostname contains an empty or oversized label.", nameof(hostname));
        }

        ushort transactionId = (ushort)Random.Shared.Next(ushort.MaxValue + 1);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(IPAddress.HostToNetworkOrder((short)transactionId));
        writer.Write(IPAddress.HostToNetworkOrder((short)0x0100)); // RD
        writer.Write(IPAddress.HostToNetworkOrder((short)1)); // QDCOUNT
        writer.Write((short)0); // ANCOUNT
        writer.Write((short)0); // NSCOUNT
        writer.Write((short)0); // ARCOUNT
        foreach (string label in labels)
        {
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(label);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }

        writer.Write((byte)0); // root
        writer.Write(IPAddress.HostToNetworkOrder((short)1)); // QTYPE A
        writer.Write(IPAddress.HostToNetworkOrder((short)1)); // QCLASS IN
        return (stream.ToArray(), transactionId);
    }

    internal static string? ValidateResponse(byte[] response, ushort transactionId)
    {
        if (response.Length < 12)
        {
            return "Truncated DNS response.";
        }

        ushort actualId = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(response, 0));
        if (actualId != transactionId)
        {
            return "Transaction ID mismatch.";
        }

        int rcode = response[3] & 0x0F;
        if (rcode != 0)
        {
            return $"Resolver returned RCODE {rcode}.";
        }

        return null;
    }
}
