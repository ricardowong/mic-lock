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
}
