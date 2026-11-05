namespace MicLock.Core.Sys;

public enum KeyRemapAction
{
    ToggleMute = 0,
    PushToTalk = 1,
    PushToMute = 2,
    CycleRecordingDevice = 3,
    LockVolume = 4,
    None = 5 // Pure remapper (injects remapped keystroke only)
}

public enum KeyMappingType
{
    SingleKey = 0,
    Shortcut = 1
}

public class KeyBindingRule
{
    // Action to execute
    public KeyRemapAction Action { get; set; } = KeyRemapAction.ToggleMute;

    // Physical Input Key/Shortcut (what the user presses on the physical keyboard)
    public KeyMappingType MappingType { get; set; } = KeyMappingType.SingleKey;
    public uint VirtualKey { get; set; } = 0;
    public bool ModifiersCtrl { get; set; } = false;
    public bool ModifiersAlt { get; set; } = false;
    public bool ModifiersShift { get; set; } = false;
    public bool ModifiersWin { get; set; } = false;
    public bool SuppressOriginalKey { get; set; } = true;
    public string DisplayText { get; set; } = "None";

    // Remapped Target Key/Shortcut (e.g. Ctrl+Shift+Alt+M)
    public uint RemapVirtualKey { get; set; } = 0;
    public bool RemapModifiersCtrl { get; set; } = false;
    public bool RemapModifiersAlt { get; set; } = false;
    public bool RemapModifiersShift { get; set; } = false;
    public bool RemapModifiersWin { get; set; } = false;
    public string RemapDisplayText { get; set; } = "None";
    public bool SendRemapKeys { get; set; } = false;

    public bool IsEmpty => VirtualKey == 0;
    public bool HasModifiers => ModifiersCtrl || ModifiersAlt || ModifiersShift || ModifiersWin;
    public bool HasRemap => RemapVirtualKey != 0;

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
            DisplayText = DisplayText,
            RemapVirtualKey = RemapVirtualKey,
            RemapModifiersCtrl = RemapModifiersCtrl,
            RemapModifiersAlt = RemapModifiersAlt,
            RemapModifiersShift = RemapModifiersShift,
            RemapModifiersWin = RemapModifiersWin,
            RemapDisplayText = RemapDisplayText,
            SendRemapKeys = SendRemapKeys
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
                VirtualKey = 0x43, // 'C' (Keychron physical mic key sends Win+C)
                ModifiersCtrl = false,
                ModifiersAlt = false,
                ModifiersShift = false,
                ModifiersWin = true,
                SuppressOriginalKey = true,
                DisplayText = "Win+C",
                RemapVirtualKey = 0x4D, // 'M'
                RemapModifiersCtrl = true,
                RemapModifiersAlt = true,
                RemapModifiersShift = true,
                RemapModifiersWin = false,
                RemapDisplayText = "Ctrl+Shift+Alt+M",
                SendRemapKeys = true
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
                DisplayText = "Ctrl+Shift+Alt+D",
                RemapVirtualKey = 0,
                RemapDisplayText = "None",
                SendRemapKeys = false
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
                DisplayText = "Ctrl+Shift+Alt+L",
                RemapVirtualKey = 0,
                RemapDisplayText = "None",
                SendRemapKeys = false
            },
            new KeyBindingRule
            {
                Action = KeyRemapAction.PushToTalk,
                VirtualKey = 0,
                DisplayText = "None",
                RemapVirtualKey = 0,
                RemapDisplayText = "None",
                SendRemapKeys = false
            },
            new KeyBindingRule
            {
                Action = KeyRemapAction.PushToMute,
                VirtualKey = 0,
                DisplayText = "None",
                RemapVirtualKey = 0,
                RemapDisplayText = "None",
                SendRemapKeys = false
            }
        };
    }
}
