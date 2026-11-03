namespace MicLock.Core.Sys;

public enum KeyRemapAction
{
    ToggleMute = 0,
    PushToTalk = 1,
    PushToMute = 2,
    CycleRecordingDevice = 3,
    LockVolume = 4
}

public enum KeyMappingType
{
    SingleKey = 0,
    Shortcut = 1
}

public class KeyBindingRule
{
    public KeyRemapAction Action { get; set; } = KeyRemapAction.ToggleMute;
    public KeyMappingType MappingType { get; set; } = KeyMappingType.SingleKey;
    public uint VirtualKey { get; set; } = 0;
    public bool ModifiersCtrl { get; set; } = false;
    public bool ModifiersAlt { get; set; } = false;
    public bool ModifiersShift { get; set; } = false;
    public bool ModifiersWin { get; set; } = false;
    public bool SuppressOriginalKey { get; set; } = true;
    public string DisplayText { get; set; } = "None";

    public bool IsEmpty => VirtualKey == 0;
    public bool HasModifiers => ModifiersCtrl || ModifiersAlt || ModifiersShift || ModifiersWin;

    public KeyBindingRule Clone()
    {
        return new KeyBindingRule
        {
            Action = Action,
            MappingType = MappingType,
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
                MappingType = KeyMappingType.Shortcut,
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
                MappingType = KeyMappingType.Shortcut,
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
                MappingType = KeyMappingType.Shortcut,
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
