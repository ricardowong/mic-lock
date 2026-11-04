using MicLock.Tools.KeyboardDiagnostics.Native;

namespace MicLock.Tools.KeyboardDiagnostics.Modes;

public class Mode3SyntheticWinUp : IHookMode
{
    public int ModeId => 3;
    public string Name => "Synthetic Win-Up on Trigger";
    public string Description => "Injects VK_LWIN keyup with KEYEVENTF_EXTENDEDKEY upon trigger keydown; tests swallowing vs forwarding physical Win keyup";

    public bool TargetTriggerOnly { get; set; } = true;
    public uint TargetTriggerKey { get; set; } = 0x43; // 'C'

    /// <summary>
    /// When true, swallows the physical Win keyup to demonstrate the infamous Windows OS 'Stuck Win Key' bug.
    /// When false, forwards the physical Win keyup safely.
    /// </summary>
    public bool SwallowPhysicalWinKeyUp { get; set; } = false;

    private bool _winPhysicallyDown;
    private bool _syntheticWinUpInjected;

    public void OnModeActivated()
    {
        _winPhysicallyDown = NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) || NativeMethods.IsKeyDown(NativeMethods.VK_RWIN);
        _syntheticWinUpInjected = false;
    }

    public void OnModeDeactivated()
    {
        _winPhysicallyDown = false;
        _syntheticWinUpInjected = false;
    }

    public bool ProcessEvent(int nCode, IntPtr wParam, ref NativeMethods.KBDLLHOOKSTRUCT hookStruct, out string actionNote)
    {
        uint msg = (uint)wParam.ToInt64();
        bool isDown = (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN);
        bool isUp = (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP);
        uint vk = hookStruct.vkCode;

        // 1. Ignore our own synthetic Win-up injection to prevent loops
        if (hookStruct.dwExtraInfo == (UIntPtr)NativeMethods.EXTRA_INFO_SYNTHETIC_DIAGNOSTIC)
        {
            actionNote = "Injected synthetic VK_LWIN keyup passed to OS";
            return false;
        }

        // 2. Track Physical Windows Keys (VK_LWIN / VK_RWIN)
        bool isWinKey = (vk == NativeMethods.VK_LWIN || vk == NativeMethods.VK_RWIN);
        if (isWinKey)
        {
            if (isDown)
            {
                _winPhysicallyDown = true;
                actionNote = "Physical Win keydown registered";
                return false;
            }
            else if (isUp)
            {
                _winPhysicallyDown = false;
                if (_syntheticWinUpInjected)
                {
                    _syntheticWinUpInjected = false;

                    if (SwallowPhysicalWinKeyUp)
                    {
                        actionNote = "[STUCK-WIN BUG SIMULATION] SWALLOWED physical Win keyup! Check Async LWin state!";
                        return true; // Intentionally swallow to demonstrate trapped OS modifier state
                    }
                    else
                    {
                        actionNote = "[SAFE ARCHITECTURE] FORWARDED physical Win keyup to OS";
                        return false; // Safe: forward physical keyup
                    }
                }

                actionNote = "Forwarding physical Win keyup to OS";
                return false;
            }
        }

        // 3. Handle non-Win trigger keys
        bool isWinActive = _winPhysicallyDown || NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) || NativeMethods.IsKeyDown(NativeMethods.VK_RWIN);
        bool matchesTrigger = !TargetTriggerOnly || (vk == TargetTriggerKey);

        if (isWinActive && matchesTrigger)
        {
            if (isDown)
            {
                // Injects synthetic VK_LWIN keyup with KEYEVENTF_EXTENDEDKEY upon trigger keydown
                NativeMethods.SendKey(NativeMethods.VK_LWIN, isKeyUp: true, isExtended: true, extraInfo: NativeMethods.EXTRA_INFO_SYNTHETIC_DIAGNOSTIC);
                _syntheticWinUpInjected = true;

                actionNote = $"[Synthetic Win-Up Injected (ExtendedKey)]; Swallowing trigger keydown ({NativeMethods.FormatKeyName(vk)})";
                return true; // Swallow trigger keydown
            }
            else if (isUp)
            {
                actionNote = $"Swallowing trigger keyup ({NativeMethods.FormatKeyName(vk)})";
                return true; // Swallow trigger keyup
            }
        }

        actionNote = "Passthrough";
        return false;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
