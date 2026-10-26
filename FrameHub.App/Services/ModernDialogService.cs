using System;
using System.Linq;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfApplication = System.Windows.Application;
using WpfBorder = System.Windows.Controls.Border;
using WpfButton = System.Windows.Controls.Button;
using WpfColumnDefinition = System.Windows.Controls.ColumnDefinition;
using WpfControl = System.Windows.Controls.Control;
using WpfCornerRadius = System.Windows.CornerRadius;
using WpfCursor = System.Windows.Input.Cursors;
using WpfDropShadowEffect = System.Windows.Media.Effects.DropShadowEffect;
using WpfFontWeights = System.Windows.FontWeights;
using WpfGrid = System.Windows.Controls.Grid;
using WpfGridLength = System.Windows.GridLength;
using WpfGridUnitType = System.Windows.GridUnitType;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfResizeMode = System.Windows.ResizeMode;
using WpfSizeToContent = System.Windows.SizeToContent;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextAlignment = System.Windows.TextAlignment;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextWrapping = System.Windows.TextWrapping;
using WpfThickness = System.Windows.Thickness;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;
using WpfWindow = System.Windows.Window;
using WpfWindowStartupLocation = System.Windows.WindowStartupLocation;
using WpfWindowStyle = System.Windows.WindowStyle;

namespace FrameHub.App.Services;

