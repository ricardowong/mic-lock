using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MicLock.Core.Sys;

public class GlobalHotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9001;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _hwndSource;
    private bool _isRegistered;

    public event Action? HotkeyPressed;

    public GlobalHotkeyManager()
    {
        var parameters = new HwndSourceParameters("MicLock_HotkeyHost")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Height = 0,
            Width = 0
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(HwndHook);
    }

    public bool Register(uint modifiers = MOD_CONTROL | MOD_SHIFT | MOD_ALT | MOD_NOREPEAT, uint vk = 0x4D /* 'M' */)
    {
        Unregister();

        if (_hwndSource.Handle != IntPtr.Zero)
        {
            _isRegistered = RegisterHotKey(_hwndSource.Handle, HOTKEY_ID, modifiers, vk);
            return _isRegistered;
        }

        return false;
    }

    public void Unregister()
    {
        if (_isRegistered && _hwndSource.Handle != IntPtr.Zero)
        {
            UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
            _isRegistered = false;
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _hwndSource.RemoveHook(HwndHook);
        _hwndSource.Dispose();
        GC.SuppressFinalize(this);
    }
}
