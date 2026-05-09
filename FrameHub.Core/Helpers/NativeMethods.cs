using System;
using System.Runtime.InteropServices;

namespace FrameHub.Core.Helpers
{
    /// <summary>
    /// Low-level Windows API declarations used by the app.
    /// Keep this file focused on documented Kernel32 APIs only.
    /// </summary>
    public static class NativeMethods
    {
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint PROCESS_SET_LIMITED_INFORMATION = 0x2000;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemCpuSetInformation(
            IntPtr information,
            uint bufferLength,
            out uint returnedLength,
            IntPtr hProcess,
            uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessDefaultCpuSets(
            IntPtr hProcess,
            uint[]? cpuSetIds,
            uint cpuSetIdCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessDefaultCpuSets(
            IntPtr hProcess,
            [Out] uint[]? cpuSetIds,
            uint cpuSetIdCount,
            out uint requiredIdCount);
    }
}
