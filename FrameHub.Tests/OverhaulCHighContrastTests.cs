using FrameHub.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// QA R1 P2: HighContrast must exist, not be "silently missing". Loads the real
/// compiled theme dictionary and asserts the shell-critical keys it overrides
/// are present and opaque (readable design-diff/AA baseline in HC mode).
/// </summary>
[TestClass]
public sealed class OverhaulCHighContrastTests
{
    [TestMethod]
    public void HighContrastDictionary_LoadsWithOpaqueShellKeys()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    var application = new FrameHub.App.App();
                    application.InitializeComponent();
                }

                var dictionary = new System.Windows.ResourceDictionary
                {
                    Source = new Uri("/FrameHub.App;component/Themes/HighContrast.xaml", UriKind.Relative)
                };

                foreach (string key in new[]
                {
                    "BrushBackground", "BrushSurface", "BrushTextPrimary",
                    "BrushBorderDefault", "BrushAccentPrimary", "BrushFocus"
                })
                {
                    Assert.IsTrue(dictionary.Contains(key), $"HighContrast must override {key}.");
                }

                var background = (System.Windows.Media.SolidColorBrush)dictionary["BrushBackground"]!;
                Assert.AreEqual(255, background.Color.A, "HC background must be opaque.");
                Assert.AreEqual(
                    System.Windows.Media.Color.FromRgb(0, 0, 0), background.Color,
                    "HC background must be black.");

                var text = (System.Windows.Media.SolidColorBrush)dictionary["BrushTextPrimary"]!;
                Assert.AreEqual(
                    System.Windows.Media.Color.FromRgb(255, 255, 255), text.Color,
                    "HC primary text must be white.");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "HC dictionary load timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    [TestMethod]
    public void ShouldApplyHighContrastTheme_MirrorsSystemSetting()
    {
        Assert.AreEqual(
            System.Windows.SystemParameters.HighContrast,
            FrameHub.App.App.ShouldApplyHighContrastTheme(),
            "Startup HC check must follow the OS setting, nothing else.");
    }
}
