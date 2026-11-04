using MicLock.Core.Sys;
using MicLock.Tools.KeyboardDiagnostics.Native;

namespace MicLock.Tools.KeyboardDiagnostics.Modes;

public class Mode4MicLockProduction : IHookMode
{
    public int ModeId => 4;
    public string Name => "MicLock Production Engine";
    public string Description => "Runs the exact production logic from KeyRemapManager in MicLock.Core";

    public bool TargetTriggerOnly { get; set; } = true;
    public uint TargetTriggerKey { get; set; } = 0x43; // 'C'

    private KeyRemapManager? _remapManager;

    public event Action<string, ConsoleColor>? EngineNotification;
    public event Action? RequestHookChainReorder;

    public void OnModeActivated()
    {
        DisposeRemapManager();

        _remapManager = new KeyRemapManager();

        _remapManager.ActionTriggered += (action, state) =>
        {
            EngineNotification?.Invoke($"[MICLOCK PRODUCTION ACTION] Action: {action} (State: {state})", ConsoleColor.Green);
        };

        _remapManager.RemapExecuted += (inputText, remapText, actionName) =>
        {
            EngineNotification?.Invoke($"[MICLOCK PRODUCTION REMAP] '{inputText}' ➔ '{remapText}' (Action: {actionName})", ConsoleColor.Yellow);
        };

        _remapManager.LogMessage += (msg) =>
        {
            EngineNotification?.Invoke($"[MICLOCK PRODUCTION LOG] {msg}", ConsoleColor.DarkYellow);
        };

        // Re-order hook chain so Diagnostic Hook sits at the head to capture both raw input and injected production output
        RequestHookChainReorder?.Invoke();
    }

    public void OnModeDeactivated()
    {
        DisposeRemapManager();
    }

    private void DisposeRemapManager()
    {
        if (_remapManager != null)
        {
            _remapManager.Dispose();
            _remapManager = null;
        }
    }

    public bool ProcessEvent(int nCode, IntPtr wParam, ref NativeMethods.KBDLLHOOKSTRUCT hookStruct, out string actionNote)
    {
        // Production engine manages its own interception/swallowing in KeyRemapManager
        actionNote = "Production Engine Active (Passthrough to KeyRemapManager)";
        return false;
    }

    public void Dispose()
    {
        DisposeRemapManager();
        GC.SuppressFinalize(this);
    }
}
