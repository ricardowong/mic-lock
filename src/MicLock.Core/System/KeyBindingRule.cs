namespace MicLock.Core.Sys;

public enum KeyRemapAction
{
    ToggleMute = 0,
    PushToTalk = 1,
    PushToMute = 2,
    CycleRecordingDevice = 3,
    LockVolume = 4
}

public class KeyBindingRule
{
    public KeyRemapAction Action { get; set; } = KeyRemapAction.ToggleMute;
    public uint VirtualKey { get; set; } = 0x4D; // VK_M
    public bool ModifiersCtrl { get; set; } = true;
    public bool ModifiersAlt { get; set; } = true;
    public bool ModifiersShift { get; set; } = true;
    public bool ModifiersWin { get; set; } = false;
    public bool SuppressOriginalKey { get; set; } = true;
    public string DisplayText { get; set; } = "Ctrl+Shift+Alt+M";

    public bool IsEmpty => VirtualKey == 0;

    public KeyBindingRule Clone()
    {
        return new KeyBindingRule
        {
            Action = Action,
            VirtualKey = VirtualKey,
            ModifiersCtrl = ModifiersCtrl,
            ModifiersAlt = ModifiersAlt,
            ModifiersShift = ModifiersShift,
            ModifiersWin = ModifiersWin,
            SuppressOriginalKey = SuppressOriginalKey,
            DisplayText = DisplayText
        };
    }

    public static List<KeyBindingRule> CreateDefaultRules()
    {
        return new List<KeyBindingRule>
        {
            new KeyBindingRule
            {
                Action = KeyRemapAction.ToggleMute,
                VirtualKey = 0x4D, // 'M'
                ModifiersCtrl = true,
                ModifiersAlt = true,
                ModifiersShift = true,
                ModifiersWin = false,
                SuppressOriginalKey = true,
                DisplayText = "Ctrl+Shift+Alt+M"
            },
            new KeyBindingRule
            {
                Action = KeyRemapAction.CycleRecordingDevice,
                VirtualKey = 0x44, // 'D'
                ModifiersCtrl = true,
                ModifiersAlt = true,
                ModifiersShift = true,
                ModifiersWin = false,
                SuppressOriginalKey = true,
                DisplayText = "Ctrl+Shift+Alt+D"
            },
            new KeyBindingRule
            {
                Action = KeyRemapAction.LockVolume,
                VirtualKey = 0x4C, // 'L'
                ModifiersCtrl = true,
                ModifiersAlt = true,
                ModifiersShift = true,
                ModifiersWin = false,
                SuppressOriginalKey = true,
                DisplayText = "Ctrl+Shift+Alt+L"
            },
            new KeyBindingRule
            {
                Action = KeyRemapAction.PushToTalk,
                VirtualKey = 0,
                ModifiersCtrl = false,
                ModifiersAlt = false,
                ModifiersShift = false,
                ModifiersWin = false,
                SuppressOriginalKey = true,
                DisplayText = "None"
            },
            new KeyBindingRule
            {
                Action = KeyRemapAction.PushToMute,
                VirtualKey = 0,
                ModifiersCtrl = false,
                ModifiersAlt = false,
                ModifiersShift = false,
                ModifiersWin = false,
                SuppressOriginalKey = true,
                DisplayText = "None"
            }
        };
    }
}
