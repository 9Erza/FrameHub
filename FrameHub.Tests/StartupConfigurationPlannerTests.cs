using FrameHub.Core.Models;
using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public class StartupConfigurationPlannerTests
{
    private static RegistryStartupSnapshot Registry(bool exists = false, bool executableExists = true, bool expectedExecutable = true, bool expectedArguments = true, bool readSucceeded = true) =>
        new(exists, "C:\\FrameHub.exe", string.Empty, null, executableExists, expectedExecutable, expectedArguments, readSucceeded);

    private static ScheduledTaskStartupSnapshot Task(bool exists = false, bool enabled = true, bool executableExists = true, bool logon = true, bool elevated = true, bool expectedExecutable = true, bool expectedArguments = true, bool readSucceeded = true) =>
        new(exists, enabled, "C:\\FrameHub.exe", string.Empty, executableExists, logon, elevated, expectedExecutable, expectedArguments, readSucceeded);

    private static StartupConfigurationEvaluation Evaluate(DesiredStartupConfiguration desired, RegistryStartupSnapshot? registry = null, ScheduledTaskStartupSnapshot? task = null) =>
        StartupConfigurationPlanner.Evaluate(desired, new(registry ?? Registry(), task ?? Task()));

    private static void Has(StartupConfigurationEvaluation evaluation, StartupConfigurationReason reason, StartupOperation operation)
    {
        CollectionAssert.Contains(evaluation.Reasons.ToList(), reason);
        CollectionAssert.Contains(evaluation.RequiredOperations.ToList(), operation);
    }

    [TestMethod] public void OffWithoutEntries_IsDisabled() { var e = Evaluate(new(false, StartupWindowMode.Normal, false)); Assert.AreEqual(StartupConfigurationState.Disabled, e.State); Assert.AreEqual(0, e.Reasons.Count); Assert.AreEqual(0, e.RequiredOperations.Count); }
    [TestMethod] public void OffWithRegistry_IsBrokenAndRemovesRegistry() { var e = Evaluate(new(false, StartupWindowMode.Normal, false), Registry(true)); Assert.AreEqual(StartupConfigurationState.Broken, e.State); Has(e, StartupConfigurationReason.UnexpectedRegistryEntry, StartupOperation.RemoveRegistry); }
    [TestMethod] public void OffWithTask_IsBrokenAndRemovesTask() { var e = Evaluate(new(false, StartupWindowMode.Normal, false), task: Task(true)); Assert.AreEqual(StartupConfigurationState.Broken, e.State); Has(e, StartupConfigurationReason.UnexpectedScheduledTask, StartupOperation.RemoveScheduledTask); }
    [TestMethod] public void OffWithBoth_IsConflictAndRemovesBoth() { var e = Evaluate(new(false, StartupWindowMode.Normal, false), Registry(true), Task(true)); Assert.AreEqual(StartupConfigurationState.Conflict, e.State); CollectionAssert.AreEqual(new[] { StartupOperation.RemoveRegistry, StartupOperation.RemoveScheduledTask }, e.RequiredOperations.ToArray()); }

    [DataTestMethod]
    [DataRow(StartupWindowMode.Normal)]
    [DataRow(StartupWindowMode.Minimized)]
    [DataRow(StartupWindowMode.Tray)]
    public void CorrectRegistryForEachMode_IsHealthy(StartupWindowMode mode) => Assert.AreEqual(StartupConfigurationState.Registry, Evaluate(new(true, mode, false), Registry(true)).State);

    [TestMethod] public void WrongRegistryArguments_IsBrokenAndRepairsRegistry() { var e = Evaluate(new(true, StartupWindowMode.Normal, false), Registry(true, expectedArguments: false)); Assert.AreEqual(StartupConfigurationState.Broken, e.State); Has(e, StartupConfigurationReason.WrongArguments, StartupOperation.CreateOrUpdateRegistry); }
    [TestMethod] public void WrongRegistryExecutable_IsBrokenAndRepairsRegistry() { var e = Evaluate(new(true, StartupWindowMode.Normal, false), Registry(true, expectedExecutable: false)); Has(e, StartupConfigurationReason.WrongExecutable, StartupOperation.CreateOrUpdateRegistry); }
    [TestMethod] public void MissingRegistryExecutable_IsBroken() { var e = Evaluate(new(true, StartupWindowMode.Normal, false), Registry(true, executableExists: false)); CollectionAssert.Contains(e.Reasons.ToList(), StartupConfigurationReason.ExecutableMissing); }
    [TestMethod] public void MissingRegistry_IsBrokenAndCreatesRegistry() { var e = Evaluate(new(true, StartupWindowMode.Normal, false)); Has(e, StartupConfigurationReason.MissingRegistry, StartupOperation.CreateOrUpdateRegistry); }
    [TestMethod] public void UnknownRegistryArgument_IsRejectedByEvaluator() { var e = Evaluate(new(true, StartupWindowMode.Normal, false), Registry(true, expectedArguments: false)); CollectionAssert.Contains(e.Reasons.ToList(), StartupConfigurationReason.WrongArguments); }

    [TestMethod] public void CorrectElevatedTask_IsHealthy() => Assert.AreEqual(StartupConfigurationState.ElevatedScheduledTask, Evaluate(new(true, StartupWindowMode.Tray, true), task: Task(true)).State);
    [TestMethod] public void MissingElevatedTask_IsBrokenAndCreatesTask() { var e = Evaluate(new(true, StartupWindowMode.Normal, true)); Has(e, StartupConfigurationReason.MissingScheduledTask, StartupOperation.CreateOrUpdateScheduledTask); }
    [DataTestMethod]
    [DataRow("wrongExecutable")]
    [DataRow("wrongArguments")]
    [DataRow("missingExecutable")]
    [DataRow("disabled")]
    [DataRow("missingLogon")]
    [DataRow("notElevated")]
    public void InvalidElevatedTask_IsBroken(string kind)
    {
        var task = kind switch
        {
            "wrongExecutable" => Task(true, expectedExecutable: false),
            "wrongArguments" => Task(true, expectedArguments: false),
            "missingExecutable" => Task(true, executableExists: false),
            "disabled" => Task(true, enabled: false),
            "missingLogon" => Task(true, logon: false),
            _ => Task(true, elevated: false)
        };
        var expected = kind switch
        {
            "wrongExecutable" => StartupConfigurationReason.WrongExecutable,
            "wrongArguments" => StartupConfigurationReason.WrongArguments,
            "missingExecutable" => StartupConfigurationReason.ExecutableMissing,
            "disabled" => StartupConfigurationReason.TaskDisabled,
            "missingLogon" => StartupConfigurationReason.MissingLogonTrigger,
            _ => StartupConfigurationReason.MissingElevatedRunLevel
        };
        var e = Evaluate(new(true, StartupWindowMode.Normal, true), task: task);
        Assert.AreEqual(StartupConfigurationState.Broken, e.State); Has(e, expected, StartupOperation.CreateOrUpdateScheduledTask);
    }

    [TestMethod] public void RegistryAndTask_AreConflict() => Assert.AreEqual(StartupConfigurationState.Conflict, Evaluate(new(true, StartupWindowMode.Normal, false), Registry(true), Task(true)).State);
    [TestMethod] public void ElevatedTaskAndRegistry_AreConflict() => Assert.AreEqual(StartupConfigurationState.Conflict, Evaluate(new(true, StartupWindowMode.Normal, true), Registry(true), Task(true)).State);
    [TestMethod] public void TransitionToElevated_CreatesTaskThenRemovesRegistry() { var e = Evaluate(new(true, StartupWindowMode.Normal, true), Registry(true)); CollectionAssert.AreEqual(new[] { StartupOperation.CreateOrUpdateScheduledTask, StartupOperation.RemoveRegistry }, e.RequiredOperations.ToArray()); }
    [TestMethod] public void TransitionToNonElevated_CreatesRegistryThenRemovesTask() { var e = Evaluate(new(true, StartupWindowMode.Normal, false), task: Task(true)); CollectionAssert.AreEqual(new[] { StartupOperation.CreateOrUpdateRegistry, StartupOperation.RemoveScheduledTask }, e.RequiredOperations.ToArray()); }

    [DataTestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ReaderFailure_IsBrokenAndDoesNotPlanOperations(bool registryFailed, bool taskFailed)
    {
        var e = Evaluate(new(true, StartupWindowMode.Normal, false), Registry(readSucceeded: !registryFailed), Task(readSucceeded: !taskFailed));
        Assert.AreEqual(StartupConfigurationState.Broken, e.State); CollectionAssert.Contains(e.Reasons.ToList(), StartupConfigurationReason.ReadFailed); Assert.AreEqual(0, e.RequiredOperations.Count);
    }
}

[TestClass]
public class StartupCommandParserTests
{
    [TestMethod] public void ParsesQuotedPathWithArguments() { var p = StartupCommandParser.Parse("\"C:\\Program Files\\FrameHub\\FrameHub.App.exe\" --tray"); Assert.AreEqual("C:\\Program Files\\FrameHub\\FrameHub.App.exe", p.ExecutablePath); Assert.AreEqual("--tray", p.Arguments); }
    [TestMethod] public void ParsesQuotedPathWithoutArguments() { var p = StartupCommandParser.Parse("\"C:\\Program Files\\FrameHub\\FrameHub.App.exe\""); Assert.AreEqual(string.Empty, p.Arguments); }
    [TestMethod] public void ParsesUnquotedPathWithArguments() { var p = StartupCommandParser.Parse("C:\\FrameHub\\FrameHub.App.exe --minimized"); Assert.AreEqual("C:\\FrameHub\\FrameHub.App.exe", p.ExecutablePath); Assert.AreEqual("--minimized", p.Arguments); }
    [TestMethod] public void TrimsWhitespaceAndPreservesSpacedQuotedPath() { var p = StartupCommandParser.Parse("  \"C:\\One Two Three\\FrameHub.App.exe\" --tray  "); Assert.AreEqual("C:\\One Two Three\\FrameHub.App.exe", p.ExecutablePath); Assert.AreEqual("--tray", p.Arguments); }
    [DataTestMethod] [DataRow(null)] [DataRow("")] [DataRow("   ")]
    public void EmptyCommand_IsControlled(string? command) { var p = StartupCommandParser.Parse(command); Assert.IsNull(p.ExecutablePath); Assert.AreEqual(string.Empty, p.Arguments); }
    [TestMethod] public void UnknownArgument_ProducesArgumentsForEvaluatorToReject() { var p = StartupCommandParser.Parse("C:\\FrameHub\\FrameHub.App.exe --something-else"); Assert.AreEqual("--something-else", p.Arguments); }
    [TestMethod] public void UnterminatedQuotedPath_IsControlled() { var p = StartupCommandParser.Parse("\"C:\\Program Files\\FrameHub.exe --tray"); Assert.IsNull(p.ExecutablePath); Assert.AreEqual(string.Empty, p.Arguments); }
}

[TestClass]
public class StartupSettingsTests
{
    [DataTestMethod] [DataRow(StartupWindowMode.Normal, "")] [DataRow(StartupWindowMode.Minimized, "--minimized")] [DataRow(StartupWindowMode.Tray, "--tray")]
    public void WindowModeMapsToSingleArgumentSource(StartupWindowMode mode, string arguments) => Assert.AreEqual(arguments, DesiredStartupConfiguration.GetArguments(mode));

    [TestMethod] public void LegacyMinimized_MigratesToMinimized() { var s = new AppSettings { LegacyStartMinimized = true }; StartupSettingsMigration.Apply(s); Assert.AreEqual(StartupWindowMode.Minimized, s.StartupWindowMode); }
    [TestMethod] public void LegacyFalse_DoesNotForceMinimized() { var s = new AppSettings { LegacyStartMinimized = false }; StartupSettingsMigration.Apply(s); Assert.AreEqual(StartupWindowMode.Normal, s.StartupWindowMode); }
    [TestMethod] public void LegacyAdmin_MigratesToElevated() { var s = new AppSettings { LegacyRunAsAdministrator = true }; StartupSettingsMigration.Apply(s); Assert.IsTrue(s.StartupRunElevated); }
    [TestMethod] public void MigrationClearsLegacyFields() { var s = new AppSettings { LegacyStartMinimized = true, LegacyRunAsAdministrator = true }; StartupSettingsMigration.Apply(s); Assert.IsNull(s.LegacyStartMinimized); Assert.IsNull(s.LegacyRunAsAdministrator); }
    [TestMethod] public void MinimizeToTray_DoesNotMigrateToTray() { var s = new AppSettings { MinimizeToTray = true }; StartupSettingsMigration.Apply(s); Assert.AreEqual(StartupWindowMode.Normal, s.StartupWindowMode); }
    [TestMethod] public void MigrationIsIdempotent() { var s = new AppSettings { LegacyStartMinimized = true }; StartupSettingsMigration.Apply(s); var mode = s.StartupWindowMode; StartupSettingsMigration.Apply(s); Assert.AreEqual(mode, s.StartupWindowMode); Assert.AreEqual(StartupSettingsMigration.CurrentVersion, s.StartupSettingsVersion); }
    [TestMethod] public void MigratedSettings_DoNotOverwriteNewValues() { var s = new AppSettings { StartupSettingsVersion = StartupSettingsMigration.CurrentVersion, StartupWindowMode = StartupWindowMode.Tray, StartupRunElevated = true, LegacyStartMinimized = false, LegacyRunAsAdministrator = false }; StartupSettingsMigration.Apply(s); Assert.AreEqual(StartupWindowMode.Tray, s.StartupWindowMode); Assert.IsTrue(s.StartupRunElevated); }
}
