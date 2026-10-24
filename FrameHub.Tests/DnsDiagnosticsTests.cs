using FrameHub.Core.Services.Network;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Sockets;

namespace FrameHub.Tests;

/// <summary>
/// Researcher R2 / dyrektywa §C, FAZA 1 (diagnostyka + podgląd, zero mutacji).
/// Latency is measured against a loopback stub responder, never against live
/// public resolvers, so the suite performs no external network I/O and changes
/// nothing on the machine (acceptance: system DNS identical before/after).
/// </summary>
[TestClass]
public sealed class DnsDiagnosticsTests
{
    [TestMethod]
    public void Presets_HaveValidIpv4AndDohTemplates()
    {
        Assert.AreEqual(4, DnsDiagnosticsService.Presets.Count);
        foreach (var preset in DnsDiagnosticsService.Presets)
        {
            Assert.IsTrue(preset.Ipv4Servers.Length >= 2, $"{preset.Id} must list at least two IPv4 resolvers.");
            foreach (string ip in preset.Ipv4Servers)
            {
                Assert.IsTrue(IPAddress.TryParse(ip, out _), $"{preset.Id}: '{ip}' is not a valid IP address.");
            }

            Assert.IsFalse(string.IsNullOrWhiteSpace(preset.DohTemplate));
            Assert.IsTrue(Uri.TryCreate(preset.DohTemplate, UriKind.Absolute, out Uri? doh) && doh.Scheme == Uri.UriSchemeHttps,
                $"{preset.Id}: DoH template must be an absolute https URI.");
        }
    }

    [TestMethod]
    public void BuildPreviewCommands_ContainsInterfaceAndServers_NoExecution()
    {
        var preset = DnsDiagnosticsService.Presets.Single(p => p.Id == "cloudflare");
        var commands = DnsDiagnosticsService.BuildPreviewCommands("Ethernet", preset);

        Assert.IsTrue(commands.Count >= 3);
        Assert.IsTrue(commands.Any(c => c.Contains("Ethernet") && c.Contains("1.1.1.1") && c.Contains("1.0.0.1")),
            "Preview must show the interface and the IPv4 servers that would be set.");
        Assert.IsTrue(commands.Any(c => c.Contains("cloudflare-dns.com/dns-query")),
            "Preview must show the DoH template that would be registered.");
        Assert.IsTrue(commands.Any(c => c.Contains("-ResetServerAddresses")),
            "Preview must always include the DHCP rollback command.");
        Assert.ThrowsException<ArgumentException>(() => DnsDiagnosticsService.BuildPreviewCommands("  ", preset));
    }

