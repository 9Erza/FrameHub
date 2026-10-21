using System;
using System.Runtime.InteropServices;
using FrameHub.Core.Logging;

namespace FrameHub.Core.Services.SessionOptimization;

/// <summary>
/// Safe wrapper and test seam around Windows power scheme APIs (powrprof.dll).
/// </summary>
public class PowerPlanService
{
    private static readonly ILogger _logger = LoggerService.Instance;

    // Standard Windows power scheme GUIDs
    public static readonly Guid HighPerformanceSchemeGuid = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid BalancedSchemeGuid = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid PowerSaverSchemeGuid = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public virtual Guid? GetActivePowerScheme()
    {
        try
        {
            uint result = PowerGetActiveScheme(IntPtr.Zero, out IntPtr activePolicyGuid);
            if (result != 0 || activePolicyGuid == IntPtr.Zero)
            {
                _logger.Warn($"PowerGetActiveScheme returned error code {result}");
                return null;
            }

            try
            {
                return Marshal.PtrToStructure<Guid>(activePolicyGuid);
            }
            finally
            {
                LocalFree(activePolicyGuid);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to retrieve active power scheme: {ex.Message}");
            return null;
        }
    }

    public virtual bool SetActivePowerScheme(Guid schemeGuid)
    {
        try
        {
            uint result = PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
            if (result != 0)
            {
                _logger.Warn($"PowerSetActiveScheme({schemeGuid}) returned error code {result}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to set active power scheme to {schemeGuid}: {ex.Message}");
            return false;
        }
    }
}
