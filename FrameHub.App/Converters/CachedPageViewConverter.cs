using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using FrameHub.App.ViewModels;

namespace FrameHub.App.Converters;

/// <summary>
/// Keeps the view of the heaviest pages alive between visits. The shell's ContentControl
/// re-instantiated every page from its DataTemplate on each navigation, which re-parsed the
/// large Library, Session and Settings XAML each time (100–200 ms per switch). For those pages the
/// view is created once per view-model and reused; every other page keeps the template path, so
/// rarely visited pages cost no memory while hidden. Views are keyed weakly by their view-model,
/// so they are released together with it. Views attach and detach their view-model on
/// Loaded/Unloaded, which still fires on every visit.
/// </summary>
public sealed class CachedPageViewConverter : IValueConverter
{
    private readonly ConditionalWeakTable<object, FrameworkElement> _views = new();

    public static bool IsCached(object? viewModel) =>
        viewModel is LibraryViewModel or SessionOptimizationViewModel or SettingsViewModel;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || !IsCached(value)) return value;
        if (_views.TryGetValue(value, out FrameworkElement? cached)) return cached;
        if (System.Windows.Application.Current?.TryFindResource(new DataTemplateKey(value.GetType())) is not DataTemplate template) return value;
        if (template.LoadContent() is not FrameworkElement view) return value;

        view.DataContext = value;
        _views.Add(value, view);
        return view;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
