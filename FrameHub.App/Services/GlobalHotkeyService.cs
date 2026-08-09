using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace FrameHub.App.Services;

[Flags]
public enum GlobalHotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

public readonly record struct BenchmarkHotkeyGesture(GlobalHotkeyModifiers Modifiers, int VirtualKey)
{
    public static bool TryCreate(Key key, ModifierKeys modifiers, out BenchmarkHotkeyGesture gesture)
    {
        gesture = default;
        if (key is Key.None or Key.System or Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return false;

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0) return false;

        GlobalHotkeyModifiers nativeModifiers = GlobalHotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Alt)) nativeModifiers |= GlobalHotkeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Control)) nativeModifiers |= GlobalHotkeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Shift)) nativeModifiers |= GlobalHotkeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) nativeModifiers |= GlobalHotkeyModifiers.Windows;

        bool allowedStandaloneFunctionKey = key is >= Key.F8 and <= Key.F12;
        if (nativeModifiers == GlobalHotkeyModifiers.None && !allowedStandaloneFunctionKey) return false;
        gesture = new BenchmarkHotkeyGesture(nativeModifiers, virtualKey);
        return true;
    }

    public static BenchmarkHotkeyGesture? FromSettings(bool enabled, uint modifiers, int virtualKey)
    {
        if (!enabled || virtualKey <= 0) return null;
        const uint allowedModifiers = (uint)(GlobalHotkeyModifiers.Alt | GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Shift | GlobalHotkeyModifiers.Windows);
        if ((modifiers & ~allowedModifiers) != 0) return null;
        var gesture = new BenchmarkHotkeyGesture((GlobalHotkeyModifiers)modifiers, virtualKey);
        Key key = KeyInterop.KeyFromVirtualKey(virtualKey);
        ModifierKeys keys = ModifierKeys.None;
        if (gesture.Modifiers.HasFlag(GlobalHotkeyModifiers.Alt)) keys |= ModifierKeys.Alt;
        if (gesture.Modifiers.HasFlag(GlobalHotkeyModifiers.Control)) keys |= ModifierKeys.Control;
        if (gesture.Modifiers.HasFlag(GlobalHotkeyModifiers.Shift)) keys |= ModifierKeys.Shift;
        if (gesture.Modifiers.HasFlag(GlobalHotkeyModifiers.Windows)) keys |= ModifierKeys.Windows;
        return TryCreate(key, keys, out _) ? gesture : null;
    }

    public override string ToString()
    {
        List<string> parts = [];
        if (Modifiers.HasFlag(GlobalHotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(GlobalHotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(GlobalHotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(GlobalHotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyInterop.KeyFromVirtualKey(VirtualKey).ToString());
        return string.Join(" + ", parts);
    }
}

internal interface IGlobalHotkeyNative
{
    bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);
    bool Unregister(IntPtr windowHandle, int id);
}

internal sealed class WindowsGlobalHotkeyNative : IGlobalHotkeyNative
{
    public bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey) => RegisterHotKey(windowHandle, id, modifiers, virtualKey);
    public bool Unregister(IntPtr windowHandle, int id) => UnregisterHotKey(windowHandle, id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

public sealed class GlobalHotkeyService : IDisposable
{
    internal const int HotkeyId = 0x4648;
    internal const int WmHotkey = 0x0312;
    private const uint NoRepeat = 0x4000;
    private readonly IntPtr _windowHandle;
    private readonly IGlobalHotkeyNative _native;
    private readonly HwndSource? _source;
    private bool _registered;
    private bool _disposed;

    public event EventHandler? HotkeyPressed;
    public BenchmarkHotkeyGesture? RegisteredGesture { get; private set; }

    public GlobalHotkeyService(HwndSource source) : this(source.Handle, new WindowsGlobalHotkeyNative(), source) { }

    internal GlobalHotkeyService(IntPtr windowHandle, IGlobalHotkeyNative native, HwndSource? source = null)
    {
        _windowHandle = windowHandle;
        _native = native;
        _source = source;
        _source?.AddHook(WindowMessageHook);
    }

    public bool UpdateRegistration(BenchmarkHotkeyGesture? gesture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (gesture == RegisteredGesture && (_registered || gesture is null)) return true;
        UnregisterCurrent();
        if (gesture is null) return true;
        _registered = _native.Register(_windowHandle, HotkeyId, (uint)gesture.Value.Modifiers | NoRepeat, (uint)gesture.Value.VirtualKey);
        RegisteredGesture = _registered ? gesture : null;
        return _registered;
    }

    internal IntPtr ProcessWindowMessage(int message, IntPtr wParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId && _registered)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        ProcessWindowMessage(message, wParam, ref handled);

    private void UnregisterCurrent()
    {
        if (_registered) _native.Unregister(_windowHandle, HotkeyId);
        _registered = false;
        RegisteredGesture = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterCurrent();
        _source?.RemoveHook(WindowMessageHook);
    }
}
