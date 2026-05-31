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

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vKey);

    private readonly object _lock = new();
    private readonly HashSet<uint> _pressedKeys = new();
    private readonly HashSet<uint> _activeModifiers = new();
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
                _activeModifiers.Clear();
            }
        }
    }

    public void StartRecording(Action<KeyBindingRule> onKeyRecorded, Action? onCancelled = null)
    {
        lock (_lock)
        {
            _recordingCallback = onKeyRecorded;
            _recordingCancelCallback = onCancelled;
            _activeModifiers.Clear();
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
            _activeModifiers.Clear();
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

    public static uint NormalizeModifier(uint vk)
    {
        return vk switch
        {
            0x11 or 0xA2 or 0xA3 => 0x11, // VK_CONTROL, VK_LCONTROL, VK_RCONTROL
            0x12 or 0xA4 or 0xA5 => 0x12, // VK_MENU, VK_LMENU, VK_RMENU
            0x10 or 0xA0 or 0xA1 => 0x10, // VK_SHIFT, VK_LSHIFT, VK_RSHIFT
            0x5B or 0x5C => 0x5B,         // VK_LWIN, VK_RWIN
            _ => 0
        };
    }

    public static bool IsModifierKey(uint vk)
    {
        return NormalizeModifier(vk) != 0;
    }

    public static bool IsTypingKey(uint vk)
    {
        return (vk >= 0x41 && vk <= 0x5A) // A-Z
            || (vk >= 0x30 && vk <= 0x39) // 0-9
            || (vk >= 0xBA && vk <= 0xC0) // Punctuation (; = , - . / `)
            || (vk >= 0xDB && vk <= 0xDF) // [ \ ] '
            || vk == 0x20                 // Space
            || vk == 0x08                 // Backspace
            || vk == 0x0D;                // Enter
    }

    private void GetCurrentModifierStates(out bool ctrl, out bool alt, out bool shift, out bool win, uint hookFlags = 0)
    {
        lock (_lock)
        {
            ctrl = _activeModifiers.Contains(0x11) ||
                   ((GetAsyncKeyState(0x11) & 0x8000) != 0) ||
                   ((GetKeyState(0x11) & 0x8000) != 0);

            alt = _activeModifiers.Contains(0x12) ||
                  ((GetAsyncKeyState(0x12) & 0x8000) != 0) ||
                  ((GetKeyState(0x12) & 0x8000) != 0) ||
                  ((hookFlags & LLKHF_ALTDOWN) != 0);

            shift = _activeModifiers.Contains(0x10) ||
                    ((GetAsyncKeyState(0x10) & 0x8000) != 0) ||
                    ((GetKeyState(0x10) & 0x8000) != 0);

            win = _activeModifiers.Contains(0x5B) ||
                  ((GetAsyncKeyState(0x5B) & 0x8000) != 0) ||
                  ((GetAsyncKeyState(0x5C) & 0x8000) != 0) ||
                  ((GetKeyState(0x5B) & 0x8000) != 0) ||
                  ((GetKeyState(0x5C) & 0x8000) != 0);
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
                // Maintain active modifiers set
                uint normMod = NormalizeModifier(vkCode);
                if (normMod != 0)
                {
                    lock (_lock)
                    {
                        if (isDown) _activeModifiers.Add(normMod);
                        else if (isUp) _activeModifiers.Remove(normMod);
                    }
                }

                // 1. RECORDING MODE
                if (IsRecording)
                {
                    if (isDown)
                    {
                        // Check if Escape pressed with no modifiers held to cancel
                        if (vkCode == 0x1B && normMod == 0)
                        {
                            GetCurrentModifierStates(out bool c, out bool a, out bool s, out bool w, hookStruct.flags);
                            if (!c && !a && !s && !w)
                            {
                                CancelRecording();
                                return (IntPtr)1; // Swallow Escape
                            }
                        }

                        // Ignore standalone modifier presses until trigger key arrives
                        if (!IsModifierKey(vkCode))
                        {
                            GetCurrentModifierStates(out bool ctrl, out bool alt, out bool shift, out bool win, hookStruct.flags);

                            // SAFETY GUARD: Ordinary letters/numbers/typing keys (A-Z, 0-9) MUST have at least one modifier
                            // to prevent accidentally hijacking normal typing!
                            if (IsTypingKey(vkCode) && !ctrl && !alt && !shift && !win)
                            {
                                // Pass through bare letters while recording so user is not blocked
                                return CallNextHookEx(_hookId, nCode, wParam, lParam);
                            }

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
                                _activeModifiers.Clear();
                            }

                            RecordingEnded?.Invoke();
                            callback?.Invoke(recorded);

                            return (IntPtr)1; // Swallow trigger key
                        }
                    }

                    // While in recording mode, swallow modifier keys so they don't leak to other apps
                    return (IntPtr)1;
                }

                // 2. NORMAL EXECUTION MODE
                if (IsRemapEnabled)
                {
                    GetCurrentModifierStates(out bool ctrl, out bool alt, out bool shift, out bool win, hookStruct.flags);
                    bool hasModifiers = ctrl || alt || shift || win;

                    // SAFETY GUARD: If this is an ordinary typing key and no modifiers are down,
                    // it CANNOT match any legitimate global hotkey. Pass through immediately!
                    if (IsTypingKey(vkCode) && !hasModifiers)
                    {
                        return CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

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
                                // Refuse to match bare typing keys
                                if (IsTypingKey(rule.VirtualKey) && !rule.HasModifiers)
                                {
                                    continue;
                                }

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

    public static string FormatKeyName(uint vkCode)
    {
        return vkCode switch
        {
            0x83 => "Dedicated Mic Key",
            0xAD => "Mute Key",
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
