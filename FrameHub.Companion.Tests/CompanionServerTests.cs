using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using FrameHub.Companion;
using FrameHub.Companion.Models;
using FrameHub.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Companion.Tests;

[TestClass]
public sealed class CompanionServerTests
{
    [TestMethod]
    public void DefaultConfigurationPort_Is47821()
    {
        var companionOptions = new CompanionOptions();
        var appSettings = new AppSettings();

        Assert.AreEqual(47821, companionOptions.Port);
        Assert.AreEqual(47821, appSettings.CompanionPort);
        Assert.IsFalse(companionOptions.Enabled);
        Assert.IsFalse(appSettings.CompanionEnabled);
    }

    [TestMethod]
    public async Task DisabledByDefault_DoesNotStartServer()
    {
        await using var server = new CompanionServer();
        var options = new CompanionOptions { Enabled = false, Port = GetFreePort() };

        bool started = await server.StartAsync(options);

        Assert.IsFalse(started);
        Assert.AreEqual(CompanionServiceState.Stopped, server.Status.State);
        Assert.IsNull(server.Status.BoundAddress);
    }

    [TestMethod]
    public async Task StartAsync_BindsToLoopback_AndRespondsToStatusEndpoint()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer();
        var options = new CompanionOptions { Enabled = true, Port = port };

        bool started = await server.StartAsync(options);

        Assert.IsTrue(started);
        Assert.AreEqual(CompanionServiceState.Running, server.Status.State);
        Assert.AreEqual($"http://127.0.0.1:{port}", server.Status.BoundAddress);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/status");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<CompanionStatusDto>();

        Assert.IsNotNull(dto);
        Assert.AreEqual("FrameHub Companion", dto.Service);
        Assert.AreEqual("1", dto.ApiVersion);
        Assert.IsFalse(string.IsNullOrWhiteSpace(dto.AppVersion));
        Assert.AreEqual("ready", dto.State);
    }

    [TestMethod]
    public async Task ConcurrentStartAsync_OnlyOneHostCreatedAndCoherentState()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer();
        var options = new CompanionOptions { Enabled = true, Port = port };

        var tasks = Enumerable.Range(0, 10).Select(_ => server.StartAsync(options)).ToArray();
        bool[] results = await Task.WhenAll(tasks);

        Assert.IsTrue(results.All(r => r));
        Assert.AreEqual(CompanionServiceState.Running, server.Status.State);
        Assert.AreEqual($"http://127.0.0.1:{port}", server.Status.BoundAddress);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/status");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task StartStopInterleaving_ResultsInStoppedServer()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer();
        var options = new CompanionOptions { Enabled = true, Port = port };

        var startTask = server.StartAsync(options);
        var stopTask = server.StopAsync();

        await Task.WhenAll(startTask, stopTask);

        Assert.AreEqual(CompanionServiceState.Stopped, server.Status.State);
        Assert.IsNull(server.Status.BoundAddress);

        using var client = new HttpClient();
        await Assert.ThrowsExceptionAsync<HttpRequestException>(async () =>
        {
            await client.GetAsync($"http://127.0.0.1:{port}/api/v1/status");
        });
    }

    [TestMethod]
    public async Task RestartAfterCleanStop_Succeeds()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer();
        var options = new CompanionOptions { Enabled = true, Port = port };

        bool firstStart = await server.StartAsync(options);
        Assert.IsTrue(firstStart);

        await server.StopAsync();
        Assert.AreEqual(CompanionServiceState.Stopped, server.Status.State);

        bool secondStart = await server.StartAsync(options);
        Assert.IsTrue(secondStart);
        Assert.AreEqual(CompanionServiceState.Running, server.Status.State);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/status");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task RestartAfterFailedBind_SucceedsWhenPortFreed()
    {
        int port = GetFreePort();

        // Occupy port
        var listener = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
        listener.Start();

        await using var server = new CompanionServer();
        var options = new CompanionOptions { Enabled = true, Port = port };

        try
        {
            bool firstStart = await server.StartAsync(options);
            Assert.IsFalse(firstStart);
            Assert.AreEqual(CompanionServiceState.Failed, server.Status.State);
        }
        finally
        {
            listener.Stop();
        }

        // Now start on freed port
        bool secondStart = await server.StartAsync(options);
        Assert.IsTrue(secondStart);
        Assert.AreEqual(CompanionServiceState.Running, server.Status.State);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/status");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task BindFailure_DoesNotLeaveOrphanedListener()
    {
        int port = GetFreePort();

        var listener = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
        listener.Start();

        try
        {
            await using var server = new CompanionServer();
            var options = new CompanionOptions { Enabled = true, Port = port };

            bool started = await server.StartAsync(options);
            Assert.IsFalse(started);
            Assert.AreEqual(CompanionServiceState.Failed, server.Status.State);
            Assert.IsFalse(string.IsNullOrWhiteSpace(server.Status.LastErrorMessage));
        }
        finally
        {
            listener.Stop();
        }

        // Verify another server can bind immediately to that port now
        await using var newServer = new CompanionServer();
        bool newStarted = await newServer.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(newStarted);
    }

    [TestMethod]
    public async Task RepeatedStopAndDispose_IsSafe()
    {
        int port = GetFreePort();
        var server = new CompanionServer();
        var options = new CompanionOptions { Enabled = true, Port = port };

        await server.StartAsync(options);

        await server.StopAsync();
        await server.StopAsync();
        server.Dispose();
        server.Dispose();
        await server.DisposeAsync();

        Assert.AreEqual(CompanionServiceState.Stopped, server.Status.State);
    }

    private static int GetFreePort()
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
            catch
            {
                if (i == 4) throw;
            }
        }
        throw new InvalidOperationException("Could not obtain a free port.");
    }
}