public static class ModernDialogService
{
    public static void ShowInfo(WpfWindow? owner, string title, string message, string buttonText = "OK")
    {
        bool isWarning = message.StartsWith("[WARN]", StringComparison.OrdinalIgnoreCase);
        if (isWarning)
        {
            message = message[6..].TrimStart();
        }

        var accent = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushPrimary") ?? MediaBrushes.DeepSkyBlue);
        var primary = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushTextPrimary") ?? MediaBrushes.White);
        var secondary = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushTextSecondary") ?? MediaBrushes.LightSteelBlue);
        var warning = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushDanger") ?? new MediaSolidColorBrush(MediaColor.FromRgb(255, 85, 85)));
        var surface = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushSurface") ?? new MediaSolidColorBrush(MediaColor.FromRgb(14, 25, 45)));
        var border = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushBorder") ?? new MediaSolidColorBrush(MediaColor.FromRgb(45, 68, 112)));

        if (isWarning)
        {
            accent = warning;
        }

        var dialog = new WpfWindow
        {
            Title = title,
            Owner = owner,
            WindowStartupLocation = owner == null ? WpfWindowStartupLocation.CenterScreen : WpfWindowStartupLocation.CenterOwner,
            SizeToContent = WpfSizeToContent.WidthAndHeight,
            ResizeMode = WpfResizeMode.NoResize,
            WindowStyle = WpfWindowStyle.None,
            AllowsTransparency = true,
            Background = MediaBrushes.Transparent,
            ShowInTaskbar = false,
            FontFamily = new MediaFontFamily("Segoe UI"),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        var okButton = new WpfButton
        {
            Content = buttonText,
            MinWidth = 120,
            Height = 40,
            Padding = new WpfThickness(18, 0, 18, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Cursor = WpfCursor.Hand
        };
        okButton.SetResourceReference(WpfControl.StyleProperty, "PrimaryButton");
        okButton.Click += (_, _) => dialog.Close();

        var closeButton = new WpfButton
        {
            Content = "×",
            Width = 34,
            Height = 30,
            Padding = new WpfThickness(0),
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            VerticalAlignment = WpfVerticalAlignment.Top,
            Cursor = WpfCursor.Hand,
            Background = MediaBrushes.Transparent,
            BorderThickness = new WpfThickness(0),
            Foreground = secondary,
            FontSize = 18,
            FontWeight = WpfFontWeights.SemiBold
        };
        closeButton.Click += (_, _) => dialog.Close();

        var titleText = new WpfTextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = WpfFontWeights.SemiBold,
            Foreground = primary,
            TextWrapping = WpfTextWrapping.Wrap
        };

        var messageText = new WpfTextBlock
        {
            Text = message,
            FontSize = 13,
            LineHeight = 20,
            Foreground = isWarning ? warning : secondary,
            TextWrapping = WpfTextWrapping.Wrap,
            Margin = new WpfThickness(0, 10, 0, 0),
            MaxWidth = 560
        };

        var icon = new WpfBorder
        {
            Width = 46,
            Height = 46,
            CornerRadius = new WpfCornerRadius(8),
            Background = accent,
            Opacity = 0.95,
            Child = new WpfTextBlock
            {
                Text = isWarning ? "!" : "✓",
                Foreground = MediaBrushes.White,
                FontSize = 25,
                FontWeight = WpfFontWeights.Bold,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center,
                TextAlignment = WpfTextAlignment.Center
            }
        };

        var contentGrid = new WpfGrid();
        contentGrid.ColumnDefinitions.Add(new WpfColumnDefinition { Width = WpfGridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new WpfColumnDefinition { Width = new WpfGridLength(1, WpfGridUnitType.Star) });

        WpfGrid.SetColumn(icon, 0);
        contentGrid.Children.Add(icon);

        var contentStack = new WpfStackPanel { Margin = new WpfThickness(16, 0, 0, 0) };
        contentStack.Children.Add(titleText);
        contentStack.Children.Add(messageText);
        WpfGrid.SetColumn(contentStack, 1);
        contentGrid.Children.Add(contentStack);

        var rootStack = new WpfStackPanel();
        var headerGrid = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 18) };
        headerGrid.ColumnDefinitions.Add(new WpfColumnDefinition { Width = new WpfGridLength(1, WpfGridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new WpfColumnDefinition { Width = WpfGridLength.Auto });
        headerGrid.Children.Add(contentGrid);
        WpfGrid.SetColumn(closeButton, 1);
        headerGrid.Children.Add(closeButton);

        rootStack.Children.Add(headerGrid);
        rootStack.Children.Add(okButton);

        var rootBorder = new WpfBorder
        {
            Background = surface,
            BorderBrush = border,
            BorderThickness = new WpfThickness(1),
            CornerRadius = new WpfCornerRadius(8),
            Padding = new WpfThickness(24),
            Effect = new WpfDropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 0,
                Opacity = 0.45,
                Color = MediaColor.FromRgb(0, 0, 0)
            },
            Child = rootStack
        };

        dialog.Content = rootBorder;
        dialog.ShowDialog();
    }

    /// <summary>
    /// Modal destructive-action confirmation. Returns true only when the user
    /// explicitly clicks the confirm button; closing, Esc or Cancel returns false.
    /// </summary>
    public static bool ShowConfirm(WpfWindow? owner, string title, string message, string confirmText, string cancelText)
    {
        var primary = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushTextPrimary") ?? MediaBrushes.White);
        var secondary = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushTextSecondary") ?? MediaBrushes.LightSteelBlue);
        var danger = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushDanger") ?? new MediaSolidColorBrush(MediaColor.FromRgb(255, 85, 85)));
        var surface = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushSurface") ?? new MediaSolidColorBrush(MediaColor.FromRgb(14, 25, 45)));
        var border = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushBorder") ?? new MediaSolidColorBrush(MediaColor.FromRgb(45, 68, 112)));

        var dialog = new WpfWindow
        {
            Title = title,
            Owner = owner,
            WindowStartupLocation = owner == null ? WpfWindowStartupLocation.CenterScreen : WpfWindowStartupLocation.CenterOwner,
            SizeToContent = WpfSizeToContent.WidthAndHeight,
            ResizeMode = WpfResizeMode.NoResize,
            WindowStyle = WpfWindowStyle.None,
            AllowsTransparency = true,
            Background = MediaBrushes.Transparent,
            ShowInTaskbar = false,
            FontFamily = new MediaFontFamily("Segoe UI"),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        bool confirmed = false;

        var confirmButton = new WpfButton
        {
            Content = confirmText,
            MinWidth = 120,
            Height = 40,
            Padding = new WpfThickness(18, 0, 18, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Cursor = WpfCursor.Hand
        };
        confirmButton.SetResourceReference(WpfControl.StyleProperty, "DangerButton");
        confirmButton.Click += (_, _) => { confirmed = true; dialog.Close(); };

        var cancelButton = new WpfButton
        {
            Content = cancelText,
            MinWidth = 120,
            Height = 40,
            Padding = new WpfThickness(18, 0, 18, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Cursor = WpfCursor.Hand
        };
        cancelButton.SetResourceReference(WpfControl.StyleProperty, "SecondaryButton");
        cancelButton.Click += (_, _) => dialog.Close();

        var titleText = new WpfTextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = WpfFontWeights.SemiBold,
            Foreground = primary,
            TextWrapping = WpfTextWrapping.Wrap
        };

        var messageText = new WpfTextBlock
        {
            Text = message,
            FontSize = 13,
            LineHeight = 20,
            Foreground = secondary,
            TextWrapping = WpfTextWrapping.Wrap,
            Margin = new WpfThickness(0, 10, 0, 0),
            MaxWidth = 560
        };

        var icon = new WpfBorder
        {
            Width = 46,
            Height = 46,
            CornerRadius = new WpfCornerRadius(8),
            Background = danger,
            Opacity = 0.95,
            Child = new WpfTextBlock
            {
                Text = "!",
                Foreground = MediaBrushes.White,
                FontSize = 25,
                FontWeight = WpfFontWeights.Bold,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center,
                TextAlignment = WpfTextAlignment.Center
            }
        };

        var contentGrid = new WpfGrid();
        contentGrid.ColumnDefinitions.Add(new WpfColumnDefinition { Width = WpfGridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new WpfColumnDefinition { Width = new WpfGridLength(1, WpfGridUnitType.Star) });

        WpfGrid.SetColumn(icon, 0);
        contentGrid.Children.Add(icon);

        var contentStack = new WpfStackPanel { Margin = new WpfThickness(16, 0, 0, 0) };
        contentStack.Children.Add(titleText);
        contentStack.Children.Add(messageText);
        WpfGrid.SetColumn(contentStack, 1);
        contentGrid.Children.Add(contentStack);

        var buttonsPanel = new WpfStackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new WpfThickness(0, 18, 0, 0)
        };
        cancelButton.Margin = new WpfThickness(0, 0, 10, 0);
        buttonsPanel.Children.Add(cancelButton);
        buttonsPanel.Children.Add(confirmButton);

        var rootStack = new WpfStackPanel();
        rootStack.Children.Add(contentGrid);
        rootStack.Children.Add(buttonsPanel);

        dialog.Content = new WpfBorder
        {
            Background = surface,
            BorderBrush = border,
            BorderThickness = new WpfThickness(1),
            CornerRadius = new WpfCornerRadius(8),
            Padding = new WpfThickness(24),
            Child = rootStack
        };
        dialog.ShowDialog();
        return confirmed;
    }

    /// <summary>
    /// About / Support dialog: product identity (version, author, MIT) plus
    /// explicit support and third-party-notices actions. No gating, no pop-ups,
    /// no feature promises — the Supporter note (part of <paramref name="message"/>)
    /// only states that optional future additions fund development.
    /// </summary>
    public static void ShowAbout(
        WpfWindow? owner,
        string title,
        string message,
        string supporterNote,
        string supportText,
        string noticesText,
        Action onSupport,
        Action onNotices,
        string? authorText = null,
        string? websiteText = null,
        string? repositoryText = null,
        Action? onAuthor = null,
        Action? onWebsite = null,
        Action? onRepository = null)
    {
        var primary = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushTextPrimary") ?? MediaBrushes.White);
        var secondary = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushTextSecondary") ?? MediaBrushes.LightSteelBlue);
        var accent = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushPrimary") ?? MediaBrushes.DeepSkyBlue);
        var surface = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushSurface") ?? new MediaSolidColorBrush(MediaColor.FromRgb(14, 25, 45)));
        var border = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushBorder") ?? new MediaSolidColorBrush(MediaColor.FromRgb(45, 68, 112)));
        var supporter = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushSupporter") ?? new MediaSolidColorBrush(MediaColor.FromRgb(214, 182, 110)));
        var supporterSoft = (MediaBrush)(WpfApplication.Current.TryFindResource("BrushSupporterSoft") ?? new MediaSolidColorBrush(MediaColor.FromRgb(46, 36, 19)));

        var dialog = new WpfWindow
        {
            Title = title,
            Owner = owner,
            WindowStartupLocation = owner == null ? WpfWindowStartupLocation.CenterScreen : WpfWindowStartupLocation.CenterOwner,
            SizeToContent = WpfSizeToContent.WidthAndHeight,
            ResizeMode = WpfResizeMode.NoResize,
            WindowStyle = WpfWindowStyle.None,
            AllowsTransparency = true,
            Background = MediaBrushes.Transparent,
            ShowInTaskbar = false,
            FontFamily = new MediaFontFamily("Segoe UI"),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        var supportButton = new WpfButton
        {
            Content = supportText,
            MinWidth = 120,
            Height = 40,
            Padding = new WpfThickness(18, 0, 18, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Cursor = WpfCursor.Hand
        };
        supportButton.SetResourceReference(WpfControl.StyleProperty, "SupporterButton");
        supportButton.Click += (_, _) => { try { onSupport(); } catch { } dialog.Close(); };

        var noticesButton = new WpfButton
        {
            Content = noticesText,
            MinWidth = 120,
            Height = 40,
            Padding = new WpfThickness(18, 0, 18, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Cursor = WpfCursor.Hand
        };
        noticesButton.SetResourceReference(WpfControl.StyleProperty, "SecondaryButton");
        noticesButton.Click += (_, _) => { try { onNotices(); } catch { } dialog.Close(); };

        var titleText = new WpfTextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = WpfFontWeights.SemiBold,
            Foreground = primary,
            TextWrapping = WpfTextWrapping.Wrap
        };

        var messageText = new WpfTextBlock
        {
            Text = message,
            FontSize = 13,
            LineHeight = 20,
            Foreground = secondary,
            TextWrapping = WpfTextWrapping.Wrap,
            Margin = new WpfThickness(0, 10, 0, 0),
            MaxWidth = 560
        };

        var icon = new WpfBorder
        {
            Width = 46,
            Height = 46,
            CornerRadius = new WpfCornerRadius(8),
            Background = accent,
            Opacity = 0.95,
            Child = new WpfTextBlock
            {
                Text = "F",
                Foreground = MediaBrushes.White,
                FontSize = 25,
                FontWeight = WpfFontWeights.Bold,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center,
                TextAlignment = WpfTextAlignment.Center
            }
        };

        var contentGrid = new WpfGrid();
        contentGrid.ColumnDefinitions.Add(new WpfColumnDefinition { Width = WpfGridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new WpfColumnDefinition { Width = new WpfGridLength(1, WpfGridUnitType.Star) });

        WpfGrid.SetColumn(icon, 0);
        contentGrid.Children.Add(icon);

        var contentStack = new WpfStackPanel { Margin = new WpfThickness(16, 0, 0, 0) };
        contentStack.Children.Add(titleText);
        contentStack.Children.Add(messageText);
        WpfGrid.SetColumn(contentStack, 1);
        contentGrid.Children.Add(contentStack);

        var buttonsPanel = new WpfStackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new WpfThickness(0, 18, 0, 0)
        };
        noticesButton.Margin = new WpfThickness(0, 0, 10, 0);
        buttonsPanel.Children.Add(noticesButton);
        buttonsPanel.Children.Add(supportButton);

        var rootStack = new WpfStackPanel();
        rootStack.Children.Add(contentGrid);

        // Identity links: author / website / repository stay one click away,
        // reusing the existing shell external-link commands passed by the caller.
        var linkItems = new (string? Text, Action? Open)[]
        {
            (authorText, onAuthor),
            (websiteText, onWebsite),
            (repositoryText, onRepository),
        };
        bool hasLinks = linkItems.Any(item => !string.IsNullOrWhiteSpace(item.Text) && item.Open is not null);
        if (hasLinks)
        {
            var linkRow = new WpfStackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new WpfThickness(62, 10, 0, 0),
            };
            foreach ((string? text, Action? open) in linkItems)
            {
                if (string.IsNullOrWhiteSpace(text) || open is null) continue;
                var linkButton = new WpfButton
                {
                    Content = text,
                    MinWidth = 40,
                    Height = 32,
                    Padding = new WpfThickness(10, 0, 10, 0),
                    Margin = new WpfThickness(0, 0, 8, 0),
                    Cursor = WpfCursor.Hand,
                };
                linkButton.SetResourceReference(WpfControl.StyleProperty, "FooterLinkButton");
                linkButton.Click += (_, _) => { try { open(); } catch { } dialog.Close(); };
                linkRow.Children.Add(linkButton);
            }
            rootStack.Children.Add(linkRow);
        }

        // Supporter chapter: one quiet gold panel with the exact SupporterNote wording.
        // No paywall, no goal counters, no premium benefits — the app stays complete and free.
        var supporterChapter = new WpfBorder
        {
            Background = supporterSoft,
            BorderBrush = supporter,
            BorderThickness = new WpfThickness(1),
            CornerRadius = new WpfCornerRadius(8),
            Padding = new WpfThickness(14, 12, 14, 12),
            Margin = new WpfThickness(0, 18, 0, 0)
        };
        var supporterRow = new WpfStackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        supporterRow.Children.Add(new WpfBorder
        {
            Width = 3,
            CornerRadius = new WpfCornerRadius(2),
            Background = supporter,
            Margin = new WpfThickness(0, 2, 10, 2)
        });
        supporterRow.Children.Add(new WpfTextBlock
        {
            Text = supporterNote,
            FontSize = 13,
            LineHeight = 20,
            Foreground = primary,
            TextWrapping = WpfTextWrapping.Wrap,
            MaxWidth = 520
        });
        supporterChapter.Child = supporterRow;
        rootStack.Children.Add(supporterChapter);

        rootStack.Children.Add(buttonsPanel);

        dialog.Content = new WpfBorder
        {
            Background = surface,
            BorderBrush = border,
            BorderThickness = new WpfThickness(1),
            CornerRadius = new WpfCornerRadius(8),
            Padding = new WpfThickness(24),
            Child = rootStack
        };
        dialog.ShowDialog();
    }
}
