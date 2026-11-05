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

    // Full 64-bit compliant 40-byte Win32 INPUT structure
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
        uint result = SendInput(nInputs, pInputs, cbSize);
        if (result == 0 && nInputs > 0)
        {
            int err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"[KeyRemapManager] SendInput failed. Inputs={nInputs}, cbSize={cbSize}, Error={err}");
        }
        return result;
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
    private uint _heldRemapTargetKey; // Tracks physical remap target key (Dual-Trigger) to swallow trailing keyup

    // UI Key Detection / Capture Mode State (PowerToys Direct State Tracking)
    private bool _isCapturingInput;
    private bool _isCapturingTarget;
    private readonly HashSet<uint> _capturingModifiers = new();
    private uint _capturedActionKey;
    private KeyBindingRule? _lastCapturedRule;
    private (uint vk, bool ctrl, bool alt, bool shift, bool win, string display)? _lastCapturedTarget;
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
                _heldRemapTargetKey = 0;
                LogMessage?.Invoke("[Hook] Keyboard hook uninstalled.");
            }
        }
    }

    public void StartCaptureInput(Action<KeyBindingRule> onCaptured, Action? onCancelled = null)
    {
        lock (_lock)
        {
            _capturingModifiers.Clear();
            _capturedActionKey = 0;
            _lastCapturedRule = null;
            _isCapturingInput = true;
            _isCapturingTarget = false;
            _captureInputCallback = onCaptured;
            _captureCancelCallback = onCancelled;
        }
        CaptureStarted?.Invoke();
        CapturePreviewChanged?.Invoke("Press key or shortcut... (Esc to cancel)");
    }

    public void StartCaptureTarget(Action<uint, bool, bool, bool, bool, string> onCaptured, Action? onCancelled = null)
    {
        lock (_lock)
        {
            _capturingModifiers.Clear();
            _capturedActionKey = 0;
            _lastCapturedTarget = null;
            _isCapturingTarget = true;
            _isCapturingInput = false;
            _captureTargetCallback = onCaptured;
            _captureCancelCallback = onCancelled;
        }
        CaptureStarted?.Invoke();
        CapturePreviewChanged?.Invoke("Press target shortcut... (Esc to cancel)");
    }

    public void CancelCapture()
    {
        Action? cancelCb;
        lock (_lock)
        {
            if (!_isCapturingInput && !_isCapturingTarget) return;
            _isCapturingInput = false;
            _isCapturingTarget = false;
            _capturingModifiers.Clear();
            _capturedActionKey = 0;
            _lastCapturedRule = null;
            _lastCapturedTarget = null;
            _captureInputCallback = null;
            _captureTargetCallback = null;
            cancelCb = _captureCancelCallback;
            _captureCancelCallback = null;
        }

        SuppressStartMenu();
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
            return _isCapturingInput ? "Press key or shortcut... (Esc to cancel)" : "Press target keys... (Esc to cancel)";
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

    public static void GetPhysicalModifiers(out bool ctrl, out bool alt, out bool shift, out bool win)
    {
        bool isAltGr = IsAltGr();
        ctrl = !isAltGr && (IsPhysicalKeyDown(0x11) || IsPhysicalKeyDown(0xA2) || IsPhysicalKeyDown(0xA3));
        alt = IsPhysicalKeyDown(0x12) || IsPhysicalKeyDown(0xA4) || IsPhysicalKeyDown(0xA5);
        shift = IsPhysicalKeyDown(0x10) || IsPhysicalKeyDown(0xA0) || IsPhysicalKeyDown(0xA1);
        win = IsPhysicalKeyDown(0x5B) || IsPhysicalKeyDown(0x5C);
    }

    // PowerToys Helpers::IsExtendedKey (Helpers.cpp line 134-177)
    // IMPORTANT: VK_LWIN (0x5B) and VK_RWIN (0x5C) MUST NOT have KEYEVENTF_EXTENDEDKEY
    public static bool IsExtendedKey(ushort vk)
    {
        return vk switch
        {
            0xA3 => true,         // VK_RCONTROL
            0xA5 => true,         // VK_RMENU
            0x90 => true,         // VK_NUMLOCK
            0x2C => true,         // VK_SNAPSHOT
            0x03 => true,         // VK_CANCEL
            0x2D or 0x2E => true, // VK_INSERT, VK_DELETE
            0x24 or 0x23 => true, // VK_HOME, VK_END
            0x21 or 0x22 => true, // VK_PRIOR, VK_NEXT
            0x25 or 0x26 or 0x27 or 0x28 => true, // VK_LEFT, VK_UP, VK_RIGHT, VK_DOWN
            0x5F => true,         // VK_SLEEP
            0xB0 or 0xB1 or 0xB2 or 0xB3 => true, // VK_MEDIA_NEXT_TRACK, VK_MEDIA_PREV_TRACK, VK_MEDIA_STOP, VK_MEDIA_PLAY_PAUSE
            0xAD or 0xAE or 0xAF => true, // VK_VOLUME_MUTE, VK_VOLUME_DOWN, VK_VOLUME_UP
            0xB4 or 0xB5 or 0xB6 or 0xB7 => true, // VK_LAUNCH_MAIL, VK_LAUNCH_MEDIA_SELECT, VK_LAUNCH_APP1, VK_LAUNCH_APP2
            0x5D => true,         // VK_APPS
            0x6F => true,         // VK_DIVIDE
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
                // 1. CAPTURE / DETECTION MODE (PowerToys KeyboardManagerState clone)
                // =================================================================================
                if (_isCapturingInput || _isCapturingTarget)
                {
                    // Escape alone cancels capture immediately
                    if (isDown && vkCode == 0x1B && _capturingModifiers.Count == 0 && _capturedActionKey == 0)
                    {
                        CancelCapture();
                        return (IntPtr)1;
                    }

                    if (isDown)
                    {
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
                        }
                        else
                        {
                            // Trigger / action key down detected!
                            bool ctrl, alt, shift, win;
                            lock (_lock)
                            {
                                ctrl = _capturingModifiers.Contains(0x11) || _capturingModifiers.Contains(0xA2) || _capturingModifiers.Contains(0xA3);
                                alt = _capturingModifiers.Contains(0x12) || _capturingModifiers.Contains(0xA4) || _capturingModifiers.Contains(0xA5);
                                shift = _capturingModifiers.Contains(0x10) || _capturingModifiers.Contains(0xA0) || _capturingModifiers.Contains(0xA1);
                                win = _capturingModifiers.Contains(0x5B) || _capturingModifiers.Contains(0x5C);
                                _capturedActionKey = vkCode;
                            }

                            bool hasModifiers = ctrl || alt || shift || win;
                            string displayCombo = FormatKeyCombination(vkCode, ctrl, alt, shift, win);

                            lock (_lock)
                            {
                                if (_isCapturingInput)
                                {
                                    _lastCapturedRule = new KeyBindingRule
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
                                }
                                else if (_isCapturingTarget)
                                {
                                    _lastCapturedTarget = (vkCode, ctrl, alt, shift, win, displayCombo);
                                }
                            }

                            CapturePreviewChanged?.Invoke(displayCombo);
                        }
                    }
                    else if (isUp)
                    {
                        bool isMod = IsModifierKey(vkCode);
                        if (isMod)
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
                        }

                        // Complete capture when action key or all modifiers are released
                        bool shouldComplete = false;
                        KeyBindingRule? ruleToEmit = null;
                        (uint vk, bool ctrl, bool alt, bool shift, bool win, string display)? targetToEmit = null;
                        Action<KeyBindingRule>? cbInput = null;
                        Action<uint, bool, bool, bool, bool, string>? cbTarget = null;

                        lock (_lock)
                        {
                            if (vkCode == _capturedActionKey || (_capturedActionKey != 0 && _capturingModifiers.Count == 0))
                            {
                                shouldComplete = true;
                                ruleToEmit = _lastCapturedRule;
                                targetToEmit = _lastCapturedTarget;
                                cbInput = _captureInputCallback;
                                cbTarget = _captureTargetCallback;

                                _isCapturingInput = false;
                                _isCapturingTarget = false;
                                _capturingModifiers.Clear();
                                _capturedActionKey = 0;
                                _lastCapturedRule = null;
                                _lastCapturedTarget = null;
                                _captureInputCallback = null;
                                _captureTargetCallback = null;
                                _captureCancelCallback = null;
                            }
                        }

                        if (shouldComplete)
                        {
                            // Disarm Start Menu before leaving capture mode
                            SuppressStartMenu();
                            CaptureEnded?.Invoke();

                            if (ruleToEmit != null && cbInput != null)
                            {
                                ThreadPool.QueueUserWorkItem(_ => cbInput(ruleToEmit));
                            }
                            else if (targetToEmit.HasValue && cbTarget != null)
                            {
                                var t = targetToEmit.Value;
                                ThreadPool.QueueUserWorkItem(_ => cbTarget(t.vk, t.ctrl, t.alt, t.shift, t.win, t.display));
                            }
                        }
                    }

                    // 100% SUPPRESSION WHILE IN CAPTURE MODE (PowerToys rule)
                    return (IntPtr)1;
                }

                // =================================================================================
                // 2. NORMAL EXECUTION MODE (PowerToys KeyboardEventHandlers clone)
                // =================================================================================
                if (IsRemapEnabled)
                {
                    bool isModifier = IsModifierKey(vkCode);

                    // Trailing release of physical remap target key (Dual-Trigger)
                    if (isUp && _heldRemapTargetKey != 0 && vkCode == _heldRemapTargetKey)
                    {
                        _heldRemapTargetKey = 0;
                        return (IntPtr)1; // Swallow physical remap target keyup
                    }

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
                                bool isWinMod = (vkCode == 0x5B || vkCode == 0x5C);
                                var releaseBatch = new List<INPUT>();

                                // Release target shortcut keys if they were remapped
                                if (_invokedRule.SendRemapKeys && _invokedRule.HasRemap)
                                {
                                    if (_heldTriggerKey != 0)
                                    {
                                        releaseBatch.Add(CreateKeyInput((ushort)_invokedRule.RemapVirtualKey, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                    }
                                    if (_invokedRule.RemapModifiersWin) releaseBatch.Add(CreateKeyInput(0x5B, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                    if (_invokedRule.RemapModifiersAlt) releaseBatch.Add(CreateKeyInput(0x12, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                    if (_invokedRule.RemapModifiersShift) releaseBatch.Add(CreateKeyInput(0x10, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                    if (_invokedRule.RemapModifiersCtrl) releaseBatch.Add(CreateKeyInput(0x11, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                }

                                if (isWinMod)
                                {
                                    // PowerToys: Send Dummy Key (0xFF down + up) to disarm Explorer Start Menu
                                    AppendDummyKey(releaseBatch, KEYBOARDMANAGER_SHORTCUT_FLAG);
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

                                if (isWinMod)
                                {
                                    // SWALLOW physical Win modifier keyup to prevent Start Menu!
                                    // Windows kernel already received synthetic Win keyup during invocation (SendInput).
                                    return (IntPtr)1;
                                }
                                else
                                {
                                    // FORWARD physical Ctrl/Alt/Shift modifier keyup to Windows so RIT clears key state!
                                    // Swallowing non-Win modifiers leaves them stuck in the OS.
                                    return IntPtr.Zero;
                                }
                            }
                        }

                        // Case 2: Action key tapped again while shortcut is invoked (Repeat/Retrigger)
                        if (isDown && vkCode == _invokedRule.VirtualKey)
                        {
                            _heldTriggerKey = vkCode;

                            // Re-fire action
                            if (_invokedRule.Action != KeyRemapAction.None)
                            {
                                var act = _invokedRule.Action;
                                ThreadPool.QueueUserWorkItem(_ => ActionTriggered?.Invoke(act, KeyPressState.Down));
                            }

                            // Re-inject target action key if remapping
                            if (_invokedRule.SendRemapKeys && _invokedRule.HasRemap)
                            {
                                var targetDown = new INPUT[1];
                                targetDown[0] = CreateKeyInput((ushort)_invokedRule.RemapVirtualKey, 0, KEYBOARDMANAGER_SHORTCUT_FLAG);
                                SafeSendInput(1, targetDown, Marshal.SizeOf<INPUT>());
                            }

                            return (IntPtr)1; // Swallow physical trigger down
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

                            // If this was a single key OR a shortcut that does not send synthetic remap keys,
                            // reset invoked state immediately so user can press it again without lifting modifiers!
                            if (!_invokedRule.HasModifiers || !_invokedRule.SendRemapKeys || !_invokedRule.HasRemap)
                            {
                                lock (_lock)
                                {
                                    _isShortcutInvoked = false;
                                    _invokedRule = null;
                                }
                            }

                            return (IntPtr)1; // Swallow physical action keyup
                        }

                        // Case 5: Any other key pressed while shortcut is invoked (PowerToys lines 1225-1407)
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

                            // 2. Re-assert physical modifiers if still physically held
                            if (_invokedRule.ModifiersWin && (IsPhysicalKeyDown(0x5B) || IsPhysicalKeyDown(0x5C)))
                            {
                                ushort winVk = (ushort)(_invokedWinKey != 0 ? _invokedWinKey : 0x5B);
                                revertBatch.Add(CreateKeyInput(winVk, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                            }
                            if (_invokedRule.ModifiersCtrl && (IsPhysicalKeyDown(0x11) || IsPhysicalKeyDown(0xA2) || IsPhysicalKeyDown(0xA3)))
                                revertBatch.Add(CreateKeyInput(0x11, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                            if (_invokedRule.ModifiersAlt && (IsPhysicalKeyDown(0x12) || IsPhysicalKeyDown(0xA4) || IsPhysicalKeyDown(0xA5)))
                                revertBatch.Add(CreateKeyInput(0x12, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                            if (_invokedRule.ModifiersShift && (IsPhysicalKeyDown(0x10) || IsPhysicalKeyDown(0xA0) || IsPhysicalKeyDown(0xA1)))
                                revertBatch.Add(CreateKeyInput(0x10, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));

                            // 3. Re-inject new key with extraInfo = 0 so OS processes it as natural input
                            revertBatch.Add(CreateKeyInput((ushort)vkCode, 0, 0));

                            SafeSendInput((uint)revertBatch.Count, revertBatch.ToArray(), Marshal.SizeOf<INPUT>());

                            lock (_lock)
                            {
                                _isShortcutInvoked = false;
                                _invokedRule = null;
                                _invokedWinKey = 0;
                                _heldTriggerKey = 0;
                            }

                            return (IntPtr)1; // Swallow raw event since re-injected cleanly
                        }
                    }

                    // Trailing release of physical trigger key after modifier already released (Keychron burst macro)
                    if (isUp && _heldTriggerKey != 0 && vkCode == _heldTriggerKey)
                    {
                        _heldTriggerKey = 0;
                        return (IntPtr)1; // Swallow trailing physical keyup
                    }

                    // B) IF NOT INVOKED: DETECT SHORTCUT OR KEY MATCH:
                    if (isDown && !isModifier)
                    {
                        GetPhysicalModifiers(out bool ctrl, out bool alt, out bool shift, out bool win);
                        KeyBindingRule? matchedRule = null;
                        bool matchedViaRemapTarget = false;

                        lock (_lock)
                        {
                            foreach (var rule in Bindings)
                            {
                                if (rule.IsEmpty) continue;

                                // Match registered shortcut or single key (NO IsTypingKey restriction!)
                                if (rule.VirtualKey == vkCode &&
                                    rule.ModifiersCtrl == ctrl &&
                                    rule.ModifiersAlt == alt &&
                                    rule.ModifiersShift == shift &&
                                    rule.ModifiersWin == win)
                                {
                                    matchedRule = rule;
                                    matchedViaRemapTarget = false;
                                    break;
                                }

                                // Dual-Trigger Match: If user presses the remapped combination directly on the keyboard
                                if (rule.HasRemap && rule.RemapVirtualKey == vkCode &&
                                    rule.RemapModifiersCtrl == ctrl &&
                                    rule.RemapModifiersAlt == alt &&
                                    rule.RemapModifiersShift == shift &&
                                    rule.RemapModifiersWin == win)
                                {
                                    matchedRule = rule;
                                    matchedViaRemapTarget = true;
                                    break;
                                }
                            }
                        }

                        if (matchedRule != null)
                        {
                            if (matchedViaRemapTarget)
                            {
                                lock (_lock)
                                {
                                    _heldRemapTargetKey = vkCode;
                                }

                                if (matchedRule.Action != KeyRemapAction.None)
                                {
                                    var act = matchedRule.Action;
                                    ThreadPool.QueueUserWorkItem(_ => ActionTriggered?.Invoke(act, KeyPressState.Down));
                                }

                                LogMessage?.Invoke($"[Remap] Triggered via Remap Target '{matchedRule.RemapDisplayText}' ➔ Action: {matchedRule.Action}");
                                RemapExecuted?.Invoke(matchedRule.RemapDisplayText, matchedRule.RemapDisplayText, matchedRule.Action.ToString());

                                return (IntPtr)1; // Swallow physical trigger keydown; physical modifiers remain clean
                            }
                            else
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

                                if (win)
                                {
                                    // PowerToys Step: Dummy Key (0xFF down + up) BEFORE releasing Win modifier!
                                    AppendDummyKey(batch, KEYBOARDMANAGER_SHORTCUT_FLAG);

                                    // Release Win modifier via SendInput
                                    ushort w = (ushort)(activeWinVk != 0 ? activeWinVk : 0x5B);
                                    batch.Add(CreateKeyInput(w, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                }

                                if (matchedRule.SendRemapKeys && matchedRule.HasRemap)
                                {
                                    // If remapping from one shortcut to another, release other non-common source modifiers
                                    if (shift && !matchedRule.RemapModifiersShift) batch.Add(CreateKeyInput(0x10, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                    if (alt && !matchedRule.RemapModifiersAlt) batch.Add(CreateKeyInput(0x12, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                    if (ctrl && !matchedRule.RemapModifiersCtrl) batch.Add(CreateKeyInput(0x11, KEYEVENTF_KEYUP, KEYBOARDMANAGER_SHORTCUT_FLAG));

                                    // Inject target modifiers and action key
                                    if (matchedRule.RemapModifiersWin) batch.Add(CreateKeyInput(0x5B, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                    if (matchedRule.RemapModifiersCtrl) batch.Add(CreateKeyInput(0x11, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                    if (matchedRule.RemapModifiersAlt) batch.Add(CreateKeyInput(0x12, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                    if (matchedRule.RemapModifiersShift) batch.Add(CreateKeyInput(0x10, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));

                                    batch.Add(CreateKeyInput((ushort)matchedRule.RemapVirtualKey, 0, KEYBOARDMANAGER_SHORTCUT_FLAG));
                                }

                                if (batch.Count > 0)
                                {
                                    SafeSendInput((uint)batch.Count, batch.ToArray(), Marshal.SizeOf<INPUT>());
                                }

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
        }

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
