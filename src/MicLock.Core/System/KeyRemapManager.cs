using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MicLock.Core.Sys;

public enum KeyPressState
{
    Down,
    Up
}

public class KeyRemapManager : IDisposable
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    // Microsoft PowerToys Standard Flags & Constants (from KeyboardManagerConstants.h)
    public const uint DUMMY_KEY = 0xFF; // VK_OEM_CLEAR / unassigned dummy key
    public const uint KEYBOARDMANAGER_SHORTCUT_FLAG = 0x101; // Marker flag to prevent hook recursion
    public const uint INJECTED_KEY_FLAG = 0x101; // Backward compatibility alias

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

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
        public KEYBDINPUT ki;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    public static Action<INPUT[]>? OnSendInputSent { get; set; }
    public static Func<uint, INPUT[], int, uint>? SendInputOverride { get; set; }

    private static uint SafeSendInput(uint nInputs, INPUT[] pInputs, int cbSize)
    {
        OnSendInputSent?.Invoke(pInputs);
        if (SendInputOverride != null)
        {
            return SendInputOverride(nInputs, pInputs, cbSize);
        }
        return SendInput(nInputs, pInputs, cbSize);
    }

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
    private readonly LowLevelKeyboardProc _hookCallbackDelegate;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _disposed;

    // PowerToys Shortcut State Machine
    private bool _isShortcutInvoked;
    private KeyBindingRule? _invokedRule;
    private uint _invokedWinKey; // Stores whether physical VK_LWIN (0x5B) or VK_RWIN (0x5C) was pressed
    private uint _heldTriggerKey; // Tracks physical trigger action key to swallow trailing keyup

    // UI Key Detection / Capture Mode State (PowerToys Direct State Tracking)
    private bool _isCapturingInput;
    private bool _isCapturingTarget;
    private readonly HashSet<uint> _capturingModifiers = new();
    private Action<KeyBindingRule>? _captureInputCallback;
    private Action<uint, bool, bool, bool, bool, string>? _captureTargetCallback;
    private Action? _captureCancelCallback;

    public bool IsHookInstalled => _hookId != IntPtr.Zero;
    public bool IsRemapEnabled { get; set; } = true;
    public bool IsCapturing => _isCapturingInput || _isCapturingTarget;

    public List<KeyBindingRule> Bindings { get; set; } = new();

    public event Action<KeyRemapAction, KeyPressState>? ActionTriggered;
    public event Action<string, string, string>? RemapExecuted;
    public event Action<string>? LogMessage;
    public event Action? CaptureStarted;
    public event Action? CaptureEnded;
    public event Action<string>? CapturePreviewChanged;

    public KeyRemapManager(IEnumerable<KeyBindingRule>? initialRules = null, bool installHook = true)
    {
        _hookCallbackDelegate = HookCallback;

        if (initialRules != null)
        {
            foreach (var rule in initialRules)
            {
                Bindings.Add(rule.Clone());
            }
        }
        else
        {
            Bindings = KeyBindingRule.CreateDefaultRules();
        }

        if (installHook)
        {
            InstallHook();
        }
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
                if (hMod == IntPtr.Zero)
                {
                    hMod = GetModuleHandle(null);
                }

                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookCallbackDelegate, hMod, 0);
                if (_hookId != IntPtr.Zero)
                {
                    LogMessage?.Invoke("[Hook] Low-level keyboard hook successfully installed.");
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"[Hook] Failed to install keyboard hook: {ex.Message}");
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
                _isShortcutInvoked = false;
                _invokedRule = null;
                _invokedWinKey = 0;
                _heldTriggerKey = 0;
                LogMessage?.Invoke("[Hook] Keyboard hook uninstalled.");
            }
        }
    }

    public void StartCaptureInput(Action<KeyBindingRule> onCaptured, Action? onCancelled = null)
    {
        lock (_lock)
        {
            _capturingModifiers.Clear();
            _isCapturingInput = true;
            _isCapturingTarget = false;
            _captureInputCallback = onCaptured;
            _captureCancelCallback = onCancelled;
        }
        CaptureStarted?.Invoke();
        CapturePreviewChanged?.Invoke("Press key... (Esc to cancel)");
    }

    public void StartCaptureTarget(Action<uint, bool, bool, bool, bool, string> onCaptured, Action? onCancelled = null)
    {
        lock (_lock)
        {
            _capturingModifiers.Clear();
            _isCapturingTarget = true;
            _isCapturingInput = false;
            _captureTargetCallback = onCaptured;
            _captureCancelCallback = onCancelled;
        }
        CaptureStarted?.Invoke();
        CapturePreviewChanged?.Invoke("Press keys... (Esc to cancel)");
    }

    public void CancelCapture()
    {
        Action? cancelCb;
        bool hadModifiers;
        lock (_lock)
        {
            if (!_isCapturingInput && !_isCapturingTarget) return;
            _isCapturingInput = false;
            _isCapturingTarget = false;
            hadModifiers = _capturingModifiers.Count > 0;
            _capturingModifiers.Clear();
            _captureInputCallback = null;
            _captureTargetCallback = null;
            cancelCb = _captureCancelCallback;
            _captureCancelCallback = null;
        }

        if (hadModifiers)
        {
            SuppressStartMenu();
        }

        cancelCb?.Invoke();
        CaptureEnded?.Invoke();
    }

    private string GetCapturingModifiersPreview()
    {
        bool ctrl, alt, shift, win;
        lock (_lock)
        {
            ctrl = _capturingModifiers.Contains(0x11) || _capturingModifiers.Contains(0xA2) || _capturingModifiers.Contains(0xA3);
            alt = _capturingModifiers.Contains(0x12) || _capturingModifiers.Contains(0xA4) || _capturingModifiers.Contains(0xA5);
            shift = _capturingModifiers.Contains(0x10) || _capturingModifiers.Contains(0xA0) || _capturingModifiers.Contains(0xA1);
            win = _capturingModifiers.Contains(0x5B) || _capturingModifiers.Contains(0x5C);
        }

        var parts = new List<string>();
        if (win) parts.Add("Win");
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");

        if (parts.Count == 0)
        {
            return _isCapturingInput ? "Press key... (Esc to cancel)" : "Press keys... (Esc to cancel)";
        }

        return string.Join("+", parts) + "+...";
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

    public static bool IsModifierKey(uint vk) => NormalizeModifier(vk) != 0;

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

    public static bool IsAltGr()
    {
        short rMenu = GetAsyncKeyState(0xA5); // VK_RMENU
        short lMenu = GetAsyncKeyState(0xA4); // VK_LMENU
        short lCtrl = GetAsyncKeyState(0xA2); // VK_LCONTROL
        short rCtrl = GetAsyncKeyState(0xA3); // VK_RCONTROL

        return (rMenu & 0x8000) != 0 && (lCtrl & 0x8000) != 0 && (lMenu & 0x8000) == 0 && (rCtrl & 0x8000) == 0;
    }

    public static Func<uint, bool>? PhysicalKeyStateOverride { get; set; }

    public static bool IsPhysicalKeyDown(uint vk)
    {
        if (PhysicalKeyStateOverride != null)
        {
            return PhysicalKeyStateOverride(vk);
        }
        return (GetAsyncKeyState((int)vk) & 0x8000) != 0;
    }

    public static bool AreAnyModifiersPhysicallyHeld()
    {
        return IsPhysicalKeyDown(0x5B) || IsPhysicalKeyDown(0x5C) ||
               IsPhysicalKeyDown(0x11) || IsPhysicalKeyDown(0xA2) || IsPhysicalKeyDown(0xA3) ||
               IsPhysicalKeyDown(0x12) || IsPhysicalKeyDown(0xA4) || IsPhysicalKeyDown(0xA5) ||
               IsPhysicalKeyDown(0x10) || IsPhysicalKeyDown(0xA0) || IsPhysicalKeyDown(0xA1);
    }

    public static void GetPhysicalModifiers(out bool ctrl, out bool alt, out bool shift, out bool win)
    {
        bool isAltGr = IsAltGr();
        ctrl = !isAltGr && (IsPhysicalKeyDown(0x11) || IsPhysicalKeyDown(0xA2) || IsPhysicalKeyDown(0xA3));
        alt = IsPhysicalKeyDown(0x12) || IsPhysicalKeyDown(0xA4) || IsPhysicalKeyDown(0xA5);
        shift = IsPhysicalKeyDown(0x10) || IsPhysicalKeyDown(0xA0) || IsPhysicalKeyDown(0xA1);
        win = IsPhysicalKeyDown(0x5B) || IsPhysicalKeyDown(0x5C);
    }

    // PowerToys Helpers::IsExtendedKey (Helpers.cpp line 134) & Win32 Extended Keys
    // VK_LWIN (0x5B) and VK_RWIN (0x5C) MUST NOT have KEYEVENTF_EXTENDEDKEY (matches PowerToys Helpers.cpp lines 134-170)
    public static bool IsExtendedKey(ushort vk)
    {
        return vk switch
        {
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
            0x03 => true,         // VK_CANCEL
            0x5F => true,         // VK_SLEEP
            0xB0 or 0xB1 or 0xB2 or 0xB3 => true, // VK_MEDIA_NEXT_TRACK, VK_MEDIA_PREV_TRACK, VK_MEDIA_STOP, VK_MEDIA_PLAY_PAUSE
            0xAD or 0xAE or 0xAF => true, // VK_VOLUME_MUTE, VK_VOLUME_DOWN, VK_VOLUME_UP
            0xB4 or 0xB5 or 0xB6 or 0xB7 => true, // VK_LAUNCH_MAIL, VK_LAUNCH_MEDIA_SELECT, VK_LAUNCH_APP1, VK_LAUNCH_APP2
            _ => false
        };
    }

    private static INPUT CreateKeyInput(ushort vk, uint dwFlags, uint extraInfo)
    {
        uint flags = dwFlags;
        if (IsExtendedKey(vk)) flags |= KEYEVENTF_EXTENDEDKEY;

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

    // PowerToys Helpers::SetDummyKeyEvent (Helpers.cpp line 199)
    private static void AppendDummyKey(List<INPUT> list, uint extraInfo = KEYBOARDMANAGER_SHORTCUT_FLAG)
    {
        list.Add(CreateKeyInput((ushort)DUMMY_KEY, 0, extraInfo));
        list.Add(CreateKeyInput((ushort)DUMMY_KEY, KEYEVENTF_KEYUP, extraInfo));
    }

    // Microsoft PowerToys Standard Shell Disarm
    public static void SuppressStartMenu()
    {
        try
        {
            var list = new List<INPUT>();
            AppendDummyKey(list, KEYBOARDMANAGER_SHORTCUT_FLAG);
            if (list.Count > 0)
            {
                SafeSendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>());
            }
        }
        catch { }
    }

    /// <summary>
    /// Injects target remapped shortcut into Windows with KEYBOARDMANAGER_SHORTCUT_FLAG (0x101).
    /// </summary>
    public static void InjectShortcut(uint vk, bool ctrl, bool alt, bool shift, bool win, uint extraInfo = KEYBOARDMANAGER_SHORTCUT_FLAG)
    {
        if (vk == 0) return;

        try
        {
            var list = new List<INPUT>();

            // Modifiers down in forward order
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

            SafeSendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>());
        }
        catch { }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            IntPtr result = ProcessKeyboardEvent(nCode, wParam, hookStruct.vkCode, hookStruct.flags, hookStruct.dwExtraInfo);
            if (result != IntPtr.Zero)
            {
                return result; // Swallowed
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public IntPtr ProcessKeyboardEvent(int msg, uint vkCode, uint flags = 0, UIntPtr dwExtraInfo = default)
    {
        return ProcessKeyboardEvent(0, (IntPtr)msg, vkCode, flags, dwExtraInfo);
    }

    public IntPtr ProcessKeyboardEvent(int nCode, IntPtr wParam, uint vkCode, uint flags = 0, UIntPtr dwExtraInfo = default)
    {
        if (nCode >= 0)
        {
            // PowerToys Invariant: Ignore all events injected by Keyboard Manager to prevent recursion
            if ((dwExtraInfo.ToUInt64() & 1) != 0 ||
                dwExtraInfo == (UIntPtr)KEYBOARDMANAGER_SHORTCUT_FLAG ||
                vkCode == DUMMY_KEY)
            {
                return IntPtr.Zero;
            }

            int msg = wParam.ToInt32();
            bool isDown = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
            bool isUp = (msg == WM_KEYUP || msg == WM_SYSKEYUP);

            if (isDown || isUp)
            {
                // =================================================================================
                // 1. CAPTURE / DETECTION MODE (PowerToys Direct State Tracking)
                // =================================================================================
                if (_isCapturingInput || _isCapturingTarget)
                {
                    if (isDown)
                    {
                        // Escape pressed alone (with no modifiers) cancels capture immediately
                        if (vkCode == 0x1B && _capturingModifiers.Count == 0)
                        {
                            CancelCapture();
                            return (IntPtr)1; // Pure suppression
                        }

                        if (IsModifierKey(vkCode))
                        {
                            lock (_lock)
                            {
                                _capturingModifiers.Add(vkCode);
                                uint norm = NormalizeModifier(vkCode);
                                if (norm != 0) _capturingModifiers.Add(norm);
                            }

                            string preview = GetCapturingModifiersPreview();
                            CapturePreviewChanged?.Invoke(preview);
                            return (IntPtr)1; // Pure suppression of modifier keydown
                        }
                        else
                        {
                            // Trigger action key detected! Record complete shortcut immediately using _capturingModifiers
                            bool ctrl, alt, shift, win;
                            lock (_lock)
                            {
                                ctrl = _capturingModifiers.Contains(0x11) || _capturingModifiers.Contains(0xA2) || _capturingModifiers.Contains(0xA3);
                                alt = _capturingModifiers.Contains(0x12) || _capturingModifiers.Contains(0xA4) || _capturingModifiers.Contains(0xA5);
                                shift = _capturingModifiers.Contains(0x10) || _capturingModifiers.Contains(0xA0) || _capturingModifiers.Contains(0xA1);
                                win = _capturingModifiers.Contains(0x5B) || _capturingModifiers.Contains(0x5C);
                            }

                            bool hasModifiers = ctrl || alt || shift || win;
                            string displayCombo = FormatKeyCombination(vkCode, ctrl, alt, shift, win);

                            Action<KeyBindingRule>? cbInput;
                            Action<uint, bool, bool, bool, bool, string>? cbTarget;
                            bool isInput;

                            lock (_lock)
                            {
                                isInput = _isCapturingInput;
                                cbInput = _captureInputCallback;
                                cbTarget = _captureTargetCallback;

                                _isCapturingInput = false;
                                _isCapturingTarget = false;
                                _capturingModifiers.Clear();
                                _captureInputCallback = null;
                                _captureTargetCallback = null;
                                _captureCancelCallback = null;
                            }

                            // PowerToys disarms Start Menu with Dummy Key 0xFF down+up
                            SuppressStartMenu();

                            CaptureEnded?.Invoke();

                            if (isInput && cbInput != null)
                            {
                                var rule = new KeyBindingRule
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
                                ThreadPool.QueueUserWorkItem(_ => cbInput(rule));
                            }
                            else if (!isInput && cbTarget != null)
                            {
                                ThreadPool.QueueUserWorkItem(_ => cbTarget(vkCode, ctrl, alt, shift, win, displayCombo));
                            }

                            return (IntPtr)1; // Pure suppression of trigger keydown
                        }
                    }
                    else if (isUp)
                    {
                        if (IsModifierKey(vkCode))
                        {
                            lock (_lock)
                            {
                                _capturingModifiers.Remove(vkCode);
                                uint norm = NormalizeModifier(vkCode);
                                if (norm == 0x11 && !_capturingModifiers.Contains(0xA2) && !_capturingModifiers.Contains(0xA3))
                                    _capturingModifiers.Remove(0x11);
                                else if (norm == 0x12 && !_capturingModifiers.Contains(0xA4) && !_capturingModifiers.Contains(0xA5))
                                    _capturingModifiers.Remove(0x12);
                                else if (norm == 0x10 && !_capturingModifiers.Contains(0xA0) && !_capturingModifiers.Contains(0xA1))
                                    _capturingModifiers.Remove(0x10);
                                else if (norm == 0x5B && !_capturingModifiers.Contains(0x5B) && !_capturingModifiers.Contains(0x5C))
                                    _capturingModifiers.Remove(0x5B);
                            }

                            string preview = GetCapturingModifiersPreview();
                            CapturePreviewChanged?.Invoke(preview);
                        }

                        return (IntPtr)1; // Suppress all keyups during capture
                    }
                }

                // =================================================================================
                // 2. NORMAL EXECUTION MODE (PowerToys KeyboardEventHandlers.cpp lines 633-1413)
                // =================================================================================
                if (IsRemapEnabled)
                {
                    bool isModifier = IsModifierKey(vkCode);

                    // A) IF SHORTCUT IS ALREADY INVOKED:
                    if (_isShortcutInvoked && _invokedRule != null)
                    {
                        // Case 1: Modifier released (PowerToys lines 1002-1074)
                        if (isUp && isModifier)
                        {
                            bool matchesInvokedModifier =
                                (_invokedRule.ModifiersWin && (vkCode == 0x5B || vkCode == 0x5C)) ||
                                (_invokedRule.ModifiersCtrl && (vkCode == 0x11 || vkCode == 0xA2 || vkCode == 0xA3)) ||
                                (_invokedRule.ModifiersAlt && (vkCode == 0x12 || vkCode == 0xA4 || vkCode == 0xA5)) ||
                                (_invokedRule.ModifiersShift && (vkCode == 0x10 || vkCode == 0xA0 || vkCode == 0xA1));

                            if (matchesInvokedModifier)
                            {
                                var releaseBatch = new List<INPUT>();

                                // Release target shortcut keys if they were sent
                                if (_invokedRule.SendRemapKeys && _invokedRule.HasRemap)
                                {
                                    // If trigger key was still down when modifier released, release action key first
                                    if (_heldTriggerKey != 0)
                                    {
                                        releaseBatch.Add(CreateKeyInput((ushort)_invokedRule.RemapVirtualKey, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                    }

                                    if (_invokedRule.RemapModifiersWin) releaseBatch.Add(CreateKeyInput(0x5B, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                    if (_invokedRule.RemapModifiersAlt) releaseBatch.Add(CreateKeyInput(0x12, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                    if (_invokedRule.RemapModifiersShift) releaseBatch.Add(CreateKeyInput(0x10, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                    if (_invokedRule.RemapModifiersCtrl) releaseBatch.Add(CreateKeyInput(0x11, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                }

                                // PowerToys: Send Dummy Key (0xFF down + up) to disarm Explorer Start Menu
                                AppendDummyKey(releaseBatch, KEYBOARDMANAGER_SHORTCUT_FLAG);

                                // Explicitly release the physical modifier to Windows kernel
                                ushort modToRelease = (ushort)vkCode;
                                if (modToRelease == 0x5B || modToRelease == 0x5C || modToRelease == 0x11 || modToRelease == 0x12 || modToRelease == 0x10 ||
                                    modToRelease == 0xA0 || modToRelease == 0xA1 || modToRelease == 0xA2 || modToRelease == 0xA3 || modToRelease == 0xA4 || modToRelease == 0xA5)
                                {
                                    releaseBatch.Add(CreateKeyInput(modToRelease, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                }

                                if (releaseBatch.Count > 0)
                                {
                                    SafeSendInput((uint)releaseBatch.Count, releaseBatch.ToArray(), Marshal.SizeOf<INPUT>());
                                }

                                lock (_lock)
                                {
                                    _isShortcutInvoked = false;
                                    _invokedRule = null;
                                    _invokedWinKey = 0;
                                    _heldTriggerKey = 0;
                                }

                                // SWALLOW the physical modifier key-up (PowerToys line 1073)!
                                return (IntPtr)1;
                            }
                        }

                        // Case 2: Autorepeat of action key while held (PowerToys line 1079)
                        if (isDown && vkCode == _invokedRule.VirtualKey)
                        {
                            return (IntPtr)1; // Suppress repeated down
                        }

                        // Case 3: Action key released (PowerToys lines 1113-1201)
                        if (isUp && vkCode == _invokedRule.VirtualKey)
                        {
                            _heldTriggerKey = 0;

                            if (_invokedRule.SendRemapKeys && _invokedRule.HasRemap)
                            {
                                var targetUp = new INPUT[1];
                                targetUp[0] = CreateKeyInput((ushort)_invokedRule.RemapVirtualKey, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG);
                                SafeSendInput(1, targetUp, Marshal.SizeOf<INPUT>());
                            }

                            if (_invokedRule.Action != KeyRemapAction.None)
                            {
                                var act = _invokedRule.Action;
                                ThreadPool.QueueUserWorkItem(_ => ActionTriggered?.Invoke(act, KeyPressState.Up));
                            }

                            // For single-key rules (no modifiers), the gesture ends here
                            if (!_invokedRule.HasModifiers)
                            {
                                lock (_lock)
                                {
                                    _isShortcutInvoked = false;
                                    _invokedRule = null;
                                }
                            }

                            // Target modifiers remain held until physical modifier is released!
                            return (IntPtr)1; // Swallow physical action keyup
                        }

                        // Case 5: Any other key pressed while shortcut is invoked (PowerToys lines 1225-1380)
                        if (isDown && !isModifier && vkCode != _invokedRule.VirtualKey)
                        {
                            var revertBatch = new List<INPUT>();

                            // 1. Release target action key and target modifiers if remapped
                            if (_invokedRule.SendRemapKeys && _invokedRule.HasRemap)
                            {
                                revertBatch.Add(CreateKeyInput((ushort)_invokedRule.RemapVirtualKey, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                if (_invokedRule.RemapModifiersWin) revertBatch.Add(CreateKeyInput(0x5B, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                if (_invokedRule.RemapModifiersAlt) revertBatch.Add(CreateKeyInput(0x12, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                if (_invokedRule.RemapModifiersShift) revertBatch.Add(CreateKeyInput(0x10, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                if (_invokedRule.RemapModifiersCtrl) revertBatch.Add(CreateKeyInput(0x11, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                            }

                            // 2. ONLY re-assert physical modifiers to OS if they are ACTUALLY STILL PHYSICALLY HELD!
                            if (_invokedRule.ModifiersWin)
                            {
                                ushort winVk = (ushort)(_invokedWinKey != 0 ? _invokedWinKey : 0x5B);
                                if (IsPhysicalKeyDown(winVk) || IsPhysicalKeyDown(0x5B) || IsPhysicalKeyDown(0x5C))
                                {
                                    revertBatch.Add(CreateKeyInput(winVk, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                }
                                else
                                {
                                    // Physical Win key was already released! Send explicit key-up to ensure Windows modifier state is clean.
                                    revertBatch.Add(CreateKeyInput(winVk, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                }
                            }
                            if (_invokedRule.ModifiersCtrl)
                            {
                                if (IsPhysicalKeyDown(0x11) || IsPhysicalKeyDown(0xA2) || IsPhysicalKeyDown(0xA3))
                                    revertBatch.Add(CreateKeyInput(0x11, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                else
                                    revertBatch.Add(CreateKeyInput(0x11, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                            }
                            if (_invokedRule.ModifiersAlt)
                            {
                                if (IsPhysicalKeyDown(0x12) || IsPhysicalKeyDown(0xA4) || IsPhysicalKeyDown(0xA5))
                                    revertBatch.Add(CreateKeyInput(0x12, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                else
                                    revertBatch.Add(CreateKeyInput(0x12, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                            }
                            if (_invokedRule.ModifiersShift)
                            {
                                if (IsPhysicalKeyDown(0x10) || IsPhysicalKeyDown(0xA0) || IsPhysicalKeyDown(0xA1))
                                    revertBatch.Add(CreateKeyInput(0x10, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                else
                                    revertBatch.Add(CreateKeyInput(0x10, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                            }

                            // 3. Re-inject the new key (extraInfo = 0 so OS processes it as natural input)
                            revertBatch.Add(CreateKeyInput((ushort)vkCode, 0, 0));

                            SafeSendInput((uint)revertBatch.Count, revertBatch.ToArray(), Marshal.SizeOf<INPUT>());

                            lock (_lock)
                            {
                                _isShortcutInvoked = false;
                                _invokedRule = null;
                                _invokedWinKey = 0;
                                _heldTriggerKey = 0;
                            }

                            return (IntPtr)1; // Swallow raw event since we re-injected it cleanly
                        }
                    }

                    // Trailing release of physical trigger key after modifier already released
                    if (isUp && _heldTriggerKey != 0 && vkCode == _heldTriggerKey)
                    {
                        _heldTriggerKey = 0;
                        return (IntPtr)1; // Swallow trailing keyup
                    }

                    // B) IF NOT INVOKED: DETECT SHORTCUT MATCH (PowerToys lines 679-725):
                    if (isDown && !isModifier)
                    {
                        GetPhysicalModifiers(out bool ctrl, out bool alt, out bool shift, out bool win);
                        KeyBindingRule? matchedRule = null;

                        lock (_lock)
                        {
                            foreach (var rule in Bindings)
                            {
                                if (rule.IsEmpty) continue;

                                if (rule.MappingType == KeyMappingType.Shortcut)
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
                                else if (rule.MappingType == KeyMappingType.SingleKey && !ctrl && !alt && !shift && !win && !IsTypingKey(vkCode))
                                {
                                    if (rule.VirtualKey == vkCode)
                                    {
                                        matchedRule = rule;
                                        break;
                                    }
                                }
                            }
                        }

                        if (matchedRule != null)
                        {
                            uint activeWinVk = IsPhysicalKeyDown(0x5C) ? 0x5Cu : (IsPhysicalKeyDown(0x5B) ? 0x5Bu : (win ? 0x5Bu : 0u));

                            lock (_lock)
                            {
                                _isShortcutInvoked = true;
                                _invokedRule = matchedRule;
                                _invokedWinKey = activeWinVk;
                                _heldTriggerKey = vkCode;
                            }

                            var batch = new List<INPUT>();

                            // 1. PowerToys Step: Send Dummy Key (0xFF down + up) BEFORE releasing source modifiers!
                            AppendDummyKey(batch, KEYBOARDMANAGER_SHORTCUT_FLAG);

                            // 2. PowerToys Step: Release source modifiers in reverse order (Shift, Alt, Ctrl, Win)
                            if (shift) batch.Add(CreateKeyInput(0x10, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                            if (alt) batch.Add(CreateKeyInput(0x12, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                            if (ctrl) batch.Add(CreateKeyInput(0x11, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                            if (win)
                            {
                                ushort w = (ushort)(activeWinVk != 0 ? activeWinVk : 0x5B);
                                batch.Add(CreateKeyInput(w, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                            }

                            // 3. PowerToys Step: Inject target modifiers down in forward order (Win, Ctrl, Alt, Shift)
                            if (matchedRule.SendRemapKeys && matchedRule.HasRemap)
                            {
                                if (matchedRule.RemapModifiersWin) batch.Add(CreateKeyInput(0x5B, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                if (matchedRule.RemapModifiersCtrl) batch.Add(CreateKeyInput(0x11, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                if (matchedRule.RemapModifiersAlt) batch.Add(CreateKeyInput(0x12, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                if (matchedRule.RemapModifiersShift) batch.Add(CreateKeyInput(0x10, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));

                                // 4. Inject target action key down
                                batch.Add(CreateKeyInput((ushort)matchedRule.RemapVirtualKey, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                            }

                            SafeSendInput((uint)batch.Count, batch.ToArray(), Marshal.SizeOf<INPUT>());

                            // Fire internal action if configured
                            if (matchedRule.Action != KeyRemapAction.None)
                            {
                                var act = matchedRule.Action;
                                ThreadPool.QueueUserWorkItem(_ => ActionTriggered?.Invoke(act, KeyPressState.Down));
                            }

                            LogMessage?.Invoke($"[Remap] Triggered '{matchedRule.DisplayText}' ➔ Remapped '{matchedRule.RemapDisplayText}' ➔ Action: {matchedRule.Action}");
                            RemapExecuted?.Invoke(matchedRule.DisplayText, matchedRule.RemapDisplayText, matchedRule.Action.ToString());

                            return (IntPtr)1; // Swallow physical trigger keydown
                        }
                    }
                }
            }
        }

        // Default: Forward unmodified to Windows kernel
        return IntPtr.Zero;
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
