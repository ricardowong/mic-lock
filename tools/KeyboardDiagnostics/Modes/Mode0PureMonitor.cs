using MicLock.Tools.KeyboardDiagnostics.Native;

namespace MicLock.Tools.KeyboardDiagnostics.Modes;

public class Mode0PureMonitor : IHookMode
{
    public int ModeId => 0;
    public string Name => "Pure Monitor";
    public string Description => "100% Passthrough - monitors, logs, and does not alter any input";

    public bool TargetTriggerOnly { get; set; } = false;
    public uint TargetTriggerKey { get; set; } = 0x43; // 'C'

    public void OnModeActivated() { }
    public void OnModeDeactivated() { }

    public bool ProcessEvent(int nCode, IntPtr wParam, ref NativeMethods.KBDLLHOOKSTRUCT hookStruct, out string actionNote)
    {
        actionNote = "Passthrough (Monitored)";
        return false; // Do not swallow
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
