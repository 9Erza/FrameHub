using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// Optional PresentMon installation contract for installer/FrameHub.iss:
/// the PresentMon task must exist, default to selected, be remembered across
/// upgrades, gate every msiexec path, leave existing installations alone and
/// never touch the shared Windows service configuration.
/// </summary>
[TestClass]
public sealed class InstallerPresentMonOptionTests
{
    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "version.txt"))) return dir;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        Assert.Fail("Repo root (version.txt) was not found above the test output directory.");
        throw new InvalidOperationException("unreachable");
    }

    private static string ReadInstallerScript()
    {
        string path = Path.Combine(FindRepoRoot(), "installer", "FrameHub.iss");
        Assert.IsTrue(File.Exists(path), "installer/FrameHub.iss must exist.");
        return File.ReadAllText(path);
    }

    private static string PresentMonTaskLine(string installer)
    {
        string? line = installer.Split('\n')
            .FirstOrDefault(candidate => candidate.Contains("Name: \"presentmon\""));
        Assert.IsNotNull(line, "The presentmon task must exist in [Tasks].");
        return line!;
    }

    [TestMethod]
    public void PresentMonTask_ExistsCheckedByDefaultWithRecommendedLabel()
    {
        string installer = ReadInstallerScript();
        string line = PresentMonTaskLine(installer);
        StringAssert.Contains(line, "Intel PresentMon 2.5.1", "Task must name the pinned version.");
        StringAssert.Contains(line, "monitoring FPS i benchmarki (zalecane)", "Task must carry the recommended label.");
        Assert.IsFalse(line.Contains("unchecked", StringComparison.OrdinalIgnoreCase),
            "The presentmon task must default to selected (no unchecked flag).");
    }

    [TestMethod]
    public void PresentMonTask_RemembersPreviousChoiceOnUpgrade()
    {
        string installer = ReadInstallerScript();
        Assert.IsFalse(installer.Contains("UsePreviousTasks=no", StringComparison.OrdinalIgnoreCase),
            "Inno default UsePreviousTasks=yes must stand so upgrades keep the user's earlier choice.");
    }

    [TestMethod]
    public void PrepareToInstall_SkipsEverythingWhenTaskDeselected()
    {
        string installer = ReadInstallerScript();
        int prepareToInstall = installer.IndexOf("function PrepareToInstall", StringComparison.Ordinal);
        Assert.IsTrue(prepareToInstall >= 0, "PrepareToInstall must exist.");
        int gate = installer.IndexOf("WizardIsTaskSelected('presentmon')", prepareToInstall, StringComparison.Ordinal);
        Assert.IsTrue(gate > prepareToInstall, "PrepareToInstall must consult the presentmon task.");
        int firstMsiexec = installer.IndexOf("{sys}\\msiexec.exe", prepareToInstall, StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(firstMsiexec > gate, "The task gate must precede every msiexec invocation.");
        int deselectLog = installer.IndexOf("PresentMon task deselected", gate, StringComparison.Ordinal);
        Assert.IsTrue(deselectLog > gate && deselectLog < firstMsiexec,
            "The deselected path must log and exit before any msiexec involvement.");
    }

    [TestMethod]
    public void MsiStaysEmbedded_AndExistingInstallationsKeepProtection()
    {
        string installer = ReadInstallerScript();
        StringAssert.Contains(installer, "PresentMonMsiPath", "The MSI must stay embedded in the single EXE.");
        StringAssert.Contains(installer, "Flags: dontcopy", "The MSI must stay a temp-extracted prerequisite.");
        StringAssert.Contains(installer, "HasNewerPresentMonInstalled", "Newer-installation protection must stay.");
        StringAssert.Contains(installer, "HasUnverifiedPresentMonInstalled", "Unverified-installation protection must stay.");
        StringAssert.Contains(installer, "will not downgrade or replace this shared component",
            "The shared-component protection message must stay.");
    }

    [TestMethod]
    public void Installer_NeverConfiguresTheSharedService()
    {
        string installer = ReadInstallerScript();
        foreach (string forbidden in new[]
            {
                "[Services]", "ChangeServiceConfig", "CreateService", "DeleteService",
                "StartService", "sc.exe", "net start", "StartMode"
            })
        {
            Assert.IsFalse(installer.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"Installer must not touch service configuration: {forbidden}");
        }
    }

    [TestMethod]
    public void ReadyMemo_StatesDeselectedBehaviorHonestly()
    {
        string installer = ReadInstallerScript();
        StringAssert.Contains(installer, "WizardIsTaskSelected('presentmon')",
            "The ready memo must reflect the task selection.");
        StringAssert.Contains(installer, "Setup will not install, repair or start it",
            "Deselected memo must not promise automatic installation.");
    }

    [TestMethod]
    public void ExistingTasks_AndIdentities_AreUnchanged()
    {
        string installer = ReadInstallerScript();
        StringAssert.Contains(installer, "Name: \"desktopicon\"");
        StringAssert.Contains(installer, "AppId={{A073F5F3-E7D0-45C7-A233-8A98B033385B}");
        StringAssert.Contains(installer, "DefaultDirName={autopf}\\FrameHub");
        StringAssert.Contains(installer, "OutputBaseFilename=FrameHub-Setup-{#MyAppVersion}");
    }
}
