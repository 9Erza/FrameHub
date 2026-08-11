using System.Windows.Input;
using FrameHub.App.Helpers;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Persistence;

namespace FrameHub.App.ViewModels;

public sealed class PairedDeviceItemViewModel : ViewModelBase
{
    private readonly Action<Guid, string, bool> _onToggleScope;
    private bool _readTelemetryEnabled;

    public Guid Id { get; }
    public string DisplayName { get; }
    public string CreatedAtText { get; }
    public string LastUsedText { get; }
    public string ScopeTelemetryLabel { get; }
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

    public PairedDeviceItemViewModel(
        PairedDeviceRecord record,
        Action<Guid> onRevoke,
        Action<Guid, string, bool> onToggleScope,
        string? scopeTelemetryLabel = null,
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
        RevokeLabel = revokeLabel ?? "Revoke";

        _onToggleScope = onToggleScope ?? ((_, _, _) => { });
        _readTelemetryEnabled = record.Scopes.Contains(CompanionScopes.ReadTelemetry, StringComparer.OrdinalIgnoreCase);

        RevokeCommand = new RelayCommand(_ => onRevoke(Id));
    }
}
