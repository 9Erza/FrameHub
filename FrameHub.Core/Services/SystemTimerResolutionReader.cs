using System;
using System.Runtime.InteropServices;

namespace FrameHub.Core.Services;

/// <summary>
/// On-demand, read-only system timer resolution diagnostic.
/// This code never requests or changes timer resolution.
/// </summary>
public sealed record SystemTimerResolutionSnapshot(
    bool Success,
    double MinimumMilliseconds,
    double MaximumMilliseconds,
    double CurrentMilliseconds,
    string Error);

public sealed class SystemTimerResolutionReader
{
    private const double HundredNanosecondsPerMillisecond = 10_000.0;
    private readonly Func<(int Status, uint Minimum, uint Maximum, uint Current)> _query;
    private readonly bool _isNativeQuery;

    /// <summary>
    /// An injectable query function allows unit tests without modifying Windows.
    /// </summary>
    public SystemTimerResolutionReader(
        Func<(int Status, uint Minimum, uint Maximum, uint Current)>? query = null)
    {
        _isNativeQuery = query is null;
        _query = query ?? QueryNative;
    }

    public SystemTimerResolutionSnapshot Read()
    {
        if (_isNativeQuery && !OperatingSystem.IsWindows())
        {
            return Unavailable("Windows only.");
        }

        try
        {
            (int status, uint minimum, uint maximum, uint current) = _query();
            return FromNativeValues(status, minimum, maximum, current);
        }
        catch (Exception ex) when (
            ex is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or SEHException)
        {
            return Unavailable($"Timer resolution query unavailable: {ex.GetType().Name}.");
        }
    }

    /// <summary>
    /// Converts native 100-nanosecond units into milliseconds.
    /// Negative NTSTATUS values indicate failure.
    /// </summary>
    public static SystemTimerResolutionSnapshot FromNativeValues(
        int status, uint minimum, uint maximum, uint current)
    {
        if (status < 0)
        {
            return Unavailable($"NtQueryTimerResolution failed (NTSTATUS 0x{unchecked((uint)status):X8}).");
        }

        if (minimum == 0 || maximum == 0 || current == 0)
        {
            return Unavailable("Timer resolution query returned an invalid zero value.");
        }

        return new SystemTimerResolutionSnapshot(
            true,
            minimum / HundredNanosecondsPerMillisecond,
            maximum / HundredNanosecondsPerMillisecond,
            current / HundredNanosecondsPerMillisecond,
            string.Empty);
    }

    private static SystemTimerResolutionSnapshot Unavailable(string error) =>
        new(false, 0, 0, 0, error);

    private static (int Status, uint Minimum, uint Maximum, uint Current) QueryNative()
    {
        int status = NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current);
        return (status, minimum, maximum, current);
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtQueryTimerResolution(
        out uint minimumResolution,
        out uint maximumResolution,
        out uint currentResolution);
}
