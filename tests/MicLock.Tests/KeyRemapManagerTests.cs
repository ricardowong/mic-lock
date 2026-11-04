using MicLock.Core.Sys;
using Xunit;

namespace MicLock.Tests;

public class KeyRemapManagerTests
{
    [Theory]
    [InlineData(0x83u, "Dedicated Mic Key")]
    [InlineData(0xADu, "Mute Key")]
    [InlineData(0x13u, "Pause")]
    [InlineData(0x91u, "Scroll Lock")]
    [InlineData(0x7Cu, "F13")]
    [InlineData(0x87u, "F24")]
    [InlineData(0x4Du, "M")]
    [InlineData(0x44u, "D")]
    [InlineData(0x20u, "Space")]
    [InlineData(0x0Du, "Enter")]
    [InlineData(0x1Bu, "Esc")]
    [InlineData(0x00u, "None")]
    public void FormatKeyName_FormatsCorrectly(uint vk, string expected)
    {
        string actual = KeyRemapManager.FormatKeyName(vk);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FormatKeyCombination_FormatsModifiersAndKeyProperly()
    {
        string combo1 = KeyRemapManager.FormatKeyCombination(0x4D, ctrl: true, alt: true, shift: true, win: false);
        Assert.Equal("Ctrl+Shift+Alt+M", combo1);

        string combo2 = KeyRemapManager.FormatKeyCombination(0x83, ctrl: false, alt: false, shift: false, win: false);
        Assert.Equal("Dedicated Mic Key", combo2);

        string combo3 = KeyRemapManager.FormatKeyCombination(0x44, ctrl: true, alt: true, shift: true, win: false);
        Assert.Equal("Ctrl+Shift+Alt+D", combo3);

        string combo4 = KeyRemapManager.FormatKeyCombination(0x13, ctrl: false, alt: false, shift: false, win: false);
        Assert.Equal("Pause", combo4);

        string combo5 = KeyRemapManager.FormatKeyCombination(0x20, ctrl: true, alt: false, shift: true, win: false);
        Assert.Equal("Ctrl+Shift+Space", combo5);

        string combo6 = KeyRemapManager.FormatKeyCombination(0, ctrl: false, alt: false, shift: false, win: false);
        Assert.Equal("None", combo6);

        // Win+C combination for Keychron
        string combo7 = KeyRemapManager.FormatKeyCombination(0x43, ctrl: false, alt: false, shift: false, win: true);
        Assert.Equal("Win+C", combo7);

        // Ctrl+Alt+M combination for PowerToys
        string combo8 = KeyRemapManager.FormatKeyCombination(0x4D, ctrl: true, alt: true, shift: false, win: false);
        Assert.Equal("Ctrl+Alt+M", combo8);
    }

    [Theory]
    [InlineData(0x10u, true)]  // Shift
    [InlineData(0x11u, true)]  // Ctrl
    [InlineData(0x12u, true)]  // Alt
    [InlineData(0x5Bu, true)]  // LWin
    [InlineData(0x5Cu, true)]  // RWin
    [InlineData(0xA0u, true)]  // LShift
    [InlineData(0xA2u, true)]  // LCtrl
    [InlineData(0xA4u, true)]  // LAlt
    [InlineData(0x83u, false)] // Dedicated Mic Key
    [InlineData(0xADu, false)] // Mute Key
    [InlineData(0x13u, false)] // Pause
    [InlineData(0x4Du, false)] // M
    public void IsModifierKey_IdentifiesModifiersAccurately(uint vk, bool expected)
    {
        bool actual = KeyRemapManager.IsModifierKey(vk);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0x4Du, true)]  // 'M'
    [InlineData(0x43u, true)]  // 'C'
    [InlineData(0x30u, true)]  // '0'
    [InlineData(0x39u, true)]  // '9'
    [InlineData(0x20u, true)]  // Space
    [InlineData(0x83u, false)] // Dedicated Mic Key (F20)
    [InlineData(0xADu, false)] // Mute Key
    [InlineData(0x13u, false)] // Pause
    [InlineData(0x7Cu, false)] // F13
    public void IsTypingKey_DistinguishesLettersFromDedicatedSpecialKeys(uint vk, bool expected)
    {
        bool actual = KeyRemapManager.IsTypingKey(vk);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void KeyBindingRule_CloneAndDefaultRules_WorkProperly()
    {
        var defaults = KeyBindingRule.CreateDefaultRules();
        Assert.Equal(5, defaults.Count);

        var muteRule = defaults.First(r => r.Action == KeyRemapAction.ToggleMute);
        var clone = muteRule.Clone();

        Assert.Equal(muteRule.Action, clone.Action);
        Assert.Equal(muteRule.VirtualKey, clone.VirtualKey);
        Assert.Equal(muteRule.DisplayText, clone.DisplayText);

        // Modifying clone does not mutate original
        clone.VirtualKey = 0x83;
        Assert.NotEqual(muteRule.VirtualKey, clone.VirtualKey);
    }

    [Fact]
    public void KeyRemapManager_UpdateAndQueryBindings_WorksCorrectly()
    {
        using var manager = new KeyRemapManager();

        var newRule = new KeyBindingRule
        {
            Action = KeyRemapAction.ToggleMute,
            VirtualKey = 0x83, // Dedicated Mic Key
            ModifiersCtrl = false,
            ModifiersAlt = false,
            ModifiersShift = false,
            ModifiersWin = false,
            SuppressOriginalKey = true,
            DisplayText = "Dedicated Mic Key"
        };

        manager.UpdateBinding(newRule);
        var retrieved = manager.GetBindingForAction(KeyRemapAction.ToggleMute);

        Assert.NotNull(retrieved);
        Assert.Equal(0x83u, retrieved.VirtualKey);
        Assert.Equal("Dedicated Mic Key", retrieved.DisplayText);
    }

    [Fact]
    public void TwoStageArchitecture_SingleKeyVsShortcut_DistinguishesCorrectly()
    {
        var singleKeyRule = new KeyBindingRule
        {
            Action = KeyRemapAction.ToggleMute,
            MappingType = KeyMappingType.SingleKey,
            VirtualKey = 0x83, // Dedicated Mic Key
            SuppressOriginalKey = true,
            DisplayText = "Dedicated Mic Key"
        };

        var shortcutRule = new KeyBindingRule
        {
            Action = KeyRemapAction.ToggleMute,
            MappingType = KeyMappingType.Shortcut,
            VirtualKey = 0x4D, // 'M'
            ModifiersCtrl = true,
            ModifiersAlt = true,
            SuppressOriginalKey = true,
            DisplayText = "Ctrl+Alt+M"
        };

        Assert.Equal(KeyMappingType.SingleKey, singleKeyRule.MappingType);
        Assert.False(singleKeyRule.HasModifiers);

        Assert.Equal(KeyMappingType.Shortcut, shortcutRule.MappingType);
        Assert.True(shortcutRule.HasModifiers);
        Assert.True(KeyRemapManager.IsTypingKey(shortcutRule.VirtualKey));
    }

    [Fact]
    public void KeychronAndPowerToys_PresetCombinations_HaveCorrectMappingTypes()
    {
        // Keychron factory mic key produces Win+C (Shortcut)
        var keychron = new KeyBindingRule
        {
            Action = KeyRemapAction.ToggleMute,
            MappingType = KeyMappingType.Shortcut,
            VirtualKey = 0x43, // 'C'
            ModifiersWin = true,
            DisplayText = "Win+C"
        };
        Assert.Equal(KeyMappingType.Shortcut, keychron.MappingType);
        Assert.True(keychron.ModifiersWin);
        Assert.False(keychron.ModifiersCtrl);

        // PowerToys remap produces Ctrl+Alt+M (Shortcut)
        var powerToys = new KeyBindingRule
        {
            Action = KeyRemapAction.ToggleMute,
            MappingType = KeyMappingType.Shortcut,
            VirtualKey = 0x4D, // 'M'
            ModifiersCtrl = true,
            ModifiersAlt = true,
            DisplayText = "Ctrl+Alt+M"
        };
        Assert.Equal(KeyMappingType.Shortcut, powerToys.MappingType);
        Assert.True(powerToys.ModifiersCtrl);
        Assert.True(powerToys.ModifiersAlt);

        // Hardware Dedicated Mic Key produces 0x83 (SingleKey)
        var hardwareKey = new KeyBindingRule
        {
            Action = KeyRemapAction.ToggleMute,
            MappingType = KeyMappingType.SingleKey,
            VirtualKey = 0x83,
            DisplayText = "Dedicated Mic Key"
        };
        Assert.Equal(KeyMappingType.SingleKey, hardwareKey.MappingType);
        Assert.False(hardwareKey.HasModifiers);
    }

    [Fact]
    public void SuppressStartMenu_InvokesWithoutExceptions()
    {
        var ex = Record.Exception(() => KeyRemapManager.SuppressStartMenu());
        Assert.Null(ex);
    }

    [Fact]
    public void KeyBindingRule_WinModifierShortcut_HasModifiersWinSet()
    {
        var winC = new KeyBindingRule
        {
            Action = KeyRemapAction.ToggleMute,
            MappingType = KeyMappingType.Shortcut,
            VirtualKey = 0x43, // 'C'
            ModifiersWin = true,
            DisplayText = "Win+C"
        };

        Assert.True(winC.ModifiersWin);
        Assert.True(winC.HasModifiers);
        Assert.Equal("Win+C", winC.DisplayText);
    }

    [Fact]
    public void KeyBindingRule_UserConfiguredCtrlShiftAltN_HasCorrectModifiers()
    {
        var rule = new KeyBindingRule
        {
            Action = KeyRemapAction.ToggleMute,
            MappingType = KeyMappingType.Shortcut,
            VirtualKey = 78, // 'N'
            ModifiersCtrl = true,
            ModifiersShift = true,
            ModifiersAlt = true,
            ModifiersWin = false,
            DisplayText = "Ctrl+Shift+Alt+N"
        };

        Assert.True(rule.ModifiersCtrl);
        Assert.True(rule.ModifiersShift);
        Assert.True(rule.ModifiersAlt);
        Assert.False(rule.ModifiersWin);
        Assert.Equal(78u, rule.VirtualKey);
        Assert.Equal("Ctrl+Shift+Alt+N", rule.DisplayText);
    }

    [Theory]
    [InlineData(0x5Bu, 0x5Bu)] // VK_LWIN
    [InlineData(0x5Cu, 0x5Bu)] // VK_RWIN
    [InlineData(0x11u, 0x11u)] // VK_CONTROL
    [InlineData(0xA2u, 0x11u)] // VK_LCONTROL
    [InlineData(0xA3u, 0x11u)] // VK_RCONTROL
    [InlineData(0x12u, 0x12u)] // VK_MENU
    [InlineData(0xA4u, 0x12u)] // VK_LMENU
    [InlineData(0xA5u, 0x12u)] // VK_RMENU
    [InlineData(0x10u, 0x10u)] // VK_SHIFT
    [InlineData(0xA0u, 0x10u)] // VK_LSHIFT
    [InlineData(0xA1u, 0x10u)] // VK_RSHIFT
    public void NormalizeModifier_MapsLeftAndRightToStandardModifier(uint inputVk, uint expectedNormalized)
    {
        uint actual = KeyRemapManager.NormalizeModifier(inputVk);
        Assert.Equal(expectedNormalized, actual);
    }

    [Fact]
    public void AppSettings_ClampValues_PreservesConfiguredWinCWithoutOverwriting()
    {
        var settings = new MicLock.Core.Config.AppSettings
        {
            KeyBindings = new List<KeyBindingRule>
            {
                new KeyBindingRule
                {
                    Action = KeyRemapAction.ToggleMute,
                    MappingType = KeyMappingType.Shortcut,
                    VirtualKey = 0x43,
                    ModifiersWin = true,
                    DisplayText = "Win+C"
                }
            }
        };

        settings.ClampValues();

        var muteRule = settings.KeyBindings.First(b => b.Action == KeyRemapAction.ToggleMute);
        Assert.Equal(0x43u, muteRule.VirtualKey);
        Assert.True(muteRule.ModifiersWin);
        Assert.False(muteRule.ModifiersCtrl);
        Assert.Equal("Win+C", muteRule.DisplayText);
    }

    [Fact]
    public void IsAltGr_ExecutesSafely_ReturnsBoolean()
    {
        bool isAltGr = KeyRemapManager.IsAltGr();
        // Method should execute without exception and return valid bool
        Assert.True(isAltGr == true || isAltGr == false);
    }

    [Fact]
    public void SuppressStartMenu_ExecutesSafely_DoesNotThrow()
    {
        var ex = Record.Exception(() => KeyRemapManager.SuppressStartMenu());
        Assert.Null(ex);
    }

    [Fact]
    public void KeyBindingRule_RemapProperties_ClonedAndSetProperly()
    {
        var rule = new KeyBindingRule
        {
            Action = KeyRemapAction.ToggleMute,
            MappingType = KeyMappingType.Shortcut,
            VirtualKey = 0x43, // 'C'
            ModifiersWin = true,
            DisplayText = "Win+C",
            RemapVirtualKey = 0x4D, // 'M'
            RemapModifiersCtrl = true,
            RemapModifiersAlt = true,
            RemapModifiersShift = true,
            RemapDisplayText = "Ctrl+Shift+Alt+M",
            SendRemapKeys = true
        };

        var clone = rule.Clone();
        Assert.Equal(rule.VirtualKey, clone.VirtualKey);
        Assert.Equal(rule.RemapVirtualKey, clone.RemapVirtualKey);
        Assert.Equal(rule.RemapDisplayText, clone.RemapDisplayText);
        Assert.True(clone.SendRemapKeys);
        Assert.True(clone.HasRemap);
    }

    [Fact]
    public void InjectShortcut_ExecutesSafely_DoesNotThrow()
    {
        var ex = Record.Exception(() => KeyRemapManager.InjectShortcut(0x4D, ctrl: true, alt: true, shift: true, win: false));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(0x5Bu, true)]  // LWin
    [InlineData(0x5Cu, true)]  // RWin
    [InlineData(0x5Du, true)]  // Apps
    [InlineData(0xA3u, true)]  // RControl
    [InlineData(0xA5u, true)]  // RMenu (AltGr)
    [InlineData(0x2Du, true)]  // Insert
    [InlineData(0x2Eu, true)]  // Delete
    [InlineData(0x25u, true)]  // Left Arrow
    [InlineData(0x43u, false)] // 'C'
    [InlineData(0x4Du, false)] // 'M'
    [InlineData(0x11u, false)] // Ctrl
    [InlineData(0x10u, false)] // Shift
    public void IsExtendedKey_IdentifiesExtendedKeysAccurately(uint vk, bool expected)
    {
        bool actual = KeyRemapManager.IsExtendedKey((ushort)vk);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CaptureLifecycle_StartsAndCancelsCorrectly()
    {
        using var manager = new KeyRemapManager();
        Assert.False(manager.IsCapturing);

        bool startedCalled = false;
        bool cancelledCalled = false;
        manager.CaptureStarted += () => startedCalled = true;

        manager.StartCaptureInput(_ => { }, () => cancelledCalled = true);
        Assert.True(manager.IsCapturing);
        Assert.True(startedCalled);

        manager.CancelCapture();
        Assert.False(manager.IsCapturing);
        Assert.True(cancelledCalled);
    }

    [Fact]
    public void CaptureTargetLifecycle_StartsAndCancelsCorrectly()
    {
        using var manager = new KeyRemapManager();
        Assert.False(manager.IsCapturing);

        bool startedCalled = false;
        bool cancelledCalled = false;
        manager.CaptureStarted += () => startedCalled = true;

        manager.StartCaptureTarget((_vk, _c, _a, _s, _w, _disp) => { }, () => cancelledCalled = true);
        Assert.True(manager.IsCapturing);
        Assert.True(startedCalled);

        manager.CancelCapture();
        Assert.False(manager.IsCapturing);
        Assert.True(cancelledCalled);
    }
}
