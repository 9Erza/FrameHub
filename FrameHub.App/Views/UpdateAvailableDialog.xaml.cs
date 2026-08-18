using FrameHub.App.Services;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using System.Windows;
using System.Windows.Input;

namespace FrameHub.App.Views;

public partial class UpdateAvailableDialog : Window
{
    private readonly string _releaseUrl;

    private UpdateAvailableDialog(string latestVersion, string releaseUrl, LocalizationService localization)
    {
        InitializeComponent();
        _releaseUrl = releaseUrl;
        Title = localization.T("Update.Dialog.Title");
        DialogTitleText.Text = localization.T("Update.Dialog.Title");
        DialogHeadingText.Text = localization.T("Update.Dialog.Heading");
        LatestVersionText.Text = $"v{latestVersion.TrimStart('v', 'V')}";
        CurrentVersionText.Text = string.Format(localization.T("Update.Dialog.CurrentVersion"), new AppInfo().Version);
        OpenReleaseButton.Content = localization.T("Update.Dialog.OpenRelease");
        LaterButton.Content = localization.T("Update.Dialog.Later");
    }

    /// <summary>
    /// Presents the single FrameHub-styled update dialog. Shared by the automatic startup check
    /// and the manual "Check now" flow so no duplicate modal implementation exists.
    /// </summary>
    public static void Present(Window? owner, string latestVersion, string releaseUrl, LocalizationService localization)
    {
        var dialog = new UpdateAvailableDialog(latestVersion, releaseUrl, localization)
        {
            Owner = owner ?? (System.Windows.Application.Current?.MainWindow is Window visible && visible.IsVisible ? visible : null)
        };
        dialog.ShowDialog();
    }

    private void OpenReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateService.OpenReleasePage(_releaseUrl);
        Close();
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DialogRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // DragMove can throw if the mouse state changes during window chrome processing.
            }
        }
    }
}
