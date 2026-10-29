using FrameHub.App.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class ActivityLocalizationTests
{
    [TestMethod]
    public void LanguageChangeRelocalizesKeyedActivityAndPreservesArguments()
    {
        var item = new ActivityItemViewModel
        {
            Time = "10:00", Message = "Background profile monitor active for 2 enabled profiles: game-a, game-b.",
            LocalizationKey = "Activity.WatcherActive", LocalizationArguments = [2, "game-a, game-b"]
        };
        int notifications = 0;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(item.Message)) notifications++; };

        item.Relocalize("pl");

        Assert.AreEqual("Monitor profili w tle aktywny dla 2 włączonych profili: game-a, game-b.", item.Message);
        Assert.AreEqual(1, notifications);
        Assert.AreEqual("10:00", item.Time);
    }

    [TestMethod]
    public void LanguageChangeLeavesUnkeyedTechnicalActivityIntact()
    {
        var item = new ActivityItemViewModel { Time = "10:00", Message = "driver error: code 42" };
        item.Relocalize("pl");
        Assert.AreEqual("driver error: code 42", item.Message);
    }
    [TestMethod]
    public void SeverityDisplayRelocalizesWithoutChangingFilterIdentifier()
    {
        var item = new ActivityItemViewModel { Time = "10:00", Message = "test", Level = "Warn" };
        item.Relocalize("pl");
        Assert.AreEqual("Ostrzeżenie", item.LevelDisplay);
        Assert.AreEqual("Warn", item.Level);
        item.Relocalize("en");
        Assert.AreEqual("Warning", item.LevelDisplay);
        Assert.AreEqual(2, LogsViewModel.ClassifyLevel(item.Level));
    }
}
