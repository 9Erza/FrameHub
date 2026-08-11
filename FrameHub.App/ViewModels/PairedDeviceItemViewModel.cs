using System.Windows.Input;
using FrameHub.App.Helpers;
using FrameHub.Companion.Persistence;

namespace FrameHub.App.ViewModels;

public sealed class PairedDeviceItemViewModel : ViewModelBase
{
    public Guid Id { get; }
    public string DisplayName { get; }
    public string CreatedAtText { get; }
    public string LastUsedText { get; }
    public ICommand RevokeCommand { get; }

    public PairedDeviceItemViewModel(PairedDeviceRecord record, Action<Guid> onRevoke)
    {
        Id = record.Id;
        DisplayName = record.DisplayName;
        CreatedAtText = record.CreatedAtUtc.ToLocalTime().ToString("g");
        LastUsedText = record.LastUsedAtUtc.HasValue
            ? record.LastUsedAtUtc.Value.ToLocalTime().ToString("g")
            : "Never";

        RevokeCommand = new RelayCommand(_ => onRevoke(Id));
    }
}
