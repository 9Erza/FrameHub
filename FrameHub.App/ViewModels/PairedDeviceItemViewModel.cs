using System.Windows.Input;
using FrameHub.App.Helpers;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Persistence;

namespace FrameHub.App.ViewModels;

public sealed class PairedDeviceItemViewModel : ViewModelBase
{
    private readonly Action<Guid, string, bool> _onToggleScope;
    private bool _readTelemetryEnabled;
    private bool _readBenchmarksEnabled;
    private bool _writeBenchmarksEnabled;
    private bool _readLibraryEnabled;
    private bool _writeLaunchEnabled;
    private bool _readOptimizationEnabled;
    private bool _writeOptimizationEnabled;

    public Guid Id { get; }
    public string DisplayName { get; }
    public string CreatedAtText { get; }
    public string LastUsedText { get; }
    public string ScopeTelemetryLabel { get; }
    public string ScopeReadBenchmarksLabel { get; }
    public string ScopeWriteBenchmarksLabel { get; }
    public string ScopeReadLibraryLabel { get; }
    public string ScopeWriteLaunchLabel { get; }
    public string ScopeReadOptimizationLabel { get; }
    public string ScopeWriteOptimizationLabel { get; }
    public string RevokeLabel { get; }
    public ICommand RevokeCommand { get; }

    public bool ReadTelemetryEnabled
    {
        get => _readTelemetryEnabled;
        set
        {
            if (SetProperty(ref _readTelemetryEnabled, value))
            {
                _onToggleScope(Id, CompanionScopes.ReadTelemetry, value);
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
        string? neverUsedText = null)
    {
        Id = record.Id;
        DisplayName = record.DisplayName;
        CreatedAtText = record.CreatedAtUtc.ToLocalTime().ToString("g");
        LastUsedText = record.LastUsedAtUtc.HasValue
            ? record.LastUsedAtUtc.Value.ToLocalTime().ToString("g")
            : (neverUsedText ?? "Never");

        ScopeTelemetryLabel = scopeTelemetryLabel ?? "Telemetry";
        ScopeReadBenchmarksLabel = scopeReadBenchmarksLabel ?? "Benchmark Data";
        ScopeWriteBenchmarksLabel = scopeWriteBenchmarksLabel ?? "Benchmark Control";
        ScopeReadLibraryLabel = scopeReadLibraryLabel ?? "Read Library";
        ScopeWriteLaunchLabel = scopeWriteLaunchLabel ?? "Launch Control";
        ScopeReadOptimizationLabel = scopeReadOptimizationLabel ?? "Optimization Data";
        ScopeWriteOptimizationLabel = scopeWriteOptimizationLabel ?? "Optimization Control";
        RevokeLabel = revokeLabel ?? "Revoke";

        _onToggleScope = onToggleScope ?? ((_, _, _) => { });
        _readTelemetryEnabled = record.Scopes.Contains(CompanionScopes.ReadTelemetry, StringComparer.OrdinalIgnoreCase);
        _readBenchmarksEnabled = record.Scopes.Contains(CompanionScopes.ReadBenchmarks, StringComparer.OrdinalIgnoreCase);
        _writeBenchmarksEnabled = record.Scopes.Contains(CompanionScopes.WriteBenchmarks, StringComparer.OrdinalIgnoreCase);
        _readLibraryEnabled = record.Scopes.Contains(CompanionScopes.ReadLibrary, StringComparer.OrdinalIgnoreCase);
        _writeLaunchEnabled = record.Scopes.Contains(CompanionScopes.WriteLaunch, StringComparer.OrdinalIgnoreCase);
        _readOptimizationEnabled = record.Scopes.Contains(CompanionScopes.ReadOptimization, StringComparer.OrdinalIgnoreCase);
        _writeOptimizationEnabled = record.Scopes.Contains(CompanionScopes.WriteOptimization, StringComparer.OrdinalIgnoreCase);

        RevokeCommand = new RelayCommand(_ => onRevoke(Id));
    }
}
