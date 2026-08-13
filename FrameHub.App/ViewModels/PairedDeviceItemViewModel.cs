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

    public Guid Id { get; }
    public string DisplayName { get; }
    public string CreatedAtText { get; }
    public string LastUsedText { get; }
    public string ScopeTelemetryLabel { get; }
    public string ScopeReadBenchmarksLabel { get; }
    public string ScopeWriteBenchmarksLabel { get; }
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

    public PairedDeviceItemViewModel(
        PairedDeviceRecord record,
        Action<Guid> onRevoke,
        Action<Guid, string, bool> onToggleScope,
        string? scopeTelemetryLabel = null,
        string? scopeReadBenchmarksLabel = null,
        string? scopeWriteBenchmarksLabel = null,
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
        RevokeLabel = revokeLabel ?? "Revoke";

        _onToggleScope = onToggleScope ?? ((_, _, _) => { });
        _readTelemetryEnabled = record.Scopes.Contains(CompanionScopes.ReadTelemetry, StringComparer.OrdinalIgnoreCase);
        _readBenchmarksEnabled = record.Scopes.Contains(CompanionScopes.ReadBenchmarks, StringComparer.OrdinalIgnoreCase);
        _writeBenchmarksEnabled = record.Scopes.Contains(CompanionScopes.WriteBenchmarks, StringComparer.OrdinalIgnoreCase);

        RevokeCommand = new RelayCommand(_ => onRevoke(Id));
    }
}