    [TestMethod]
    public async Task MeasureResolverLatency_LoopbackStub_ReturnsSuccess()
    {
        using var stub = new LoopbackDnsStub();
        var service = new DnsDiagnosticsService();

        DnsDiagnosticsService.ResolverLatencyResult result = await service.MeasureResolverLatencyAsync(
            "example.com", "127.0.0.1", TimeSpan.FromSeconds(5), stub.Port);

        Assert.IsTrue(result.Success, "Loopback stub must produce a valid DNS response. Error: " + result.Error);
        Assert.IsTrue(result.LatencyMs >= 0);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public async Task MeasureResolverLatency_ClosedPort_ReturnsTimeoutFailure()
    {
        int closedPort;
        using (var probe = new UdpClient(AddressFamily.InterNetwork))
        {
            probe.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            closedPort = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        }

        var service = new DnsDiagnosticsService();
        DnsDiagnosticsService.ResolverLatencyResult result = await service.MeasureResolverLatencyAsync(
            "example.com", "127.0.0.1", TimeSpan.FromSeconds(2), closedPort);

        Assert.IsFalse(result.Success, "A closed port must surface as an explicit failure, never a hang.");
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public async Task MeasureResolverLatency_InvalidInputs_FailFastWithoutNetwork()
    {
        var service = new DnsDiagnosticsService();

        DnsDiagnosticsService.ResolverLatencyResult badHost = await service.MeasureResolverLatencyAsync(
            "  ", "127.0.0.1", TimeSpan.FromSeconds(1), 5353);
        Assert.IsFalse(badHost.Success);

        DnsDiagnosticsService.ResolverLatencyResult badResolver = await service.MeasureResolverLatencyAsync(
            "example.com", "not-an-ip", TimeSpan.FromSeconds(1), 5353);
        Assert.IsFalse(badResolver.Success);
    }

    [TestMethod]
    public void BuildPreviewCommands_EscapesPowerShellMetacharacters()
    {
        var preset = DnsDiagnosticsService.Presets.Single(p => p.Id == "cloudflare");
        var commands = DnsDiagnosticsService.BuildPreviewCommands("Foo\"; $x", preset);

        // The raw break-out sequence must not survive into the preview text.
        Assert.IsFalse(commands.Any(c => c.Contains("Foo\";")),
            "An unescaped quote would terminate the PowerShell string early.");
        var aliasCommands = commands.Where(c => c.Contains("Foo")).ToList();
        Assert.IsTrue(aliasCommands.Count >= 3, "Alias-bearing commands must still be present.");
        Assert.IsTrue(aliasCommands.All(c => c.Contains("Foo`\"; `$x")),
            "Quotes and $ must be backtick-escaped for PowerShell double-quoted strings.");

        // Plain aliases are byte-identical to before (no behavior change).
        var plain = DnsDiagnosticsService.BuildPreviewCommands("Ethernet", preset);
        Assert.IsTrue(plain.Any(c => c.Contains("\"Ethernet\"") && c.Contains("1.1.1.1")));
    }

    [TestMethod]
    public void BuildPreviewCommands_RejectsControlCharacters()
    {
        var preset = DnsDiagnosticsService.Presets.Single(p => p.Id == "cloudflare");
        Assert.ThrowsException<ArgumentException>(() => DnsDiagnosticsService.BuildPreviewCommands("Eth\r\nernet", preset));
    }

    [TestMethod]
    public void GetAdapters_DoesNotThrow_ReturnsNamedEntries()
    {
        var service = new DnsDiagnosticsService();
        var adapters = service.GetAdapters();

        Assert.IsNotNull(adapters);
        foreach (var adapter in adapters)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(adapter.Name));
        }
    }

    /// <summary>Minimal in-test DNS responder: echoes the query with one A record.</summary>
    private sealed class LoopbackDnsStub : IDisposable
    {
        private readonly UdpClient _udp = new(AddressFamily.InterNetwork);
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public int Port { get; }

        public LoopbackDnsStub()
        {
            _udp.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
            _loop = Task.Run(RunAsync);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _udp.Close();
            try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts.Dispose();
        }

        private async Task RunAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                UdpReceiveResult received;
                try
                {
                    received = await _udp.ReceiveAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }

                try
                {
                    byte[] response = BuildResponse(received.Buffer);
                    await _udp.SendAsync(response, received.RemoteEndPoint, _cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort stub: a malformed query simply gets no answer.
                }
            }
        }

        private static byte[] BuildResponse(byte[] query)
        {
            if (query.Length < 12)
            {
                throw new InvalidOperationException("Truncated query.");
            }

            // Locate the end of QNAME to echo the whole question section.
            int offset = 12;
            while (offset < query.Length && query[offset] != 0)
            {
                offset += query[offset] + 1;
            }

            int questionLength = (offset + 1 + 4) - 12; // QNAME + root + QTYPE + QCLASS
            if (offset + 5 > query.Length || questionLength <= 0)
            {
                throw new InvalidOperationException("Malformed question.");
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(query[0]);
            writer.Write(query[1]); // transaction ID echo
            writer.Write((byte)0x81);
            writer.Write((byte)0x80); // standard response, no error
            writer.Write(IPAddress.HostToNetworkOrder((short)1)); // QDCOUNT
            writer.Write(IPAddress.HostToNetworkOrder((short)1)); // ANCOUNT
            writer.Write((short)0);
            writer.Write((short)0);
            writer.Write(query, 12, questionLength); // question echo
            writer.Write((byte)0xC0);
            writer.Write((byte)0x0C); // NAME pointer to QNAME
            writer.Write(IPAddress.HostToNetworkOrder((short)1)); // TYPE A
            writer.Write(IPAddress.HostToNetworkOrder((short)1)); // CLASS IN
            writer.Write(IPAddress.HostToNetworkOrder(60)); // TTL
            writer.Write(IPAddress.HostToNetworkOrder((short)4)); // RDLENGTH
            writer.Write(new byte[] { 93, 184, 216, 34 }); // example.com
            return stream.ToArray();
        }
    }
}
