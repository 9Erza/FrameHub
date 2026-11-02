using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Companion;
using FrameHub.Core.Models;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Sockets;

namespace FrameHub.Tests;

/// <summary>
/// First-run onboarding, optional-module gating, settings reset and full data
/// wipe (perf + first-run phase 2026-11). All file tests run against isolated
/// FRAMEHUB_DATA_DIR / FRAMEHUB_LOCAL_DATA_DIR temp roots with synthetic data;
/// destructive paths are never executed against real user data.
/// </summary>
[TestClass]
public sealed class FirstRunAndResetTests
{
    private string _tempDirectory = null!;
    private string _tempLocalDirectory = null!;
    private string? _previousDataDir;
    private string? _previousLocalDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.FirstRun", Guid.NewGuid().ToString("N"));
        _tempLocalDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.FirstRunLocal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        Directory.CreateDirectory(_tempLocalDirectory);
        _previousDataDir = Environment.GetEnvironmentVariable("FRAMEHUB_DATA_DIR");
        _previousLocalDir = Environment.GetEnvironmentVariable("FRAMEHUB_LOCAL_DATA_DIR");
        Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", _tempDirectory);
        Environment.SetEnvironmentVariable("FRAMEHUB_LOCAL_DATA_DIR", _tempLocalDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", _previousDataDir);
        Environment.SetEnvironmentVariable("FRAMEHUB_LOCAL_DATA_DIR", _previousLocalDir);
        foreach (string dir in new[] { _tempDirectory, _tempLocalDirectory })
        {
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }

    // ---------- Onboarding state machine ----------

    [TestMethod]
    public void FreshDataDirectory_ShowsWizardAndStampsInProgress()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            Assert.IsTrue(runtime.IsFirstRun, "Missing settings file must mean a fresh install with the wizard.");
            Assert.AreEqual(0, runtime.Settings.OnboardingCompletedVersion);
            Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "settings.json")),
                "Fresh detection must persist the in-progress marker so an interrupted run resumes.");
        });
    }

    [TestMethod]
    public void LegacySettingsFile_UpgradesSilentlyPreservingSettings()
    {
        RunOnSta(() =>
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "settings.json"), """{"Language":"pl"}""");
            using var runtime = CreateRuntime();
            Assert.IsFalse(runtime.IsFirstRun, "Pre-existing settings without the marker are an upgrade, not a first run.");
            Assert.AreEqual("pl", runtime.Settings.Language, "Upgrade must preserve existing settings.");
            Assert.AreEqual(1, runtime.Settings.OnboardingCompletedVersion);
        });
    }

    [TestMethod]
    public void InterruptedOnboarding_ResumesWizard()
    {
        RunOnSta(() =>
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "settings.json"),
                """{"Language":"en","OnboardingCompletedVersion":0}""");
            using var runtime = CreateRuntime();
            Assert.IsTrue(runtime.IsFirstRun, "Marker 0 means an interrupted first run and must resume the wizard.");
        });
    }

    [TestMethod]
    public void CompletedOnboarding_NeverShowsWizard()
    {
        RunOnSta(() =>
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "settings.json"),
                """{"Language":"pl","OnboardingCompletedVersion":1}""");
            using var runtime = CreateRuntime();
            Assert.IsFalse(runtime.IsFirstRun);
            Assert.AreEqual("pl", runtime.Settings.Language);
        });
    }

    [TestMethod]
    public void CompleteOnboarding_PersistsMarkerAndClearsFirstRun()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            Assert.IsTrue(runtime.IsFirstRun);
            runtime.CompleteOnboarding();
            Assert.IsFalse(runtime.IsFirstRun);
            var reloaded = new SettingsService(Path.Combine(_tempDirectory, "settings.json")).LoadSettings();
            Assert.AreEqual(1, reloaded.OnboardingCompletedVersion);
        });
    }

    // ---------- Wizard draft application ----------

    [TestMethod]
    public async Task WizardFinish_AppliesDraftChoicesAndCompletes()
    {
        await RunOnStaAsync(async () =>
        {
            AssumeNoStartupResidue();
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var wizard = new FirstRunSetupViewModel(localization, runtime);
            wizard.HardwareMonitorEnabled = true;
            wizard.CompanionEnabled = false;
            await wizard.FinishAsync();
            Assert.IsTrue(runtime.Settings.HardwareMonitorEnabled);
            Assert.IsFalse(runtime.Settings.CompanionEnabled);
            Assert.IsFalse(runtime.IsFirstRun);
            Assert.AreEqual(1, new SettingsService(Path.Combine(_tempDirectory, "settings.json"))
                .LoadSettings().OnboardingCompletedVersion);
        });
    }

    [TestMethod]
    public async Task WizardSkip_ForcesLightDefaultsButKeepsLanguage()
    {
        await RunOnStaAsync(async () =>
        {
            AssumeNoStartupResidue();
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var wizard = new FirstRunSetupViewModel(localization, runtime);
            wizard.IsPolish = true;
            wizard.HardwareMonitorEnabled = true;
            await wizard.SkipAsync();
            Assert.AreEqual("pl", runtime.Settings.Language, "Skip must preserve the chosen language.");
            Assert.IsFalse(runtime.Settings.HardwareMonitorEnabled);
            Assert.IsFalse(runtime.Settings.LiveTelemetryEnabled);
            Assert.IsFalse(runtime.Settings.CompanionEnabled);
            Assert.IsFalse(runtime.Settings.StartWithWindows);
            Assert.IsFalse(runtime.IsFirstRun);
        });
    }

    [TestMethod]
    public void WizardSteps_NavigateForwardAndBack()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var wizard = new FirstRunSetupViewModel(localization, runtime);
            Assert.AreEqual(0, wizard.Step);
            Assert.IsTrue(wizard.IsWelcomeVisible);
            wizard.Step = 5;
            Assert.IsTrue(wizard.IsSummaryVisible);
            Assert.IsTrue(wizard.IsLastStep);
            Assert.IsTrue(wizard.StepIndicator.Contains("6", StringComparison.Ordinal));
            wizard.Step = 99;
            Assert.AreEqual(5, wizard.Step, "Step must clamp to the last page.");
            wizard.Step = -1;
            Assert.AreEqual(0, wizard.Step, "Step must clamp to the first page.");
        });
    }

    // ---------- Live FPS gating ----------

    [TestMethod]
    public async Task CompanionOn_LiveOff_DoesNotStartPresentMonLoop()
    {
        await RunOnStaAsync(async () =>
        {
            using var runtime = CreateRuntime(port: 47981, companion: true, live: false);
            try
            {
                await runtime.SyncCompanionServerStateAsync();
            }
            catch (SocketException ex)
            {
                Assert.Inconclusive($"Loopback sockets unavailable in this environment: {ex.Message}");
            }

            if (runtime.CompanionServer.Status.State != CompanionServiceState.Running)
            {
                Assert.Inconclusive("Companion server did not start; loop gating cannot be observed.");
            }

            Assert.IsFalse(runtime.IsLiveTelemetryRunningForTesting,
                "With LiveTelemetryEnabled=false the native PresentMon loop must stay down.");
            Assert.IsNotNull(runtime.ActiveGameMonitor,
                "ActiveGame discovery must stay up for Session/phone use regardless of the live switch.");
        });
    }

    [TestMethod]
    public async Task CompanionOn_LiveOn_StartsPresentMonLoop()
    {
        await RunOnStaAsync(async () =>
        {
            using var runtime = CreateRuntime(port: 47982, companion: true, live: true);
            try
            {
                await runtime.SyncCompanionServerStateAsync();
            }
            catch (SocketException ex)
            {
                Assert.Inconclusive($"Loopback sockets unavailable in this environment: {ex.Message}");
            }

            if (runtime.CompanionServer.Status.State != CompanionServiceState.Running)
            {
                Assert.Inconclusive("Companion server did not start; loop gating cannot be observed.");
            }

            Assert.IsTrue(runtime.IsLiveTelemetryRunningForTesting,
                "With Companion+Live enabled the telemetry loop must run.");
        });
    }

    // ---------- Settings reset ----------

    [TestMethod]
    public async Task ResetSettings_RestoresDefaultsButKeepsUserData()
    {
        await RunOnStaAsync(async () =>
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "library.json"), """[{"Id":"g1"}]""");
            File.WriteAllText(Path.Combine(_tempDirectory, "profiles.json"), """[]""");
            File.WriteAllText(Path.Combine(_tempDirectory, "settings.json"),
                """{"Language":"pl","CompanionEnabled":true,"OnboardingCompletedVersion":1}""");
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new SettingsViewModel(localization, runtime);
            await vm.ConfirmResetSettingsAsync();
            Assert.AreEqual("en", runtime.Settings.Language);
            Assert.IsFalse(runtime.Settings.CompanionEnabled);
            Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "library.json")),
                "Settings reset must not delete the library.");
            Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "profiles.json")),
                "Settings reset must not delete profiles.");
            Assert.IsTrue(vm.DataResetStatusMessage.Contains(
                localization.T("Settings.DataReset.SettingsRestored"), StringComparison.Ordinal));
            vm.Dispose();
        });
    }

    // ---------- Full wipe ----------

    [TestMethod]
    public async Task FactoryWipe_DeletesAllowListedDataOnly()
    {
        await RunOnStaAsync(async () =>
        {
            AssumeNoStartupResidue();
            SeedUserData(marker: 1);
            string outside = Path.Combine(Path.GetTempPath(), "FrameHub.WipeProbe." + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(outside, "must survive");
            try
            {
                using var runtime = CreateRuntime();
                var result = await runtime.WipeAllUserDataAsync();
                Assert.IsTrue(result.Success, "Wipe errors: " + string.Join("; ", result.Errors));
                Assert.IsFalse(result.Blocked);
                foreach (string name in new[]
                    {
                        "settings.json.bak", "profiles.json", "library.json",
                        "session_optimization.json", "active_session.json",
                        "paired-devices.json"
                    })
                {
                    Assert.IsFalse(File.Exists(Path.Combine(_tempDirectory, name)), $"{name} must be deleted.");
                }

                Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "settings.json")),
                    "Fresh defaults must be rewritten after the wipe.");

                string logPath = Path.Combine(_tempDirectory, "FrameHub.log");
                string logContent;
                using (var logStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var logReader = new StreamReader(logStream))
                {
                    logContent = logReader.ReadToEnd();
                }

                Assert.IsFalse(logContent.Contains("synthetic log", StringComparison.Ordinal),
                    "Active log must be truncated (logger holds the handle); prior content must be gone.");

                Assert.IsFalse(Directory.Exists(Path.Combine(_tempDirectory, "Backups")),
                    "CS2 config backups must be deleted.");
                Assert.IsFalse(Directory.Exists(Path.Combine(_tempLocalDirectory, "Benchmarks")),
                    "Benchmark history must be deleted.");
                Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "GameData", "games", "g1", "game.json")),
                    "User-dropped GameData overrides must survive.");
                Assert.IsTrue(File.Exists(outside), "Files outside the data roots must survive.");
                var fresh = new SettingsService(Path.Combine(_tempDirectory, "settings.json")).LoadSettings();
                Assert.AreEqual(0, fresh.OnboardingCompletedVersion,
                    "Post-wipe settings must reset onboarding so the next launch shows the wizard.");
            }
            finally
            {
                try { File.Delete(outside); } catch { }
            }
        });
    }

    [TestMethod]
    public async Task FactoryWipe_IsBlockedDuringActiveSession()
    {
        await RunOnStaAsync(async () =>
        {
            SeedUserData(marker: 1);
            using var runtime = CreateRuntime();
            SetActiveSession(runtime, new ActiveSessionState { IsActive = true, GameId = "g1", GameName = "Game 1" });
            Assert.IsTrue(runtime.IsDestructiveResetBlocked);            var result = await runtime.WipeAllUserDataAsync();
            Assert.IsTrue(result.Blocked);
            Assert.IsFalse(result.Success);
            Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "library.json")),
                "Blocked wipe must not delete anything.");
        });
    }

    [TestMethod]
    public async Task FactoryWipe_IsBlockedWhenRecoveryPendingWithoutActiveSession()
    {
        await RunOnStaAsync(async () =>
        {
            SeedUserData(marker: 1);
            using var runtime = CreateRuntime();
            SetActiveSession(runtime, new ActiveSessionState
            {
                IsActive = false,
                IsRecoveryPending = true,
                GameId = "g1",
                GameName = "Game 1"
            });
            Assert.IsTrue(runtime.IsDestructiveResetBlocked,
                "Pending recovery without an active session still needs the journal (taskbar/power/process restore).");
            var result = await runtime.WipeAllUserDataAsync();
            Assert.IsTrue(result.Blocked);
            Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "active_session.json")),
                "Blocked wipe must preserve the recovery journal.");
        });
    }

    [TestMethod]
    public async Task FactoryWipe_IsBlockedDuringNonTerminalRecoveryPhase()
    {
        await RunOnStaAsync(async () =>
        {
            SeedUserData(marker: 1);
            using var runtime = CreateRuntime();
            foreach (var phase in new[]
                {
                    SessionRecoveryPhase.Prepared, SessionRecoveryPhase.Applying,
                    SessionRecoveryPhase.Restoring, SessionRecoveryPhase.RollingBack
                })
            {
                SetActiveSession(runtime, new ActiveSessionState
                {
                    IsActive = false,
                    IsRecoveryPending = false,
                    RecoveryPhase = phase,
                    GameId = "g1",
                    GameName = "Game 1"
                });
                Assert.IsTrue(runtime.IsDestructiveResetBlocked, $"Phase {phase} must block the wipe.");
                var result = await runtime.WipeAllUserDataAsync();
                Assert.IsTrue(result.Blocked, $"Phase {phase} must refuse deletion.");
            }

            Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "library.json")));
        });
    }

    [TestMethod]
    public async Task FactoryWipe_IsBlockedWhileSessionOperationInFlight()
    {
        await RunOnStaAsync(async () =>
        {
            SeedUserData(marker: 1);
            using var runtime = CreateRuntime();
            var field = typeof(App.Services.SessionOptimizationCoordinator).GetField(
                "_activeOperations",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field!.SetValue(runtime.SessionOptimizationCoordinator, 1);
            try
            {
                Assert.IsTrue(runtime.IsDestructiveResetBlocked);
                var result = await runtime.WipeAllUserDataAsync();
                Assert.IsTrue(result.Blocked);
                Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "library.json")));
            }
            finally
            {
                field.SetValue(runtime.SessionOptimizationCoordinator, 0);
            }
        });
    }

    [TestMethod]
    public async Task FactoryWipe_IsBlockedWhileLoadedJournalUnresolved()
    {
        await RunOnStaAsync(async () =>
        {
            SeedUserData(marker: 1);
            using var runtime = CreateRuntime();
            var field = typeof(App.Services.SessionOptimizationCoordinator).GetField(
                "_loadedStateRequiresConservativeRecovery",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field!.SetValue(runtime.SessionOptimizationCoordinator, true);
            try
            {
                Assert.IsTrue(runtime.IsDestructiveResetBlocked,
                    "An unresolved startup journal must block the wipe even with no in-memory session.");
                var result = await runtime.WipeAllUserDataAsync();
                Assert.IsTrue(result.Blocked);
            }
            finally
            {
                field.SetValue(runtime.SessionOptimizationCoordinator, false);
            }
        });
    }

    [TestMethod]
    public async Task FactoryWipe_AllowedForTerminalCleanSessionState()
    {
        await RunOnStaAsync(async () =>
        {
            AssumeNoStartupResidue();
            SeedUserData(marker: 1);
            using var runtime = CreateRuntime();
            SetActiveSession(runtime, new ActiveSessionState
            {
                IsActive = false,
                IsRecoveryPending = false,
                RecoveryPhase = SessionRecoveryPhase.Active,
                GameId = "g1",
                GameName = "Game 1"
            });
            Assert.IsFalse(runtime.IsDestructiveResetBlocked);
            var result = await runtime.WipeAllUserDataAsync();
            Assert.IsTrue(result.Success, "Terminal clean state must allow the wipe. Errors: " +
                string.Join("; ", result.Errors));
        });
    }

    [TestMethod]
    public async Task FactoryWipe_IsIdempotent()
    {
        await RunOnStaAsync(async () =>
        {
            AssumeNoStartupResidue();
            SeedUserData(marker: 1);
            using var runtime = CreateRuntime();
            var first = await runtime.WipeAllUserDataAsync();
            Assert.IsTrue(first.Success, string.Join("; ", first.Errors));
            var second = await runtime.WipeAllUserDataAsync();
            Assert.IsTrue(second.Success, "Re-running the wipe must succeed. Errors: " +
                string.Join("; ", second.Errors));
        });
    }

    [TestMethod]
    public async Task FactoryResetCommand_RequiresTypedReset()
    {
        await RunOnStaAsync(async () =>
        {
            SeedUserData(marker: 1);
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new SettingsViewModel(localization, runtime);
            Assert.IsFalse(vm.IsFactoryResetArmed);
            await vm.RunFactoryResetAsync();
            Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "library.json")),
                "Unarmed factory reset must not run.");
            vm.FactoryResetConfirmationText = "reset";
            Assert.IsFalse(vm.IsFactoryResetArmed, "Arming requires exact RESET.");
            vm.FactoryResetConfirmationText = "RESET";
            Assert.IsTrue(vm.IsFactoryResetArmed);
            vm.Dispose();
        });
    }

    [TestMethod]
    public void WizardReopen_KeepsCurrentModuleState()
    {
        RunOnSta(() =>
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "settings.json"),
                """{"HardwareMonitorEnabled":true,"OnboardingCompletedVersion":1}""");
            using var runtime = CreateRuntime();
            Assert.IsFalse(runtime.IsFirstRun);
            var localization = new LocalizationService(runtime.SettingsService);
            var wizard = new FirstRunSetupViewModel(localization, runtime);
            Assert.IsTrue(wizard.HardwareMonitorEnabled,
                "Reopening the wizard from Settings must reflect current state, not defaults.");
        });
    }

    [TestMethod]
    public async Task FactoryWipe_LeavesUnmanagedBackupsEntriesInPlace()
    {
        await RunOnStaAsync(async () =>
        {
            AssumeNoStartupResidue();
            SeedUserData(marker: 1);
            string unmanaged = Path.Combine(_tempDirectory, "Backups", "my-manual-note.txt");
            File.WriteAllText(unmanaged, "user placed");
            using var runtime = CreateRuntime();
            var result = await runtime.WipeAllUserDataAsync();
            Assert.IsTrue(result.Success, "Wipe errors: " + string.Join("; ", result.Errors));
            Assert.IsFalse(Directory.Exists(Path.Combine(_tempDirectory, "Backups", "CS2")),
                "Owned CS2 backup subtree must be deleted.");
            Assert.IsTrue(File.Exists(unmanaged), "Unmanaged Backups entries must survive.");
            Assert.IsTrue(Directory.Exists(Path.Combine(_tempDirectory, "Backups")),
                "Non-empty Backups must be left in place, not removed.");
        });
    }

    [TestMethod]
    public async Task FactoryWipe_RefusesTreeWithReparsePointAndRecoversOnRetry()
    {
        await RunOnStaAsync(async () =>
        {
            AssumeNoStartupResidue();
            SeedUserData(marker: 1);
            string link = Path.Combine(_tempDirectory, "Backups", "CS2", "x", "ext-link");
            if (!TryCreateJunction(link, _tempDirectory))
            {
                Assert.Inconclusive("Junction creation unavailable in this environment.");
            }

            try
            {
                using var runtime = CreateRuntime();
                var refused = await runtime.WipeAllUserDataAsync();
                Assert.IsFalse(refused.Success, "Wipe must fail closed on reparse points.");
                Assert.IsTrue(refused.Errors.Any(e => e.Contains("link", StringComparison.OrdinalIgnoreCase)),
                    "Refusal must name the link. Errors: " + string.Join("; ", refused.Errors));
                Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "library.json")),
                    "Refused subtree must stay intact.");

                Directory.Delete(link);
                var retried = await runtime.WipeAllUserDataAsync();
                Assert.IsTrue(retried.Success, "Retry after link removal must succeed. Errors: " +
                    string.Join("; ", retried.Errors));
            }
            finally
            {
                try { if (Directory.Exists(link)) Directory.Delete(link); } catch { }
            }
        });
    }

    [TestMethod]
    public async Task ResetSettings_ReconcilesStartupOffOnCleanMachine()
    {
        await RunOnStaAsync(async () =>
        {
            AssumeNoStartupResidue();
            File.WriteAllText(Path.Combine(_tempDirectory, "settings.json"),
                """{"StartWithWindows":false,"OnboardingCompletedVersion":1}""");
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new SettingsViewModel(localization, runtime);
            await vm.ConfirmResetSettingsAsync();
            Assert.IsTrue(vm.DataResetStatusMessage.Contains(
                localization.T("Settings.DataReset.SettingsRestored"), StringComparison.Ordinal));
            Assert.IsFalse(vm.DataResetStatusMessage.Contains("emains", StringComparison.Ordinal),
                "Clean machine must report full success, no residue. Got: " + vm.DataResetStatusMessage);
            vm.Dispose();
        });
    }

    [TestMethod]
    public async Task LiveTelemetry_StaysDownWhenCompanionOff()
    {
        await RunOnStaAsync(async () =>
        {
            using var runtime = CreateRuntime();
            await runtime.SyncCompanionServerStateAsync();
            Assert.IsFalse(runtime.IsLiveTelemetryRunningForTesting,
                "Live loop must never start while Companion is off, regardless of the live flag.");
        });
    }

    [TestMethod]
    public void WipeStatus_FullSuccess_RestartsWithoutWarning()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new SettingsViewModel(localization, runtime);
            var result = new AppRuntimeService.UserDataWipeResult(
                true, false, new[] { "settings.json" }, Array.Empty<string>(),
                Array.Empty<string>(), null);
            Assert.AreEqual(localization.T("Settings.DataReset.WipeCompleteRestarting"),
                vm.BuildWipeStatusMessage(result));
            Assert.IsTrue(SettingsViewModel.ShouldRestartAfterWipe(result));
            vm.Dispose();
        });
    }

    [TestMethod]
    public void WipeStatus_StartupResidue_WarnsButStillRestarts()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new SettingsViewModel(localization, runtime);
            var result = new AppRuntimeService.UserDataWipeResult(
                true, false, new[] { "settings.json" }, Array.Empty<string>(),
                Array.Empty<string>(), "elevated scheduled task remains");
            string message = vm.BuildWipeStatusMessage(result);
            Assert.IsTrue(message.Contains("elevated scheduled task remains", StringComparison.Ordinal),
                "Residue must be named, not hidden. Got: " + message);
            Assert.AreNotEqual(localization.T("Settings.DataReset.WipeCompleteRestarting"), message,
                "Residue must never present as full success.");
            Assert.IsTrue(SettingsViewModel.ShouldRestartAfterWipe(result),
                "Data success must keep the restart path into the wizard.");
            vm.Dispose();
        });
    }

    [TestMethod]
    public void WipeStatus_DataFailure_NeverRestarts()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new SettingsViewModel(localization, runtime);
            var result = new AppRuntimeService.UserDataWipeResult(
                false, false, Array.Empty<string>(), Array.Empty<string>(),
                new[] { "Could not delete file x" }, null);
            Assert.IsTrue(vm.BuildWipeStatusMessage(result).Contains("x", StringComparison.Ordinal));
            Assert.IsFalse(SettingsViewModel.ShouldRestartAfterWipe(result));
            vm.Dispose();
        });
    }

    [TestMethod]
    public void WipeStatus_Blocked_NeverRestarts()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new SettingsViewModel(localization, runtime);
            var result = new AppRuntimeService.UserDataWipeResult(
                false, true, Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), null);
            Assert.AreEqual(localization.T("Settings.DataReset.BlockedBusy"),
                vm.BuildWipeStatusMessage(result));
            Assert.IsFalse(SettingsViewModel.ShouldRestartAfterWipe(result));
            vm.Dispose();
        });
    }

    [TestMethod]
    public async Task FactoryReset_FullSuccess_RestartsIntoWizard()
    {
        await RunOnStaAsync(async () =>
        {
            AssumeNoStartupResidue();
            SeedUserData(marker: 1);
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new SettingsViewModel(localization, runtime);
            bool restartRequested = false;
            vm.NormalRestartRequested += () => restartRequested = true;
            vm.FactoryResetConfirmationText = "RESET";
            await vm.RunFactoryResetAsync();
            Assert.IsTrue(restartRequested, "Data success must raise the restart into the wizard.");
            Assert.AreEqual(localization.T("Settings.DataReset.WipeCompleteRestarting"),
                vm.DataResetStatusMessage);
            Assert.AreEqual(string.Empty, vm.FactoryResetConfirmationText);
            vm.Dispose();
        });
    }

    [TestMethod]
    public async Task FactoryReset_DataFailure_KeepsConfirmationAndSkipsRestart()
    {
        await RunOnStaAsync(async () =>
        {
            SeedUserData(marker: 1);
            string link = Path.Combine(_tempDirectory, "Backups", "CS2", "x", "ext-link");
            if (!TryCreateJunction(link, _tempDirectory))
            {
                Assert.Inconclusive("Junction creation unavailable in this environment.");
            }

            try
            {
                using var runtime = CreateRuntime();
                var localization = new LocalizationService(runtime.SettingsService);
                var vm = new SettingsViewModel(localization, runtime);
                bool restartRequested = false;
                vm.NormalRestartRequested += () => restartRequested = true;
                vm.FactoryResetConfirmationText = "RESET";
                await vm.RunFactoryResetAsync();
                Assert.IsFalse(restartRequested, "Data failure must not restart.");
                Assert.AreEqual("RESET", vm.FactoryResetConfirmationText,
                    "Arming must be kept so the user can retry after removing the link.");
                Assert.IsTrue(vm.DataResetStatusMessage.Contains(
                    localization.T("Settings.DataReset.WipeIncomplete").Split(':')[0],
                    StringComparison.Ordinal));
                vm.Dispose();
            }
            finally
            {
                try { if (Directory.Exists(link)) Directory.Delete(link); } catch { }
            }
        });
    }

    [TestMethod]
    public async Task FactoryReset_BlockedSession_SkipsRestartAndKeepsData()
    {
        await RunOnStaAsync(async () =>
        {
            SeedUserData(marker: 1);
            using var runtime = CreateRuntime();
            SetActiveSession(runtime, new ActiveSessionState { IsActive = true, GameId = "g1", GameName = "Game 1" });
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new SettingsViewModel(localization, runtime);
            bool restartRequested = false;
            vm.NormalRestartRequested += () => restartRequested = true;
            vm.FactoryResetConfirmationText = "RESET";
            await vm.RunFactoryResetAsync();
            Assert.IsFalse(restartRequested);
            Assert.AreEqual(localization.T("Settings.DataReset.BlockedBusy"), vm.DataResetStatusMessage);
            Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "library.json")));
            vm.Dispose();
        });
    }

    private static bool TryCreateJunction(string linkPath, string targetPath)
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return false;
            process.WaitForExit(10000);
            return process.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Guards tests that reconcile Windows startup state: if this machine has
    /// a real FrameHub autostart entry, removal tests would touch real user
    /// configuration and must not run.
    /// </summary>
    private static void AssumeNoStartupResidue()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            if (key?.GetValue("FrameHub") is not null)
            {
                Assert.Inconclusive("Real FrameHub autostart registry entry present; skipping.");
            }
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
            Assert.Inconclusive($"Could not inspect startup registry: {ex.Message}");
        }
    }

    private static void SetActiveSession(AppRuntimeService runtime, ActiveSessionState state)
    {
        var field = typeof(App.Services.SessionOptimizationCoordinator).GetField(
            "_activeSession",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(runtime.SessionOptimizationCoordinator, state);
    }

    [TestMethod]
    public void WizardWindow_MaterializesWithAllSteps()
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

                using var runtime = CreateRuntime();
                var localization = new LocalizationService(runtime.SettingsService);
                var wizard = new FirstRunSetupViewModel(localization, runtime);
                var window = new App.Views.FirstRunSetupWindow { DataContext = wizard };
                window.Measure(new System.Windows.Size(660, 600));
                window.Arrange(new System.Windows.Rect(0, 0, 660, 600));
                window.UpdateLayout();
                Assert.IsTrue(wizard.IsWelcomeVisible);
                wizard.Step = 5;
                window.UpdateLayout();
                Assert.IsTrue(wizard.IsSummaryVisible);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(60)), "Wizard materialization timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    private void SeedUserData(int marker)
    {
        File.WriteAllText(Path.Combine(_tempDirectory, "settings.json"),
            $$"""{"Language":"pl","OnboardingCompletedVersion":{{marker}}}""");
        File.WriteAllText(Path.Combine(_tempDirectory, "profiles.json"), """[]""");
        File.WriteAllText(Path.Combine(_tempDirectory, "library.json"), """[{"Id":"g1"}]""");
        File.WriteAllText(Path.Combine(_tempDirectory, "session_optimization.json"), """{}""");
        File.WriteAllText(Path.Combine(_tempDirectory, "active_session.json"), """{}""");
        File.WriteAllText(Path.Combine(_tempDirectory, "paired-devices.json"), """[]""");
        File.WriteAllText(Path.Combine(_tempDirectory, "FrameHub.log"), "synthetic log");
        Directory.CreateDirectory(Path.Combine(_tempDirectory, "Backups", "CS2", "x"));
        File.WriteAllText(Path.Combine(_tempDirectory, "Backups", "CS2", "x", "autoexec.cfg"), "echo hi");
        Directory.CreateDirectory(Path.Combine(_tempLocalDirectory, "Benchmarks", "s1"));
        File.WriteAllText(Path.Combine(_tempLocalDirectory, "Benchmarks", "s1", "session.json"), """{}""");
        Directory.CreateDirectory(Path.Combine(_tempDirectory, "GameData", "games", "g1"));
        File.WriteAllText(Path.Combine(_tempDirectory, "GameData", "games", "g1", "game.json"), """{}""");
    }

    private AppRuntimeService CreateRuntime(int port = 0, bool companion = false, bool live = false)
    {
        // Production-equivalent default path: seeding tests write plain
        // settings.json and the runtime must resolve the same file.
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        if (port > 0 || companion || live)
        {
            var seed = new AppSettings
            {
                CompanionEnabled = companion,
                CompanionPort = port > 0 ? port : 47821,
                LiveTelemetryEnabled = live,
                OnboardingCompletedVersion = 1
            };
            new SettingsService(settingsPath).SaveSettings(seed);
        }

        return new AppRuntimeService(settingsPath);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(60)), "STA test host timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    private static async Task RunOnStaAsync(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action().GetAwaiter().GetResult(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(90)), "STA test host timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
        await Task.CompletedTask;
    }
}
