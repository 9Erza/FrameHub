using System;
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
}
