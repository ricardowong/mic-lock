using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MicLock.Tools.KeyboardDiagnostics.Modes;
using MicLock.Tools.KeyboardDiagnostics.Native;
using MicLock.Tools.KeyboardDiagnostics.Telemetry;

namespace MicLock.Tools.KeyboardDiagnostics.Hooks;

public class LowLevelKeyboardHook : IDisposable
{
    private readonly object _lock = new();
    private readonly NativeMethods.LowLevelKeyboardProc _hookDelegate;
    private IntPtr _hookId = IntPtr.Zero;
    private uint _hookThreadId;
    private Thread? _hookThread;
    private readonly ManualResetEventSlim _hookReadyEvent = new(false);

    private readonly ConcurrentQueue<KeyboardTelemetryRecord> _eventQueue = new();
    private readonly Thread _workerThread;
    private volatile bool _isRunning;

    private readonly IHookMode[] _modes;
    private int _activeModeIndex;

    private long _lastStopwatchTicks;

    public event Action<KeyboardTelemetryRecord>? EventLogged;
    public event Action<int, string>? ModeChanged;

    public int ActiveModeIndex => _activeModeIndex;
    public IHookMode ActiveMode => _modes[_activeModeIndex];
    public bool GlobalFKeySwitchingEnabled { get; set; } = true;

    public T? GetMode<T>() where T : class, IHookMode
    {
        return _modes.OfType<T>().FirstOrDefault();
    }

    public LowLevelKeyboardHook()
    {
        _hookDelegate = HookCallback;

        // Initialize modes 0 - 4
        var mode4 = new Mode4MicLockProduction();
        mode4.RequestHookChainReorder += Rehook;

        _modes = new IHookMode[]
        {
            new Mode0PureMonitor(),
            new Mode1AutoHotkeyMenuMask(),
            new Mode2PowerToysDummy(),
            new Mode3SyntheticWinUp(),
            mode4
        };

        _activeModeIndex = 0;
        _modes[_activeModeIndex].OnModeActivated();

        _isRunning = true;
        _workerThread = new Thread(TelemetryWorkerLoop)
        {
            IsBackground = true,
            Name = "KeyboardTelemetryWorker"
        };
        _workerThread.Start();

        StartHookThread();
    }

    private void StartHookThread()
    {
        _hookReadyEvent.Reset();
        _hookThread = new Thread(HookThreadLoop)
        {
            IsBackground = true,
            Name = "LowLevelKeyboardHookThread"
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();

        if (!_hookReadyEvent.Wait(TimeSpan.FromSeconds(3)))
        {
            throw new InvalidOperationException("Failed to initialize low-level keyboard hook within timeout.");
        }
    }

    private void HookThreadLoop()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        InstallHookInternal();

        _hookReadyEvent.Set();

        // Standard Win32 Message Loop required for WH_KEYBOARD_LL hooks
        while (_isRunning && NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        UninstallHookInternal();
    }

    private void InstallHookInternal()
    {
        if (_hookId != IntPtr.Zero) return;

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        IntPtr hMod = NativeMethods.GetModuleHandle(module?.ModuleName);
        if (hMod == IntPtr.Zero)
        {
            hMod = NativeMethods.GetModuleHandle(null);
        }

        _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookDelegate, hMod, 0);
    }

    private void UninstallHookInternal()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    public void Rehook()
    {
        lock (_lock)
        {
            UninstallHookInternal();
            InstallHookInternal();
        }
    }

    public void SetMode(int modeIndex)
    {
        if (modeIndex < 0 || modeIndex >= _modes.Length) return;

        lock (_lock)
        {
            if (_activeModeIndex == modeIndex) return;

            _modes[_activeModeIndex].OnModeDeactivated();
            _activeModeIndex = modeIndex;
            _modes[_activeModeIndex].OnModeActivated();

            ModeChanged?.Invoke(_activeModeIndex, _modes[_activeModeIndex].Name);
        }
    }

    public void ToggleTriggerFilter()
    {
        lock (_lock)
        {
            foreach (var mode in _modes)
            {
                mode.TargetTriggerOnly = !mode.TargetTriggerOnly;
            }
        }
    }

