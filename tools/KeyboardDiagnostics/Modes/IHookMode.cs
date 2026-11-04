using MicLock.Tools.KeyboardDiagnostics.Native;

namespace MicLock.Tools.KeyboardDiagnostics.Modes;

public interface IHookMode : IDisposable
{
    int ModeId { get; }
    string Name { get; }
    string Description { get; }

    bool TargetTriggerOnly { get; set; }
    uint TargetTriggerKey { get; set; }

    void OnModeActivated();
    void OnModeDeactivated();

    /// <summary>
    /// Processes a low-level keyboard event.
    /// </summary>
    /// <returns>True if event should be swallowed (hook returns 1), False if event should be passed through.</returns>
    bool ProcessEvent(int nCode, IntPtr wParam, ref NativeMethods.KBDLLHOOKSTRUCT hookStruct, out string actionNote);
}
