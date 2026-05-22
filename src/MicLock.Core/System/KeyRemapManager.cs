using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MicLock.Core.Sys;

public enum KeyPressState
{
    Down,
    Up
}

public class KeyRemapManager : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_ALTDOWN = 0x20;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private readonly object _lock = new();
    private readonly HashSet<uint> _pressedKeys = new();
    private readonly LowLevelKeyboardProc _hookCallbackDelegate;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _disposed;

    private Action<KeyBindingRule>? _recordingCallback;
    private Action? _recordingCancelCallback;

    public bool IsHookInstalled => _hookId != IntPtr.Zero;
    public bool IsRemapEnabled { get; set; } = true;
    public bool IsRecording { get; private set; }

    public List<KeyBindingRule> Bindings { get; set; } = new();

    public event Action<KeyRemapAction, KeyPressState>? ActionTriggered;
    public event Action? RecordingStarted;
    public event Action? RecordingEnded;

    public KeyRemapManager(IEnumerable<KeyBindingRule>? initialRules = null)
    {
        _hookCallbackDelegate = HookCallback;

        if (initialRules != null)
        {
            Bindings = initialRules.Select(r => r.Clone()).ToList();
        }
        else
        {
            Bindings = KeyBindingRule.CreateDefaultRules();
        }

        InstallHook();
    }

    public void InstallHook()
    {
        lock (_lock)
        {
            if (_hookId != IntPtr.Zero) return;

            try
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                IntPtr hMod = curModule != null ? GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;
                if (hMod == IntPtr.Zero)
                {
                    hMod = GetModuleHandle(null);
                }

                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookCallbackDelegate, hMod, 0);
            }
            catch (Exception)
            {
                _hookId = IntPtr.Zero;
            }
        }
    }

    public void UninstallHook()
    {
        lock (_lock)
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                _pressedKeys.Clear();
            }
        }
    }

    public void StartRecording(Action<KeyBindingRule> onKeyRecorded, Action? onCancelled = null)
    {
        lock (_lock)
        {
            _recordingCallback = onKeyRecorded;
            _recordingCancelCallback = onCancelled;
            IsRecording = true;
        }

        RecordingStarted?.Invoke();
    }

    public void CancelRecording()
    {
        lock (_lock)
        {
            if (!IsRecording) return;
            IsRecording = false;
            _recordingCallback = null;
            var cancelCb = _recordingCancelCallback;
            _recordingCancelCallback = null;
            cancelCb?.Invoke();
        }

        RecordingEnded?.Invoke();
    }

    public void UpdateBinding(KeyBindingRule rule)
    {
        lock (_lock)
        {
            var existing = Bindings.FirstOrDefault(b => b.Action == rule.Action);
            if (existing != null)
            {
                existing.VirtualKey = rule.VirtualKey;
                existing.ModifiersCtrl = rule.ModifiersCtrl;
                existing.ModifiersAlt = rule.ModifiersAlt;
                existing.ModifiersShift = rule.ModifiersShift;
                existing.ModifiersWin = rule.ModifiersWin;
                existing.SuppressOriginalKey = rule.SuppressOriginalKey;
                existing.DisplayText = rule.DisplayText;
            }
            else
            {
                Bindings.Add(rule.Clone());
            }
        }
    }

    public KeyBindingRule? GetBindingForAction(KeyRemapAction action)
    {
        lock (_lock)
        {
            return Bindings.FirstOrDefault(b => b.Action == action)?.Clone();
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vkCode = hookStruct.vkCode;
            int msg = wParam.ToInt32();

            bool isDown = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
            bool isUp = (msg == WM_KEYUP || msg == WM_SYSKEYUP);

            if (isDown || isUp)
            {
                // Check if in recording mode
                if (IsRecording)
                {
                    if (isDown)
                    {
                        // Check if user pressed Escape to cancel
                        if (vkCode == 0x1B && !IsModifierKeyDown())
                        {
                            CancelRecording();
                            return (IntPtr)1; // Swallow Escape
                        }

                        // Ignore lone modifier key presses in recording mode until non-modifier is pressed
                        if (!IsModifierKey(vkCode))
                        {
                            bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                            bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
                            bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0 || (hookStruct.flags & LLKHF_ALTDOWN) != 0;
                            bool win = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;

                            var recorded = new KeyBindingRule
                            {
                                VirtualKey = vkCode,
                                ModifiersCtrl = ctrl,
                                ModifiersAlt = alt,
                                ModifiersShift = shift,
                                ModifiersWin = win,
                                SuppressOriginalKey = true,
                                DisplayText = FormatKeyCombination(vkCode, ctrl, alt, shift, win)
                            };

                            Action<KeyBindingRule>? callback;
                            lock (_lock)
                            {
                                callback = _recordingCallback;
                                IsRecording = false;
                                _recordingCallback = null;
                                _recordingCancelCallback = null;
                            }

                            RecordingEnded?.Invoke();
                            callback?.Invoke(recorded);

                            return (IntPtr)1; // Swallow the recorded key press
                        }
                    }

                    return (IntPtr)1; // While recording, swallow keys
                }

                // Normal remap execution
                if (IsRemapEnabled)
                {
                    bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                    bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
                    bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0 || (hookStruct.flags & LLKHF_ALTDOWN) != 0;
                    bool win = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;

                    KeyBindingRule? matchedRule = null;
                    lock (_lock)
                    {
                        foreach (var rule in Bindings)
                        {
                            if (rule.VirtualKey == vkCode &&
                                rule.ModifiersCtrl == ctrl &&
                                rule.ModifiersAlt == alt &&
                                rule.ModifiersShift == shift &&
                                rule.ModifiersWin == win)
                            {
                                matchedRule = rule;
                                break;
                            }
                        }
                    }

                    if (matchedRule != null)
                    {
                        if (isDown)
                        {
                            bool firstPress = false;
                            lock (_lock)
                            {
                                if (!_pressedKeys.Contains(vkCode))
                                {
                                    _pressedKeys.Add(vkCode);
                                    firstPress = true;
                                }
                            }

                            if (firstPress)
                            {
                                ActionTriggered?.Invoke(matchedRule.Action, KeyPressState.Down);
                            }

                            if (matchedRule.SuppressOriginalKey)
                            {
                                return (IntPtr)1;
                            }
                        }
                        else if (isUp)
                        {
                            bool wasDown = false;
                            lock (_lock)
                            {
                                if (_pressedKeys.Contains(vkCode))
                                {
                                    _pressedKeys.Remove(vkCode);
                                    wasDown = true;
                                }
                            }

                            if (wasDown)
                            {
                                ActionTriggered?.Invoke(matchedRule.Action, KeyPressState.Up);
                            }

                            if (matchedRule.SuppressOriginalKey)
                            {
                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public static bool IsModifierKey(uint vk)
    {
        return vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C
                  or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;
    }

    private static bool IsModifierKeyDown()
    {
        return ((GetAsyncKeyState(0x11) & 0x8000) != 0) ||
               ((GetAsyncKeyState(0x10) & 0x8000) != 0) ||
               ((GetAsyncKeyState(0x12) & 0x8000) != 0) ||
               ((GetAsyncKeyState(0x5B) & 0x8000) != 0) ||
               ((GetAsyncKeyState(0x5C) & 0x8000) != 0);
    }

    public static string FormatKeyName(uint vkCode)
    {
        return vkCode switch
        {
            0x83 => "F20",
            0xAD => "VK_VOLUME_MUTE",
            0xAE => "Volume Down",
            0xAF => "Volume Up",
            0x13 => "Pause",
            0x91 => "Scroll Lock",
            0x14 => "Caps Lock",
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x08 => "Backspace",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x25 => "Left Arrow",
            0x26 => "Up Arrow",
            0x27 => "Right Arrow",
            0x28 => "Down Arrow",
            0xC0 => "` (Backquote)",
            0xBD => "-",
            0xBB => "=",
            0xDB => "[",
            0xDD => "]",
            0xDC => "\\",
            0xBA => ";",
            0xDE => "'",
            0xBC => ",",
            0xBE => ".",
            0xBF => "/",
            >= 0x70 and <= 0x87 => $"F{(vkCode - 0x70 + 1)}", // F1 - F24
            >= 0x30 and <= 0x39 => ((char)vkCode).ToString(), // 0-9
            >= 0x41 and <= 0x5A => ((char)vkCode).ToString(), // A-Z
            >= 0x60 and <= 0x69 => $"Num {vkCode - 0x60}", // Numpad 0-9
            0 => "None",
            _ => ((System.Windows.Forms.Keys)vkCode).ToString()
        };
    }

    public static string FormatKeyCombination(uint vk, bool ctrl, bool alt, bool shift, bool win)
    {
        if (vk == 0) return "None";

        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        if (win) parts.Add("Win");
        parts.Add(FormatKeyName(vk));
        return string.Join("+", parts);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UninstallHook();
        GC.SuppressFinalize(this);
    }
}
