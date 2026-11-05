using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MicLock.Tools.KeyboardDiagnostics.Native;

public static class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13;

    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const uint LLKHF_EXTENDED = 0x01;
    public const uint LLKHF_LOWER_IL_INJECTED = 0x02;
    public const uint LLKHF_INJECTED = 0x10;
    public const uint LLKHF_ALTDOWN = 0x20;
    public const uint LLKHF_UP = 0x80;

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_RWIN = 0x5C;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_MENU_MASK = 0xE8;
    public const ushort VK_DUMMY = 0xFF;

    public const uint EXTRA_INFO_MICLOCK_DUMMY = 0x2000;
    public const uint EXTRA_INFO_POWERTOYS_SHORTCUT = 0x101;
    public const uint EXTRA_INFO_AHK_MASK = 0x1001;
    public const uint EXTRA_INFO_SYNTHETIC_DIAGNOSTIC = 0x3003;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    public static bool IsKeyDown(int vKey)
    {
        return (GetAsyncKeyState(vKey) & 0x8000) != 0;
    }

    public static bool IsExtendedKey(ushort vk)
    {
        return vk switch
        {
            0x5B or 0x5C => true, // VK_LWIN, VK_RWIN
            0x5D => true,         // VK_APPS
            0xA3 => true,         // VK_RCONTROL
            0xA5 => true,         // VK_RMENU
            0x2D or 0x2E => true, // VK_INSERT, VK_DELETE
            0x24 or 0x23 => true, // VK_HOME, VK_END
            0x21 or 0x22 => true, // VK_PRIOR, VK_NEXT
            0x25 or 0x26 or 0x27 or 0x28 => true, // VK_LEFT, VK_UP, VK_RIGHT, VK_DOWN
            0x6F => true,         // VK_DIVIDE
            0x90 => true,         // VK_NUMLOCK
            0x2C => true,         // VK_SNAPSHOT
            _ => false
        };
    }

    public static void SendKey(ushort vk, bool isKeyUp, bool isExtended = false, uint extraInfo = 0)
    {
        uint flags = isKeyUp ? KEYEVENTF_KEYUP : 0;
        if (isExtended || IsExtendedKey(vk))
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        var inputs = new INPUT[1];
        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)MapVirtualKey(vk, 0),
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = (UIntPtr)extraInfo
                }
            }
        };

        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void SendKeyDownUp(ushort vk, bool isExtended = false, uint extraInfo = 0)
    {
        SendKey(vk, isKeyUp: false, isExtended: isExtended, extraInfo: extraInfo);
        SendKey(vk, isKeyUp: true, isExtended: isExtended, extraInfo: extraInfo);
    }

    private static readonly ConcurrentDictionary<uint, (string Name, DateTime CachedAt)> ProcessCache = new();

    public static (string Title, string ProcessName, uint ProcessId) GetForegroundWindowDetails()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return ("<No Foreground Window>", "N/A", 0);
        }

        GetWindowThreadProcessId(hwnd, out uint pid);

        const int maxChars = 256;
        var sb = new StringBuilder(maxChars);
        string title = GetWindowText(hwnd, sb, maxChars) > 0 ? sb.ToString() : "<No Title>";

        string procName = "Unknown";
        if (pid > 0)
        {
            if (ProcessCache.TryGetValue(pid, out var cached) && (DateTime.UtcNow - cached.CachedAt).TotalSeconds < 3)
            {
                procName = cached.Name;
            }
            else
            {
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    procName = proc.ProcessName;
                    ProcessCache[pid] = (procName, DateTime.UtcNow);
                }
                catch
                {
                    procName = "System";
                }
            }
        }

        return (title, procName, pid);
    }

    public static string FormatKeyName(uint vkCode)
    {
        return vkCode switch
        {
            0 => "None",
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0C => "Clear",
            0x0D => "Enter",
            0x10 => "Shift",
            0x11 => "Control",
            0x12 => "Alt / Menu",
            0x13 => "Pause",
            0x14 => "Caps Lock",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left Arrow",
            0x26 => "Up Arrow",
            0x27 => "Right Arrow",
            0x28 => "Down Arrow",
            0x2C => "Print Screen",
            0x2D => "Insert",
            0x2E => "Delete",
            >= 0x30 and <= 0x39 => ((char)vkCode).ToString(),
            >= 0x41 and <= 0x5A => ((char)vkCode).ToString(),
            0x5B => "Left Windows",
            0x5C => "Right Windows",
            0x5D => "Applications",
            0x5F => "Sleep",
            >= 0x60 and <= 0x69 => $"Numpad {vkCode - 0x60}",
            0x6A => "Numpad *",
            0x6B => "Numpad +",
            0x6C => "Separator",
            0x6D => "Numpad -",
            0x6E => "Numpad .",
            0x6F => "Numpad /",
            0x83 => "Dedicated Mic Key / F20",
            >= 0x70 and <= 0x87 => $"F{vkCode - 0x6F}",
            0x90 => "Num Lock",
            0x91 => "Scroll Lock",
            0xA0 => "Left Shift",
            0xA1 => "Right Shift",
            0xA2 => "Left Control",
            0xA3 => "Right Control",
            0xA4 => "Left Alt",
            0xA5 => "Right Alt",
            0xAD => "Volume Mute",
            0xAE => "Volume Down",
            0xAF => "Volume Up",
            0xB0 => "Media Next",
            0xB1 => "Media Previous",
            0xB2 => "Media Stop",
            0xB3 => "Media Play/Pause",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            0xE8 => "Menu Mask (0xE8)",
            0xFF => "Dummy Key (0xFF)",
            _ => $"VK_0x{vkCode:X2}"
        };
    }
}
