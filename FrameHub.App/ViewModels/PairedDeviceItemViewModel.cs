using System.Windows.Input;
using FrameHub.App.Helpers;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Persistence;

namespace FrameHub.App.ViewModels;

public sealed class PairedDeviceItemViewModel : ViewModelBase
{
    private readonly Action<Guid, string, bool> _onToggleScope;
    private bool _readTelemetryEnabled;
    private bool _writeTelemetryEnabled;
    private bool _readBenchmarksEnabled;
    private bool _writeBenchmarksEnabled;
    private bool _readLibraryEnabled;
    private bool _writeLaunchEnabled;
    private bool _readBackgroundAppsEnabled;
    private bool _writeBackgroundAppsEnabled;
    private bool _readOptimizationEnabled;
    private bool _writeOptimizationEnabled;
    private bool _readOptimizationCpuEnabled;
    private bool _writeOptimizationCpuEnabled;

    public Guid Id { get; }
    public string DisplayName { get; }
    public string CreatedAtText { get; }
    public string LastUsedText { get; }
    public string PairedOnText { get; }
    public string LastUsedDisplay { get; }
    public string ScopeTelemetryLabel { get; }
    public string ScopeWriteTelemetryLabel { get; }
    public string ScopeReadBenchmarksLabel { get; }
    public string ScopeWriteBenchmarksLabel { get; }
    public string ScopeReadLibraryLabel { get; }
    public string ScopeWriteLaunchLabel { get; }
    public string ScopeReadBackgroundAppsLabel { get; }
    public string ScopeWriteBackgroundAppsLabel { get; }
    public string ScopeReadOptimizationLabel { get; }
    public string ScopeWriteOptimizationLabel { get; }
    public string ScopeReadOptimizationCpuLabel { get; }
    public string ScopeWriteOptimizationCpuLabel { get; }
    public string RevokeLabel { get; }
    public ICommand RevokeCommand { get; }

    public string PermissionsHeader { get; }
    public string AreaHeader { get; }
    public string ReadHeader { get; }
    public string ControlHeader { get; }
    public string AreaTelemetryLabel { get; }
    public string AreaLibraryLabel { get; }
    public string AreaBackgroundAppsLabel { get; }
    public string AreaOptimizationLabel { get; }
    public string AreaBenchmarksLabel { get; }
    public string AreaSessionCpuLabel { get; }

    public bool ReadTelemetryEnabled
    {
        get => _readTelemetryEnabled;
        set
        {
            if (SetProperty(ref _readTelemetryEnabled, value))
            {
                _onToggleScope(Id, CompanionScopes.ReadTelemetry, value);

                if (!value && _writeTelemetryEnabled)
                {
                    _writeTelemetryEnabled = false;
                    OnPropertyChanged(nameof(WriteTelemetryEnabled));
                    _onToggleScope(Id, CompanionScopes.WriteTelemetry, false);
                }
            }
        }
    }

    public bool WriteTelemetryEnabled
    {
        get => _writeTelemetryEnabled;
        set
        {
            if (SetProperty(ref _writeTelemetryEnabled, value))
            {
                _onToggleScope(Id, CompanionScopes.WriteTelemetry, value);

                if (value && !_readTelemetryEnabled)
                {
                    _readTelemetryEnabled = true;
                    OnPropertyChanged(nameof(ReadTelemetryEnabled));
                    _onToggleScope(Id, CompanionScopes.ReadTelemetry, true);
                }
            }
        }
    }

    public bool ReadBenchmarksEnabled
    {
        get => _readBenchmarksEnabled;
        set
        {
            if (SetProperty(ref _readBenchmarksEnabled, value))
            {
                _onToggleScope(Id, CompanionScopes.ReadBenchmarks, value);

                if (!value && _writeBenchmarksEnabled)
                {
                    _writeBenchmarksEnabled = false;
                    OnPropertyChanged(nameof(WriteBenchmarksEnabled));
                    _onToggleScope(Id, CompanionScopes.WriteBenchmarks, false);
                }
            }
        }
    }

    public bool WriteBenchmarksEnabled
    {
        get => _writeBenchmarksEnabled;
        set
        {
            if (SetProperty(ref _writeBenchmarksEnabled, value))
            {
                _onToggleScope(Id, CompanionScopes.WriteBenchmarks, value);

                if (value && !_readBenchmarksEnabled)
                {
                    _readBenchmarksEnabled = true;
                    OnPropertyChanged(nameof(ReadBenchmarksEnabled));
                    _onToggleScope(Id, CompanionScopes.ReadBenchmarks, true);
                }
            }
        }
    }

    public bool ReadLibraryEnabled
    {
        get => _readLibraryEnabled;
        set
        {
            if (SetProperty(ref _readLibraryEnabled, value))
            {
                _onToggleScope(Id, CompanionScopes.ReadLibrary, value);

                if (!value && _writeLaunchEnabled)
                {
                    _writeLaunchEnabled = false;
                    OnPropertyChanged(nameof(WriteLaunchEnabled));
                    _onToggleScope(Id, CompanionScopes.WriteLaunch, false);
                }
            }
        }
    }