    public void ToggleMode3WinUpSubmode()
    {
        lock (_lock)
        {
            if (_modes[3] is Mode3SyntheticWinUp mode3)
            {
                mode3.SwallowPhysicalWinKeyUp = !mode3.SwallowPhysicalWinKeyUp;
            }
        }
    }

    public bool IsMode3SwallowingWinUp()
    {
        lock (_lock)
        {
            return _modes[3] is Mode3SyntheticWinUp { SwallowPhysicalWinKeyUp: true };
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            long currentTicks = Stopwatch.GetTimestamp();
            DateTime now = DateTime.Now;

            double microsecondDelta = 0;
            if (_lastStopwatchTicks != 0)
            {
                microsecondDelta = (currentTicks - _lastStopwatchTicks) * 1_000_000.0 / Stopwatch.Frequency;
            }
            _lastStopwatchTicks = currentTicks;

            var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            uint msg = (uint)wParam.ToInt64();
            bool isDown = (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN);

            // Global Hotkey Switching via F1 - F5
            if (GlobalFKeySwitchingEnabled && isDown && hookStruct.vkCode >= 0x70 && hookStruct.vkCode <= 0x74)
            {
                int targetMode = (int)(hookStruct.vkCode - 0x70);
                ThreadPool.QueueUserWorkItem(_ => SetMode(targetMode));
            }

            // Capture OS Asynchronous Key States immediately at time of hook
            bool asyncLWin = NativeMethods.IsKeyDown(NativeMethods.VK_LWIN);
            bool asyncRWin = NativeMethods.IsKeyDown(NativeMethods.VK_RWIN);
            bool asyncCtrl = NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL);
            bool asyncAlt = NativeMethods.IsKeyDown(NativeMethods.VK_MENU);
            bool asyncShift = NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT);

            // Capture Foreground Window details
            var (windowTitle, processName, pid) = NativeMethods.GetForegroundWindowDetails();

            // Process through active test mode
            bool swallow = false;
            string actionNote = "";
            int currentModeId = _activeModeIndex;

            lock (_lock)
            {
                swallow = _modes[_activeModeIndex].ProcessEvent(nCode, wParam, ref hookStruct, out actionNote);
            }

            var record = new KeyboardTelemetryRecord
            {
                Timestamp = now,
                StopwatchTimestamp = currentTicks,
                MicrosecondsDelta = microsecondDelta,
                MessageId = msg,
                MessageName = KeyboardTelemetryRecord.FormatMessageName(msg),
                VirtualKeyCode = hookStruct.vkCode,
                KeyName = NativeMethods.FormatKeyName(hookStruct.vkCode),
                ScanCode = hookStruct.scanCode,
                Flags = hookStruct.flags,
                ExtraInfo = hookStruct.dwExtraInfo,
                ExtraInfoDescription = KeyboardTelemetryRecord.DescribeExtraInfo(hookStruct.dwExtraInfo),
                AsyncLWinDown = asyncLWin,
                AsyncRWinDown = asyncRWin,
                AsyncCtrlDown = asyncCtrl,
                AsyncAltDown = asyncAlt,
                AsyncShiftDown = asyncShift,
                WindowTitle = windowTitle,
                ProcessName = processName,
                ProcessId = pid,
                ModeId = currentModeId,
                Swallowed = swallow,
                ActionNote = actionNote
            };

            _eventQueue.Enqueue(record);

            if (swallow)
            {
                return (IntPtr)1; // Swallow event
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void TelemetryWorkerLoop()
    {
        while (_isRunning)
        {
            if (_eventQueue.TryDequeue(out var record))
            {
                EventLogged?.Invoke(record);
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    public void Dispose()
    {
        if (!_isRunning) return;

        _isRunning = false;

        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hookThreadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        }

        if (_hookThread?.IsAlive == true)
        {
            _hookThread.Join(1000);
        }

        foreach (var mode in _modes)
        {
            mode.Dispose();
        }

        _hookReadyEvent.Dispose();
        GC.SuppressFinalize(this);
    }
}
