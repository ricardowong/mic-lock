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

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint VK_MENU_MASK = 0xE8; // AutoHotkey / Windows Shell menu mask key (clears g_fWinKeyPressed in explorer.exe)
    private const uint VK_DUMMY = 0xFF;     // PowerToys dummy key
    private const uint DUMMY_KEY_FLAG = 0x2000;
    private const uint POWERTOYS_SHORTCUT_FLAG = 0x101;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

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
    private readonly HashSet<uint> _activeModifiers = new();
    private readonly LowLevelKeyboardProc _hookCallbackDelegate;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _disposed;

    // PowerToys Remap Engine State
    private bool _isShortcutInvoked;
    private KeyBindingRule? _invokedRule;
    private bool _suppressNextWinKeyUp;

    // Capture Mode State
    private bool _isCapturingInput;
    private bool _isCapturingTarget;
    private Action<KeyBindingRule>? _captureInputCallback;
    private Action<uint, bool, bool, bool, bool, string>? _captureTargetCallback;
    private Action? _captureCancelCallback;

    public bool IsHookInstalled => _hookId != IntPtr.Zero;
    public bool IsRemapEnabled { get; set; } = true;
    public bool IsCapturing => _isCapturingInput || _isCapturingTarget;

    public List<KeyBindingRule> Bindings { get; set; } = new();

    public event Action<KeyRemapAction, KeyPressState>? ActionTriggered;
    public event Action<string, string, string>? RemapExecuted; // InputText, RemapText, Action
    public event Action<string>? LogMessage;
    public event Action? CaptureStarted;
    public event Action? CaptureEnded;

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
                IntPtr hMod = GetModuleHandle(curModule?.ModuleName);
                if (hMod == IntPtr.Zero) hMod = GetModuleHandle(null);
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
                _isShortcutInvoked = false;
                _invokedRule = null;
                _suppressNextWinKeyUp = false;
            }
        }
    }

    public void StartCaptureInput(Action<KeyBindingRule> onCaptured, Action? onCancelled = null)
    {
        lock (_lock)
        {
            _isCapturingInput = true;
            _isCapturingTarget = false;
            _captureInputCallback = onCaptured;
            _captureCancelCallback = onCancelled;
            _activeModifiers.Clear();
        }

        CaptureStarted?.Invoke();
    }

    public void StartCaptureTarget(Action<uint, bool, bool, bool, bool, string> onCaptured, Action? onCancelled = null)
    {
        lock (_lock)
        {
            _isCapturingTarget = true;
            _isCapturingInput = false;
            _captureTargetCallback = onCaptured;
            _captureCancelCallback = onCancelled;
            _activeModifiers.Clear();
        }

        CaptureStarted?.Invoke();
    }

    public void CancelCapture()
    {
        Action? cancelCb;
        lock (_lock)
        {
            if (!_isCapturingInput && !_isCapturingTarget) return;
            _isCapturingInput = false;
            _isCapturingTarget = false;
            _captureInputCallback = null;
            _captureTargetCallback = null;
            cancelCb = _captureCancelCallback;
            _captureCancelCallback = null;
            _activeModifiers.Clear();
        }

        cancelCb?.Invoke();
        CaptureEnded?.Invoke();
    }

    public void UpdateBinding(KeyBindingRule rule)
    {
        lock (_lock)
        {
            var existing = Bindings.FirstOrDefault(b => b.Action == rule.Action);
            if (existing != null)
            {
                existing.MappingType = rule.MappingType;
                existing.VirtualKey = rule.VirtualKey;
                existing.ModifiersCtrl = rule.ModifiersCtrl;
                existing.ModifiersAlt = rule.ModifiersAlt;
                existing.ModifiersShift = rule.ModifiersShift;
                existing.ModifiersWin = rule.ModifiersWin;
                existing.SuppressOriginalKey = rule.SuppressOriginalKey;
                existing.DisplayText = rule.DisplayText;
                existing.RemapVirtualKey = rule.RemapVirtualKey;
                existing.RemapModifiersCtrl = rule.RemapModifiersCtrl;
                existing.RemapModifiersAlt = rule.RemapModifiersAlt;
                existing.RemapModifiersShift = rule.RemapModifiersShift;
                existing.RemapModifiersWin = rule.RemapModifiersWin;
                existing.RemapDisplayText = rule.RemapDisplayText;
                existing.SendRemapKeys = rule.SendRemapKeys;
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

    public static void SuppressStartMenu()
    {
        try
        {
            var inputs = new INPUT[4];
            // 1. VK_MENU_MASK 0xE8 down (AutoHotkey verified shell mask key that resets g_fWinKeyPressed in explorer.exe)
            inputs[0] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)VK_MENU_MASK,
                        wScan = 0,
                        dwFlags = 0,
                        time = 0,
                        dwExtraInfo = (UIntPtr)DUMMY_KEY_FLAG
                    }
                }
            };
            // 2. VK_MENU_MASK 0xE8 up
            inputs[1] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)VK_MENU_MASK,
                        wScan = 0,
                        dwFlags = KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = (UIntPtr)DUMMY_KEY_FLAG
                    }
                }
            };
            // 3. VK_DUMMY 0xFF down (PowerToys dummy key)
            inputs[2] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)VK_DUMMY,
                        wScan = 0,
                        dwFlags = 0,
                        time = 0,
                        dwExtraInfo = (UIntPtr)DUMMY_KEY_FLAG
                    }
                }
            };
            // 4. VK_DUMMY 0xFF up
            inputs[3] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)VK_DUMMY,
                        wScan = 0,
                        dwFlags = KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = (UIntPtr)DUMMY_KEY_FLAG
                    }
                }
            };

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }
        catch { }
    }

    /// <summary>
    /// Replicates PowerToys SetModifierKeyEvents + SendVirtualInput:
    /// Injects target remapped shortcut into Windows with KEYBOARDMANAGER_SHORTCUT_FLAG (0x101).
    /// </summary>
    public static void InjectShortcut(uint vk, bool ctrl, bool alt, bool shift, bool win, uint extraInfo = POWERTOYS_SHORTCUT_FLAG)
    {
        if (vk == 0) return;

        try
        {
            var list = new List<INPUT>();

            // Modifiers down
            if (ctrl) list.Add(CreateKeyInput(0x11, 0, extraInfo));
            if (shift) list.Add(CreateKeyInput(0x10, 0, extraInfo));
            if (alt) list.Add(CreateKeyInput(0x12, 0, extraInfo));
            if (win) list.Add(CreateKeyInput(0x5B, 0, extraInfo));

            // Target action key down & up
            list.Add(CreateKeyInput((ushort)vk, 0, extraInfo));
            list.Add(CreateKeyInput((ushort)vk, KEYEVENTF_KEYUP, extraInfo));

            // Modifiers up in reverse order
            if (win) list.Add(CreateKeyInput(0x5B, KEYEVENTF_KEYUP, extraInfo));
            if (alt) list.Add(CreateKeyInput(0x12, KEYEVENTF_KEYUP, extraInfo));
            if (shift) list.Add(CreateKeyInput(0x10, KEYEVENTF_KEYUP, extraInfo));
            if (ctrl) list.Add(CreateKeyInput(0x11, KEYEVENTF_KEYUP, extraInfo));

            SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>());
        }
        catch { }
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

    private static INPUT CreateKeyInput(ushort vk, uint dwFlags, uint extraInfo)
    {
        uint flags = dwFlags;
        if (IsExtendedKey(vk))
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        return new INPUT
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
    }

    public static bool IsAltGr()
    {
        short rMenu = GetAsyncKeyState(0xA5); // VK_RMENU
        short lMenu = GetAsyncKeyState(0xA4); // VK_LMENU
        short lCtrl = GetAsyncKeyState(0xA2); // VK_LCONTROL
        short rCtrl = GetAsyncKeyState(0xA3); // VK_RCONTROL

        return (rMenu & 0x8000) != 0 && (lCtrl & 0x8000) != 0 && (lMenu & 0x8000) == 0 && (rCtrl & 0x8000) == 0;
    }

    private void GetCurrentModifierStates(out bool ctrl, out bool alt, out bool shift, out bool win, uint hookFlags = 0)
    {
        lock (_lock)
        {
            bool isAltGr = IsAltGr();

            ctrl = !isAltGr && (_activeModifiers.Contains(0x11) ||
                   ((GetAsyncKeyState(0x11) & 0x8000) != 0) ||
                   ((GetAsyncKeyState(0xA2) & 0x8000) != 0) ||
                   ((GetAsyncKeyState(0xA3) & 0x8000) != 0));

            alt = !isAltGr && (_activeModifiers.Contains(0x12) ||
                  ((GetAsyncKeyState(0x12) & 0x8000) != 0) ||
                  ((GetAsyncKeyState(0xA4) & 0x8000) != 0) ||
                  ((GetAsyncKeyState(0xA5) & 0x8000) != 0) ||
                  ((hookFlags & LLKHF_ALTDOWN) != 0));

            shift = _activeModifiers.Contains(0x10) ||
                    ((GetAsyncKeyState(0x10) & 0x8000) != 0) ||
                    ((GetAsyncKeyState(0xA0) & 0x8000) != 0) ||
                    ((GetAsyncKeyState(0xA1) & 0x8000) != 0);

            win = _activeModifiers.Contains(0x5B) ||
                  ((GetAsyncKeyState(0x5B) & 0x8000) != 0) ||
                  ((GetAsyncKeyState(0x5C) & 0x8000) != 0);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vkCode = hookStruct.vkCode;

            // Ignore synthetic dummy and menu-mask key events injected for Start Menu suppression
            if (hookStruct.dwExtraInfo == (UIntPtr)DUMMY_KEY_FLAG ||
                vkCode == VK_DUMMY ||
                vkCode == VK_MENU_MASK)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            int msg = wParam.ToInt32();
            bool isDown = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
            bool isUp = (msg == WM_KEYUP || msg == WM_SYSKEYUP);

            if (isDown || isUp)
            {
                uint normMod = NormalizeModifier(vkCode);
                if (normMod != 0)
                {
                    lock (_lock)
                    {
                        if (isDown) _activeModifiers.Add(normMod);
                        else if (isUp) _activeModifiers.Remove(normMod);
                    }
                }

                // =================================================================================
                // 1. CAPTURE / DETECTION MODE (PowerToys: DetectShortcutUIBackend / DetectSingleRemapKeyUIBackend)
                // =================================================================================
                if (_isCapturingInput || _isCapturingTarget)
                {
                    if (isDown)
                    {
                        // Escape pressed alone cancels capture
                        if (vkCode == 0x1B)
                        {
                            GetCurrentModifierStates(out bool c, out bool a, out bool s, out bool w, hookStruct.flags);
                            if (!c && !a && !s && !w)
                            {
                                CancelCapture();
                                return (IntPtr)1;
                            }
                        }

                        // While capturing, suppress modifier presses and standalone keys completely
                        if (IsModifierKey(vkCode))
                        {
                            if (vkCode == 0x5B || vkCode == 0x5C)
                            {
                                SuppressStartMenu();
                            }
                            return (IntPtr)1; // Swallow modifier during capture so Win menu never triggers
                        }
                        else
                        {
                            // Non-modifier trigger key arrived!
                            GetCurrentModifierStates(out bool ctrl, out bool alt, out bool shift, out bool win, hookStruct.flags);
                            bool hasModifiers = ctrl || alt || shift || win;
                            string displayCombo = FormatKeyCombination(vkCode, ctrl, alt, shift, win);

                            LogMessage?.Invoke($"[Capture] Intercepted Key: vkCode=0x{vkCode:X2} ({FormatKeyName(vkCode)}), ScanCode=0x{hookStruct.scanCode:X2}, Flags=0x{hookStruct.flags:X2}, Modifiers: Ctrl={ctrl}, Alt={alt}, Shift={shift}, Win={win}");

                            if (win)
                            {
                                _suppressNextWinKeyUp = true;
                                SuppressStartMenu();
                            }

                            if (_isCapturingInput)
                            {
                                var recorded = new KeyBindingRule
                                {
                                    VirtualKey = vkCode,
                                    ModifiersCtrl = ctrl,
                                    ModifiersAlt = alt,
                                    ModifiersShift = shift,
                                    ModifiersWin = win,
                                    MappingType = hasModifiers ? KeyMappingType.Shortcut : KeyMappingType.SingleKey,
                                    SuppressOriginalKey = true,
                                    DisplayText = displayCombo
                                };

                                Action<KeyBindingRule>? cb = _captureInputCallback;
                                lock (_lock)
                                {
                                    _isCapturingInput = false;
                                    _captureInputCallback = null;
                                    _captureCancelCallback = null;
                                    _activeModifiers.Clear();
                                }

                                CaptureEnded?.Invoke();
                                if (cb != null)
                                {
                                    ThreadPool.QueueUserWorkItem(_ => cb(recorded));
                                }
                            }
                            else if (_isCapturingTarget)
                            {
                                Action<uint, bool, bool, bool, bool, string>? cb = _captureTargetCallback;
                                lock (_lock)
                                {
                                    _isCapturingTarget = false;
                                    _captureTargetCallback = null;
                                    _captureCancelCallback = null;
                                    _activeModifiers.Clear();
                                }

                                CaptureEnded?.Invoke();
                                if (cb != null)
                                {
                                    ThreadPool.QueueUserWorkItem(_ => cb(vkCode, ctrl, alt, shift, win, displayCombo));
                                }
                            }

                            return (IntPtr)1; // Swallow trigger key
                        }
                    }
                    else if (isUp)
                    {
                        if (vkCode == 0x5B || vkCode == 0x5C)
                        {
                            SuppressStartMenu();
                        }
                        if (normMod != 0)
                        {
                            // Never swallow modifier releases during capture so OS modifier state remains clean
                            return CallNextHookEx(_hookId, nCode, wParam, lParam);
                        }
                        return (IntPtr)1; // Swallow non-modifier keyup during capture
                    }
                }

                // =================================================================================
                // 2. NORMAL EXECUTION MODE (PowerToys Keyboard Event Handlers)
                // =================================================================================
                if (IsRemapEnabled)
                {
                    // A) PHYSICAL MODIFIER KEYS (Win, Ctrl, Alt, Shift):
                    if (normMod != 0)
                    {
                        if (isUp)
                        {
                            // WIN-KEY SUPPRESSION & UNSTUCK MODIFIER INVARIANT:
                            // If a shortcut using Win was invoked, mask the Start Menu right before forwarding Win keyup.
                            // VK_MENU_MASK (0xE8) ensures Windows Explorer clears g_fWinKeyPressed, preventing Start Menu popups 100%.
                            // CRITICAL: We forward the physical Win keyup to Windows OS (NEVER swallow it!).
                            // Swallowing physical modifier keyups is what traps VK_LWIN in Windows OS keystate table.
                            if (_suppressNextWinKeyUp && (vkCode == 0x5B || vkCode == 0x5C))
                            {
                                _suppressNextWinKeyUp = false;
                                SuppressStartMenu();
                                LogMessage?.Invoke("[Remap] Masked Start Menu before forwarding physical Win keyup.");
                            }

                            lock (_lock)
                            {
                                if (_activeModifiers.Count == 0 && _pressedKeys.Count == 0)
                                {
                                    _isShortcutInvoked = false;
                                    _invokedRule = null;
                                }
                            }
                        }

                        // Pass normal modifier events to Windows
                        return CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    // B) NON-MODIFIER ACTION / TRIGGER KEYS:
                    if (isDown)
                    {
                        // Autorepeat suppression for currently invoked shortcut trigger key
                        if (_isShortcutInvoked && _invokedRule != null && vkCode == _invokedRule.VirtualKey)
                        {
                            return (IntPtr)1;
                        }

                        GetCurrentModifierStates(out bool ctrl, out bool alt, out bool shift, out bool win, hookStruct.flags);
                        bool hasModifiers = ctrl || alt || shift || win;
                        KeyBindingRule? matchedRule = null;

                        lock (_lock)
                        {
                            foreach (var rule in Bindings)
                            {
                                if (rule.IsEmpty) continue;

                                bool match = false;
                                if (rule.MappingType == KeyMappingType.Shortcut && hasModifiers)
                                {
                                    match = (rule.VirtualKey == vkCode &&
                                             rule.ModifiersCtrl == ctrl &&
                                             rule.ModifiersAlt == alt &&
                                             rule.ModifiersShift == shift &&
                                             rule.ModifiersWin == win);
                                }
                                else if (rule.MappingType == KeyMappingType.SingleKey && !hasModifiers && !IsTypingKey(vkCode))
                                {
                                    match = (rule.VirtualKey == vkCode);
                                }

                                if (match)
                                {
                                    matchedRule = rule;
                                    break;
                                }
                            }
                        }

                        if (matchedRule != null)
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

                            _isShortcutInvoked = true;
                            _invokedRule = matchedRule;

                            // If this shortcut involved the Windows key, activate suppression
                            if (matchedRule.ModifiersWin || win)
                            {
                                _suppressNextWinKeyUp = true;
                                SuppressStartMenu();

                                // In PowerToys HandleShortcutRemapEvent:
                                // Inject synthetic Win key-up immediately so Windows OS modifier state clears Win!
                                var winRelease = new INPUT[2];
                                winRelease[0] = CreateKeyInput(0x5B, KEYEVENTF_KEYUP, DUMMY_KEY_FLAG);
                                winRelease[1] = CreateKeyInput(0x5C, KEYEVENTF_KEYUP, DUMMY_KEY_FLAG);
                                SendInput(2, winRelease, Marshal.SizeOf<INPUT>());
                            }

                            // Inject remapped keystrokes if configured
                            if (matchedRule.SendRemapKeys && matchedRule.HasRemap)
                            {
                                InjectShortcut(
                                    matchedRule.RemapVirtualKey,
                                    matchedRule.RemapModifiersCtrl,
                                    matchedRule.RemapModifiersAlt,
                                    matchedRule.RemapModifiersShift,
                                    matchedRule.RemapModifiersWin);
                            }

                            // Trigger action if not purely remapped
                            if (firstPress && matchedRule.Action != KeyRemapAction.None)
                            {
                                var act = matchedRule.Action;
                                ThreadPool.QueueUserWorkItem(_ => ActionTriggered?.Invoke(act, KeyPressState.Down));
                            }

                            LogMessage?.Invoke($"[Remap] Triggered: '{matchedRule.DisplayText}' ➔ Remapped: '{matchedRule.RemapDisplayText}' ➔ Action: {matchedRule.Action} (Win Suppressed: {_suppressNextWinKeyUp})");
                            RemapExecuted?.Invoke(matchedRule.DisplayText, matchedRule.RemapDisplayText, matchedRule.Action.ToString());

                            if (matchedRule.SuppressOriginalKey)
                            {
                                return (IntPtr)1; // Swallow original trigger key
                            }
                        }
                    }
                    else if (isUp)
                    {
                        bool wasPressed = false;
                        KeyBindingRule? ruleToRelease = null;

                        lock (_lock)
                        {
                            if (_pressedKeys.Contains(vkCode))
                            {
                                _pressedKeys.Remove(vkCode);
                                wasPressed = true;
                                ruleToRelease = _invokedRule;
                            }

                            if (_activeModifiers.Count == 0 && _pressedKeys.Count == 0)
                            {
                                _isShortcutInvoked = false;
                                _invokedRule = null;
                            }
                        }

                        if (wasPressed && ruleToRelease != null)
                        {
                            if (ruleToRelease.Action != KeyRemapAction.None)
                            {
                                var act = ruleToRelease.Action;
                                ThreadPool.QueueUserWorkItem(_ => ActionTriggered?.Invoke(act, KeyPressState.Up));
                            }

                            if (ruleToRelease.SuppressOriginalKey)
                            {
                                return (IntPtr)1; // Swallow trigger keyup
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
            0 => "None",
            0x83 => "Dedicated Mic Key",
            0xAD => "Mute Key",
            0xAE => "Volume Down",
            0xAF => "Volume Up",
            0x13 => "Pause",
            0x2C => "Print Screen",
            0x91 => "Scroll Lock",
            0x20 => "Space",
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x14 => "Caps Lock",
            0x1B => "Esc",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "Page Up",
            0x22 => "Page Down",
            >= 0x70 and <= 0x87 => $"F{vkCode - 0x6F}", // F1 - F24
            >= 0x30 and <= 0x39 => ((char)vkCode).ToString(), // 0 - 9
            >= 0x41 and <= 0x5A => ((char)vkCode).ToString(), // A - Z
            >= 0x60 and <= 0x69 => $"Num {vkCode - 0x60}",     // Numpad 0-9
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
            _ => $"VK 0x{vkCode:X2}"
        };
    }

    public static string FormatKeyCombination(uint vk, bool ctrl, bool alt, bool shift, bool win)
    {
        if (vk == 0) return "None";

        var parts = new List<string>();
        if (win) parts.Add("Win");
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");

        parts.Add(FormatKeyName(vk));
        return string.Join("+", parts);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            UninstallHook();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