    public bool WriteLaunchEnabled
    {
        get => _writeLaunchEnabled;
        set
        {
            if (SetProperty(ref _writeLaunchEnabled, value))
            {
                _onToggleScope(Id, CompanionScopes.WriteLaunch, value);

                if (value && !_readLibraryEnabled)
                {
                    _readLibraryEnabled = true;
                    OnPropertyChanged(nameof(ReadLibraryEnabled));
                    _onToggleScope(Id, CompanionScopes.ReadLibrary, true);
                }
            }
        }
    }

    public bool ReadBackgroundAppsEnabled
    {
        get => _readBackgroundAppsEnabled;
        set
        {
            if (SetProperty(ref _readBackgroundAppsEnabled, value))
            {
                _onToggleScope(Id, CompanionScopes.ReadBackgroundApps, value);
                if (!value && _writeBackgroundAppsEnabled)
                {
                    _writeBackgroundAppsEnabled = false;
                    OnPropertyChanged(nameof(WriteBackgroundAppsEnabled));
                    _onToggleScope(Id, CompanionScopes.WriteBackgroundApps, false);
                }
            }
        }
    }

    public bool WriteBackgroundAppsEnabled
    {
        get => _writeBackgroundAppsEnabled;
        set
        {
            if (SetProperty(ref _writeBackgroundAppsEnabled, value))
            {
                _onToggleScope(Id, CompanionScopes.WriteBackgroundApps, value);
                if (value && !_readBackgroundAppsEnabled)
                {
                    _readBackgroundAppsEnabled = true;
                    OnPropertyChanged(nameof(ReadBackgroundAppsEnabled));
                    _onToggleScope(Id, CompanionScopes.ReadBackgroundApps, true);
                }
            }
        }
    }

    public bool ReadOptimizationEnabled
    {
        get => _readOptimizationEnabled;
        set
        {
            if (SetProperty(ref _readOptimizationEnabled, value))
            {
                _onToggleScope(Id, CompanionScopes.ReadOptimization, value);

                if (!value && _writeOptimizationEnabled)
                {
                    _writeOptimizationEnabled = false;
                    OnPropertyChanged(nameof(WriteOptimizationEnabled));
                    _onToggleScope(Id, CompanionScopes.WriteOptimization, false);
                }
            }
        }
    }

    public bool WriteOptimizationEnabled
    {
        get => _writeOptimizationEnabled;
        set
        {
            if (SetProperty(ref _writeOptimizationEnabled, value))
            {
                _onToggleScope(Id, CompanionScopes.WriteOptimization, value);

                if (value && !_readOptimizationEnabled)
                {
                    _readOptimizationEnabled = true;
                    OnPropertyChanged(nameof(ReadOptimizationEnabled));
                    _onToggleScope(Id, CompanionScopes.ReadOptimization, true);
                }
            }
        }
    }

    public bool ReadOptimizationCpuEnabled
    {
        get => _readOptimizationCpuEnabled;
        set
        {
            if (SetProperty(ref _readOptimizationCpuEnabled, value))
            {
                _onToggleScope(Id, CompanionScopes.ReadOptimizationCpu, value);

                if (!value && _writeOptimizationCpuEnabled)
                {
                    _writeOptimizationCpuEnabled = false;
                    OnPropertyChanged(nameof(WriteOptimizationCpuEnabled));
                    _onToggleScope(Id, CompanionScopes.WriteOptimizationCpu, false);
                }
            }
        }
    }

    public bool WriteOptimizationCpuEnabled
    {
        get => _writeOptimizationCpuEnabled;
        set
        {
            if (SetProperty(ref _writeOptimizationCpuEnabled, value))
            {
                _onToggleScope(Id, CompanionScopes.WriteOptimizationCpu, value);

                if (value && !_readOptimizationCpuEnabled)
                {
                    _readOptimizationCpuEnabled = true;
                    OnPropertyChanged(nameof(ReadOptimizationCpuEnabled));
                    _onToggleScope(Id, CompanionScopes.ReadOptimizationCpu, true);
                }
            }
        }
    }

