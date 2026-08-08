using FrameHub.Core.Models;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace FrameHub.Core.Services;

public sealed record ScheduledTaskQueryResult(bool ReadSucceeded, bool Exists, string? Xml = null, string? Error = null);

public interface ITaskSchedulerQuery
{
    ScheduledTaskQueryResult Query(string taskName);
}

/// <summary>Locale-independent read-only Task Scheduler COM query.</summary>
public sealed class TaskSchedulerComQuery : ITaskSchedulerQuery
{
    // HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND) and ERROR_PATH_NOT_FOUND.
    // The latter is documented by MS-TSCH as a GetTask result for an absent task path.
    internal const int TaskFileNotFoundHResult = unchecked((int)0x80070002);
    internal const int TaskPathNotFoundHResult = unchecked((int)0x80070003);

    public ScheduledTaskQueryResult Query(string taskName)
    {
        object? service = null;
        object? folder = null;
        object? task = null;
        string stage = "create Schedule.Service";
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null) return new(false, false, Error: "Task Scheduler COM is unavailable.");
            service = Activator.CreateInstance(type);
            if (service == null) return new(false, false, Error: "Task Scheduler COM could not be created.");

            dynamic scheduler = service;
            stage = "Connect";
            scheduler.Connect();
            stage = "GetFolder";
            folder = scheduler.GetFolder("\\");
            try
            {
                stage = "GetTask";
                task = ((dynamic)folder).GetTask(taskName);
            }
            catch (Exception ex) when (IsTaskNotFoundHResult(ex.HResult))
            {
                return new(true, false);
            }

            stage = "read task XML";
            string xml = ((dynamic)task).Xml;
            return new(true, true, xml);
        }
        catch (Exception ex)
        {
            return new(false, false, Error: $"Task Scheduler {stage} failed; Exception={ex.GetType().Name}; HRESULT=0x{ex.HResult:X8}; {ex.Message}");
        }
        finally
        {
            Release(task);
            Release(folder);
            Release(service);
        }
    }

    public static bool IsTaskNotFoundHResult(int hresult) => hresult is TaskFileNotFoundHResult or TaskPathNotFoundHResult;

    private static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}

public sealed class ScheduledTaskStartupReader(ITaskSchedulerQuery query, string expectedExecutablePath)
{
    public ScheduledTaskStartupSnapshot Read(DesiredStartupConfiguration desired)
    {
        var result = query.Query("FrameHub");
        if (!result.ReadSucceeded) return new(false, false, null, string.Empty, false, false, false, false, false, false, result.Error);
        if (!result.Exists) return new(false, false, null, string.Empty, false, false, false, false, false);

        try
        {
            var xml = XDocument.Parse(result.Xml!);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            bool enabled = !bool.TryParse(xml.Descendants(ns + "Settings").Elements(ns + "Enabled").FirstOrDefault()?.Value, out bool parsedEnabled) || parsedEnabled;
            bool logon = xml.Descendants(ns + "LogonTrigger").Any();
            bool elevated = string.Equals(xml.Descendants(ns + "RunLevel").FirstOrDefault()?.Value, "HighestAvailable", StringComparison.OrdinalIgnoreCase);
            string? path = NormalizePath(xml.Descendants(ns + "Command").FirstOrDefault()?.Value);
            string arguments = xml.Descendants(ns + "Arguments").FirstOrDefault()?.Value ?? string.Empty;
            return new(true, enabled, path, arguments, path != null && File.Exists(path), logon, elevated,
                PathsEqual(path, expectedExecutablePath), string.Equals(arguments, desired.Arguments, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            return new(false, false, null, string.Empty, false, false, false, false, false, false, ex.Message);
        }
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path.Trim()); } catch { return path.Trim(); }
    }

    private static bool PathsEqual(string? left, string? right) => left != null && right != null &&
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
}
