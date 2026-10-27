using System.ComponentModel;
using FrameHub.App.Services;

namespace FrameHub.App.ViewModels;

public sealed class ActivityItemViewModel : INotifyPropertyChanged
{
    private string _message = string.Empty;
    public required string Time { get; init; }
    public required string Message { get => _message; init => _message = value; }
    public string Level { get; init; } = "Info";
    internal string? LocalizationKey { get; init; }
    internal object[] LocalizationArguments { get; init; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Relocalize(string language)
    {
        if (LocalizationKey is null) return;
        string value = string.Format(LocalizationService.GetUiCulture(language),
            LocalizationService.Translate(LocalizationKey, language), LocalizationArguments);
        if (_message == value) return;
        _message = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
    }
}
