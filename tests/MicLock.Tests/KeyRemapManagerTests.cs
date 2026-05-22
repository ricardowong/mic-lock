using MicLock.Core.Sys;
using Xunit;

namespace MicLock.Tests;

public class KeyRemapManagerTests
{
    [Theory]
    [InlineData(0x83u, "F20")]
    [InlineData(0xADu, "VK_VOLUME_MUTE")]
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
        Assert.Equal("F20", combo2);

        string combo3 = KeyRemapManager.FormatKeyCombination(0x44, ctrl: true, alt: true, shift: true, win: false);
        Assert.Equal("Ctrl+Shift+Alt+D", combo3);

        string combo4 = KeyRemapManager.FormatKeyCombination(0x13, ctrl: false, alt: false, shift: false, win: false);
        Assert.Equal("Pause", combo4);

        string combo5 = KeyRemapManager.FormatKeyCombination(0x20, ctrl: true, alt: false, shift: true, win: false);
        Assert.Equal("Ctrl+Shift+Space", combo5);

        string combo6 = KeyRemapManager.FormatKeyCombination(0, ctrl: false, alt: false, shift: false, win: false);
        Assert.Equal("None", combo6);
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
    [InlineData(0x83u, false)] // F20 (Dedicated Mic Key)
    [InlineData(0xADu, false)] // VK_VOLUME_MUTE
    [InlineData(0x13u, false)] // Pause
    [InlineData(0x4Du, false)] // M
    public void IsModifierKey_IdentifiesModifiersAccurately(uint vk, bool expected)
    {
        bool actual = KeyRemapManager.IsModifierKey(vk);
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
            VirtualKey = 0x83, // F20
            ModifiersCtrl = false,
            ModifiersAlt = false,
            ModifiersShift = false,
            ModifiersWin = false,
            SuppressOriginalKey = true,
            DisplayText = "F20"
        };

        manager.UpdateBinding(newRule);
        var retrieved = manager.GetBindingForAction(KeyRemapAction.ToggleMute);

        Assert.NotNull(retrieved);
        Assert.Equal(0x83u, retrieved.VirtualKey);
        Assert.Equal("F20", retrieved.DisplayText);
    }
}
