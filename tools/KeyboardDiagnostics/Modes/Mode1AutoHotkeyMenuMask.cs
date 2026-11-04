using MicLock.Tools.KeyboardDiagnostics.Native;

namespace MicLock.Tools.KeyboardDiagnostics.Modes;

public class Mode1AutoHotkeyMenuMask : IHookMode
{
    public int ModeId => 1;
    public string Name => "AutoHotkey / MenuMask Mode";
    public string Description => "Injects VK_MENU_MASK (0xE8) down/up when Win is held and non-Win key is pressed; forwards physical Win keyup";

    public bool TargetTriggerOnly { get; set; } = true;
    public uint TargetTriggerKey { get; set; } = 0x43; // 'C'

    private bool _winPhysicallyDown;
    private bool _maskedStartMenu;

    public void OnModeActivated()
    {
        _winPhysicallyDown = NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) || NativeMethods.IsKeyDown(NativeMethods.VK_RWIN);
        _maskedStartMenu = false;
    }

    public void OnModeDeactivated()
    {
        _winPhysicallyDown = false;
        _maskedStartMenu = false;
    }

    public bool ProcessEvent(int nCode, IntPtr wParam, ref NativeMethods.KBDLLHOOKSTRUCT hookStruct, out string actionNote)
    {
        uint msg = (uint)wParam.ToInt64();
        bool isDown = (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN);
        bool isUp = (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP);
        uint vk = hookStruct.vkCode;

        // 1. Ignore our own injected VK_MENU_MASK events to prevent loops
        if (vk == NativeMethods.VK_MENU_MASK || hookStruct.dwExtraInfo == (UIntPtr)NativeMethods.EXTRA_INFO_AHK_MASK)
        {
            actionNote = "Injected MenuMask (0xE8) event passed to OS";
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
                if (_maskedStartMenu)
                {
                    _maskedStartMenu = false;
                    actionNote = "FORWARDING physical Win keyup to OS (Start Menu was masked via 0xE8)";
                    return false; // Crucial invariant: never swallow physical Win keyup!
                }

                actionNote = "Forwarding unmasked physical Win keyup to OS";
                return false;
            }
        }

        // 3. Handle non-Win keys
        bool isWinActive = _winPhysicallyDown || NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) || NativeMethods.IsKeyDown(NativeMethods.VK_RWIN);
        bool matchesTrigger = !TargetTriggerOnly || (vk == TargetTriggerKey);

        if (isWinActive && matchesTrigger)
        {
            if (isDown)
            {
                // In AutoHotkey: Inject VK_MENU_MASK (0xE8) down and up to clear g_fWinKeyPressed in explorer.exe
                NativeMethods.SendKeyDownUp(NativeMethods.VK_MENU_MASK, isExtended: false, extraInfo: NativeMethods.EXTRA_INFO_AHK_MASK);
                _maskedStartMenu = true;

                actionNote = $"[AHK 0xE8 Injected] Masked Start Menu; Swallowing trigger keydown ({NativeMethods.FormatKeyName(vk)})";
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
