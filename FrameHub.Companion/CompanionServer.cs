using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FrameHub.Core.Logging;

namespace FrameHub.Companion;

public sealed class CompanionServer : IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _statusLock = new();
    private WebApplication? _app;
    private CompanionStatusInfo _status = new();
    private bool _disposed;

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
            StatusChanged?.Invoke(this, _status);
        }
    }

    public event EventHandler<CompanionStatusInfo>? StatusChanged;

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

            if (_status.State == CompanionServiceState.Running && _app != null && _status.Port == options.Port)
            {
                return true;
            }

            await StopInternalAsync().ConfigureAwait(false);

            Status = new CompanionStatusInfo
            {
                State = CompanionServiceState.Starting,
                Port = options.Port
            };

            WebApplication? app = null;
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    Args = Array.Empty<string>()
                });

                builder.Logging.ClearProviders();

                builder.WebHost.UseKestrel(kestrel =>
                {
                    // Strict localhost-only loopback binding (127.0.0.1)
                    kestrel.Listen(System.Net.IPAddress.Parse("127.0.0.1"), options.Port);
                });

                builder.Services.AddControllers()
                    .AddApplicationPart(typeof(CompanionServer).Assembly);

                app = builder.Build();
                app.MapControllers();

                await app.StartAsync(cancellationToken).ConfigureAwait(false);

                _app = app;

                string url = $"http://127.0.0.1:{options.Port}";
                Status = new CompanionStatusInfo
                {
                    State = CompanionServiceState.Running,
                    BoundAddress = url,
                    Port = options.Port,
                    LastErrorMessage = null
                };

                LoggerService.Instance.Info($"FrameHub Companion server started on {url}");
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to start FrameHub Companion on port {options.Port}: {ex.Message}");

                if (app != null)
                {
                    try
                    {
                        await app.StopAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore stop errors during failure cleanup
                    }

                    try
                    {
                        await app.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore disposal errors during failure cleanup
                    }
                }

                _app = null;

                Status = new CompanionStatusInfo
                {
                    State = CompanionServiceState.Failed,
                    BoundAddress = null,
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

        if (app != null)
        {
            try
            {
                await app.StopAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore stop errors during cleanup
            }

            try
            {
                await app.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore disposal errors during cleanup
            }
        }

        Status = new CompanionStatusInfo
        {
            State = CompanionServiceState.Stopped,
            BoundAddress = null,
            Port = _status.Port,
            LastErrorMessage = null
        };
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
