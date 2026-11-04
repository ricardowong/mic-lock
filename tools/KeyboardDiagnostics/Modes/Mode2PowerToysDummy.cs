using MicLock.Tools.KeyboardDiagnostics.Native;

namespace MicLock.Tools.KeyboardDiagnostics.Modes;

public class Mode2PowerToysDummy : IHookMode
{
    public int ModeId => 2;
    public string Name => "PowerToys Dummy Key Mode";
    public string Description => "Injects VK_DUMMY (0xFF) down/up with extraInfo 0x101; tests PowerToys suppression sequence";

    public bool TargetTriggerOnly { get; set; } = true;
    public uint TargetTriggerKey { get; set; } = 0x43; // 'C'

    private bool _winPhysicallyDown;
    private bool _dummyInjected;

    public void OnModeActivated()
    {
        _winPhysicallyDown = NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) || NativeMethods.IsKeyDown(NativeMethods.VK_RWIN);
        _dummyInjected = false;
    }

    public void OnModeDeactivated()
    {
        _winPhysicallyDown = false;
        _dummyInjected = false;
    }

    public bool ProcessEvent(int nCode, IntPtr wParam, ref NativeMethods.KBDLLHOOKSTRUCT hookStruct, out string actionNote)
    {
        uint msg = (uint)wParam.ToInt64();
        bool isDown = (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN);
        bool isUp = (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP);
        uint vk = hookStruct.vkCode;

        // 1. Ignore our injected VK_DUMMY (0xFF) events
        if (vk == NativeMethods.VK_DUMMY || hookStruct.dwExtraInfo == (UIntPtr)NativeMethods.EXTRA_INFO_POWERTOYS_SHORTCUT)
        {
            actionNote = "Injected PowerToys Dummy (0xFF) event passed to OS";
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
                if (_dummyInjected)
                {
                    _dummyInjected = false;
                    actionNote = "FORWARDING physical Win keyup to OS (Suppressed via PowerToys 0xFF dummy)";
                    return false;
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
                // Replicate PowerToys SendVirtualInput: Injects VK_DUMMY down and up with flag 0x101
                NativeMethods.SendKeyDownUp(NativeMethods.VK_DUMMY, isExtended: false, extraInfo: NativeMethods.EXTRA_INFO_POWERTOYS_SHORTCUT);
                _dummyInjected = true;

                actionNote = $"[PowerToys 0xFF Injected] Extra=0x101; Swallowing trigger keydown ({NativeMethods.FormatKeyName(vk)})";
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