    public PairedDeviceItemViewModel(
        PairedDeviceRecord record,
        Action<Guid> onRevoke,
        Action<Guid, string, bool> onToggleScope,
        string? scopeTelemetryLabel = null,
        string? scopeReadBenchmarksLabel = null,
        string? scopeWriteBenchmarksLabel = null,
        string? scopeReadLibraryLabel = null,
        string? scopeWriteLaunchLabel = null,
        string? scopeReadOptimizationLabel = null,
        string? scopeWriteOptimizationLabel = null,
        string? revokeLabel = null,
        string? neverUsedText = null,
        string? scopeReadBackgroundAppsLabel = null,
        string? scopeWriteBackgroundAppsLabel = null,
        string? pairedLabel = null,
        string? lastUsedLabel = null,
        string? permissionsHeader = null,
        string? areaHeader = null,
        string? readHeader = null,
        string? controlHeader = null,
        string? areaTelemetryLabel = null,
        string? areaLibraryLabel = null,
        string? areaBackgroundAppsLabel = null,
        string? areaOptimizationLabel = null,
        string? areaBenchmarksLabel = null,
        string? scopeWriteTelemetryLabel = null,
        string? scopeReadOptimizationCpuLabel = null,
        string? scopeWriteOptimizationCpuLabel = null,
        string? areaSessionCpuLabel = null)
    {
        Id = record.Id;
        DisplayName = record.DisplayName;
        CreatedAtText = record.CreatedAtUtc.ToLocalTime().ToString("g");
        LastUsedText = record.LastUsedAtUtc.HasValue
            ? record.LastUsedAtUtc.Value.ToLocalTime().ToString("g")
            : (neverUsedText ?? "Never");
        PairedOnText = string.IsNullOrWhiteSpace(pairedLabel) ? CreatedAtText : $"{pairedLabel}: {CreatedAtText}";
        LastUsedDisplay = string.IsNullOrWhiteSpace(lastUsedLabel) ? LastUsedText : $"{lastUsedLabel}: {LastUsedText}";

        ScopeTelemetryLabel = scopeTelemetryLabel ?? "Telemetry";
        ScopeWriteTelemetryLabel = scopeWriteTelemetryLabel ?? "Telemetry Control";
        ScopeReadBenchmarksLabel = scopeReadBenchmarksLabel ?? "Benchmark Data";
        ScopeWriteBenchmarksLabel = scopeWriteBenchmarksLabel ?? "Benchmark Control";
        ScopeReadLibraryLabel = scopeReadLibraryLabel ?? "Read Library";
        ScopeWriteLaunchLabel = scopeWriteLaunchLabel ?? "Launch Control";
        ScopeReadBackgroundAppsLabel = scopeReadBackgroundAppsLabel ?? "Background Apps";
        ScopeWriteBackgroundAppsLabel = scopeWriteBackgroundAppsLabel ?? "Background App Control";
        ScopeReadOptimizationLabel = scopeReadOptimizationLabel ?? "Optimization Data";
        ScopeWriteOptimizationLabel = scopeWriteOptimizationLabel ?? "Optimization Control";
        ScopeReadOptimizationCpuLabel = scopeReadOptimizationCpuLabel ?? "Session CPU Data";
        ScopeWriteOptimizationCpuLabel = scopeWriteOptimizationCpuLabel ?? "Session CPU Control";
        RevokeLabel = revokeLabel ?? "Revoke";

        PermissionsHeader = permissionsHeader ?? "Permissions";
        AreaHeader = areaHeader ?? "Area";
        ReadHeader = readHeader ?? "Read";
        ControlHeader = controlHeader ?? "Control";
        AreaTelemetryLabel = areaTelemetryLabel ?? "Telemetry";
        AreaLibraryLabel = areaLibraryLabel ?? "Game library";
        AreaBackgroundAppsLabel = areaBackgroundAppsLabel ?? "Background apps";
        AreaOptimizationLabel = areaOptimizationLabel ?? "Optimization";
        AreaBenchmarksLabel = areaBenchmarksLabel ?? "Benchmarks";
        AreaSessionCpuLabel = areaSessionCpuLabel ?? "Session CPU";

        _onToggleScope = onToggleScope ?? ((_, _, _) => { });
        _readTelemetryEnabled = record.Scopes.Contains(CompanionScopes.ReadTelemetry, StringComparer.OrdinalIgnoreCase);
        _writeTelemetryEnabled = record.Scopes.Contains(CompanionScopes.WriteTelemetry, StringComparer.OrdinalIgnoreCase);
        _readBenchmarksEnabled = record.Scopes.Contains(CompanionScopes.ReadBenchmarks, StringComparer.OrdinalIgnoreCase);
        _writeBenchmarksEnabled = record.Scopes.Contains(CompanionScopes.WriteBenchmarks, StringComparer.OrdinalIgnoreCase);
        _readLibraryEnabled = record.Scopes.Contains(CompanionScopes.ReadLibrary, StringComparer.OrdinalIgnoreCase);
        _writeLaunchEnabled = record.Scopes.Contains(CompanionScopes.WriteLaunch, StringComparer.OrdinalIgnoreCase);
        _readBackgroundAppsEnabled = record.Scopes.Contains(CompanionScopes.ReadBackgroundApps, StringComparer.OrdinalIgnoreCase);
        _writeBackgroundAppsEnabled = record.Scopes.Contains(CompanionScopes.WriteBackgroundApps, StringComparer.OrdinalIgnoreCase);
        _readOptimizationEnabled = record.Scopes.Contains(CompanionScopes.ReadOptimization, StringComparer.OrdinalIgnoreCase);
        _writeOptimizationEnabled = record.Scopes.Contains(CompanionScopes.WriteOptimization, StringComparer.OrdinalIgnoreCase);
        _readOptimizationCpuEnabled = record.Scopes.Contains(CompanionScopes.ReadOptimizationCpu, StringComparer.OrdinalIgnoreCase);
        _writeOptimizationCpuEnabled = record.Scopes.Contains(CompanionScopes.WriteOptimizationCpu, StringComparer.OrdinalIgnoreCase);

        RevokeCommand = new RelayCommand(_ => onRevoke(Id));
    }
}
