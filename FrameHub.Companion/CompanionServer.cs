using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Handlers;
using FrameHub.Companion.Network;
using FrameHub.Companion.Pairing;
using FrameHub.Companion.Persistence;
using FrameHub.Companion.Providers;
using FrameHub.Companion.RateLimiting;
using FrameHub.Core.Logging;
using FrameHub.Core.Services;

namespace FrameHub.Companion;

public sealed class CompanionServer : IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _statusLock = new();
    private WebApplication? _app;
    private CompanionStatusInfo _status = new();
    private bool _disposed;
    private ITelemetrySnapshotProvider? _snapshotProvider;
    private ICompanionBenchmarkProvider? _benchmarkProvider;
    private ICompanionPresentationPreferencesProvider? _preferencesProvider;
    private ICompanionLibraryProvider? _libraryProvider;
    private ICompanionSessionOptimizationProvider? _sessionOptimizationProvider;
    private Func<IHardwareMonitorLease>? _hardwareLeaser;

    public DeviceRecordStore DeviceStore { get; }
    public PairingEngine PairingEngine { get; }
    public PairingRateLimiter RateLimiter { get; }
    public WebSocketTicketStore TicketStore { get; }

    public CompanionStatusInfo Status
    {
        get
        {
            lock (_statusLock)
            {
                return _status;
            }
        }
        private set
        {
            lock (_statusLock)
            {
                _status = value;
            }

            var handlers = StatusChanged;
            if (handlers == null) return;
            foreach (EventHandler<CompanionStatusInfo> handler in handlers.GetInvocationList())
            {
                try { handler(this, value); }
                catch (Exception ex) { LoggerService.Instance.Warn($"Companion status subscriber failed: {ex.Message}"); }
            }
        }
    }

    public event EventHandler<CompanionStatusInfo>? StatusChanged;

    public CompanionServer(DeviceRecordStore? deviceStore = null, Func<DateTimeOffset>? clock = null)
    {
        DeviceStore = deviceStore ?? new DeviceRecordStore();
        PairingEngine = new PairingEngine(DeviceStore, clock);
        RateLimiter = new PairingRateLimiter(5, TimeSpan.FromMinutes(1), clock);
        TicketStore = new WebSocketTicketStore(clock);
    }

    public void ConfigureTelemetryProvider(ITelemetrySnapshotProvider provider, Func<IHardwareMonitorLease>? hardwareLeaser = null)
    {
        _snapshotProvider = provider;
        _hardwareLeaser = hardwareLeaser;
    }

    public void ConfigureBenchmarkProvider(ICompanionBenchmarkProvider provider)
    {
        _benchmarkProvider = provider;
    }

    public void ConfigurePresentationPreferencesProvider(ICompanionPresentationPreferencesProvider provider)
    {
        _preferencesProvider = provider;
    }

    public void ConfigureLibraryProvider(ICompanionLibraryProvider provider)
    {
        _libraryProvider = provider;
    }

    public void ConfigureSessionOptimizationProvider(ICompanionSessionOptimizationProvider provider)
    {
        _sessionOptimizationProvider = provider;
    }


    public async Task<bool> StartAsync(CompanionOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!options.Enabled)
            {
                await StopInternalAsync().ConfigureAwait(false);
                return false;
            }

            if (IsRunningConfigEquivalent(options))
            {
                return true;
            }

            await StopInternalAsync().ConfigureAwait(false);

            Status = new CompanionStatusInfo
            {
                State = CompanionServiceState.Starting,
                Port = options.Port,
                LanEnabled = options.LanEnabled
            };

            WebApplication? app = null;
            bool lanValid = false;
            IPAddress? lanIpAddress = null;
            string? lanError = null;

            if (options.LanEnabled)
            {
                if (string.IsNullOrWhiteSpace(options.LanAddress) || !LanAddressService.IsValidLanAddress(options.LanAddress))
                {
                    lanError = "Selected LAN IPv4 address is invalid or unavailable on active network interfaces.";
                    LoggerService.Instance.Warn($"LAN Companion binding rejected: address '{options.LanAddress}' is invalid or unavailable.");
                }
                else if (IPAddress.TryParse(options.LanAddress.Trim(), out var parsedLan))
                {
                    lanValid = true;
                    lanIpAddress = parsedLan;
                }
            }

            try
            {
                string baseDir = AppContext.BaseDirectory;
                string webRootPath = Path.Combine(baseDir, "wwwroot");

                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    Args = Array.Empty<string>(),
                    ContentRootPath = baseDir,
                    WebRootPath = Directory.Exists(webRootPath) ? webRootPath : baseDir
                });

                builder.Logging.ClearProviders();

                builder.Services.AddSingleton(options);
                builder.Services.AddSingleton(DeviceStore);
                builder.Services.AddSingleton(PairingEngine);
                builder.Services.AddSingleton(RateLimiter);
                builder.Services.AddSingleton(TicketStore);

                var snapshotProvider = _snapshotProvider ?? new NullTelemetrySnapshotProvider();
                builder.Services.AddSingleton(snapshotProvider);

                if (_benchmarkProvider != null)
                {
                    builder.Services.AddSingleton(_benchmarkProvider);
                }

                var preferencesProvider = _preferencesProvider ?? new NullCompanionPresentationPreferencesProvider();
                builder.Services.AddSingleton<ICompanionPresentationPreferencesProvider>(preferencesProvider);

                var libraryProvider = _libraryProvider ?? new NullCompanionLibraryProvider();
                builder.Services.AddSingleton<ICompanionLibraryProvider>(libraryProvider);

                var optimizationProvider = _sessionOptimizationProvider ?? new NullCompanionSessionOptimizationProvider();
                builder.Services.AddSingleton<ICompanionSessionOptimizationProvider>(optimizationProvider);


                builder.WebHost.UseKestrel(kestrel =>
                {
                    // Strict localhost loopback binding (127.0.0.1) - NEVER 0.0.0.0 or ListenAnyIP
                    kestrel.Listen(IPAddress.Parse("127.0.0.1"), options.Port);

                    if (lanValid && lanIpAddress != null)
                    {
                        // Explicit single LAN IPv4 binding ONLY
                        kestrel.Listen(lanIpAddress, options.Port);
                    }
                });

                builder.Services.AddControllers()
                    .AddApplicationPart(typeof(CompanionServer).Assembly);

                app = builder.Build();

                app.UseWebSockets();

                app.Use(async (context, next) =>
                {
                    if (context.Request.Path.Equals("/api/v1/telemetry/ws", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!CompanionAuthMiddleware.IsHostAllowed(context.Request.Host.Value ?? string.Empty, options))
                        {
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await context.Response.WriteAsync("Invalid Host header.");
                            return;
                        }

                        await TelemetryWebSocketHandler.HandleWebSocketRequestAsync(
                            context,
                            TicketStore,
                            DeviceStore,
                            snapshotProvider,
                            _hardwareLeaser);
                        return;
                    }
                    await next();
                });

                app.UseDefaultFiles();
                app.UseStaticFiles();

                app.UseMiddleware<CompanionAuthMiddleware>();
                app.MapControllers();

                await app.StartAsync(cancellationToken).ConfigureAwait(false);

                _app = app;

                string localUrl = $"http://127.0.0.1:{options.Port}";
                string? lanUrl = lanValid && lanIpAddress != null ? $"http://{lanIpAddress}:{options.Port}" : null;

                Status = new CompanionStatusInfo
                {
                    State = CompanionServiceState.Running,
                    BoundAddress = localUrl,
                    LanBoundAddress = lanUrl,
                    LanEnabled = options.LanEnabled,
                    LanFaulted = options.LanEnabled && !lanValid,
                    LanErrorMessage = lanError,
                    Port = options.Port,
                    LastErrorMessage = null
                };

                LoggerService.Instance.Info($"FrameHub Companion server started on {localUrl}" + (lanUrl != null ? $" and LAN {lanUrl}" : ""));
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to start FrameHub Companion on port {options.Port}: {ex.Message}");

                if (app != null)
                {
                    try { await app.StopAsync().ConfigureAwait(false); } catch { }
                    try { await app.DisposeAsync().ConfigureAwait(false); } catch { }
                }

                _app = null;

                Status = new CompanionStatusInfo
                {
                    State = CompanionServiceState.Failed,
                    BoundAddress = null,
                    LanBoundAddress = null,
                    LanEnabled = options.LanEnabled,
                    LanFaulted = options.LanEnabled,
                    LanErrorMessage = ex.Message,
                    Port = options.Port,
                    LastErrorMessage = ex.Message
                };

                return false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopInternalAsync()
    {
        WebApplication? app = _app;
        _app = null;

        PairingEngine.CancelPairingSession();
        TicketStore.Clear();

        if (app != null)
        {
            try { await app.StopAsync().ConfigureAwait(false); } catch { }
            try { await app.DisposeAsync().ConfigureAwait(false); } catch { }
        }

        Status = new CompanionStatusInfo
        {
            State = CompanionServiceState.Stopped,
            BoundAddress = null,
            LanBoundAddress = null,
            LanEnabled = _status.LanEnabled,
            LanFaulted = false,
            LanErrorMessage = null,
            LastErrorMessage = null
        };
    }

    private bool IsRunningConfigEquivalent(CompanionOptions options)
    {
        if (_status.State != CompanionServiceState.Running || _app == null)
        {
            return false;
        }

        if (_status.Port != options.Port || _status.LanEnabled != options.LanEnabled)
        {
            return false;
        }

        if (options.LanEnabled)
        {
            if (_status.LanFaulted)
            {
                return false;
            }

            string? expectedLanUrl = !string.IsNullOrWhiteSpace(options.LanAddress)
                ? $"http://{options.LanAddress.Trim()}:{options.Port}"
                : null;

            if (!string.Equals(_status.LanBoundAddress, expectedLanUrl, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await StopInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        Task.Run(async () => await DisposeAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
    }
}
