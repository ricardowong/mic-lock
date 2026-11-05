using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MicLock.App.ViewModels;
using MicLock.Core.Audio;
using MicLock.Core.Config;
using MicLock.Core.Sys;
using Xunit;

namespace MicLock.Tests;

public class KeyCaptureTests
{
    private const int WM_KEYDOWN = KeyRemapManager.WM_KEYDOWN;
    private const int WM_KEYUP = KeyRemapManager.WM_KEYUP;
    private const int WM_SYSKEYDOWN = KeyRemapManager.WM_SYSKEYDOWN;
    private const int WM_SYSKEYUP = KeyRemapManager.WM_SYSKEYUP;

    [Fact]
    public async Task CaptureInput_WinDown_Then_CDown_CapturesWinC()
    {
        // 1. Arrange
        using var manager = new KeyRemapManager(installHook: false);
        var tcs = new TaskCompletionSource<KeyBindingRule>();

        manager.StartCaptureInput(rule => tcs.SetResult(rule));
        Assert.True(manager.IsCapturing);

        // 2. Act: Win down (0x5B)
        IntPtr r1 = manager.ProcessKeyboardEvent(WM_KEYDOWN, 0x5B);
        Assert.Equal((IntPtr)1, r1); // Swallowed
        Assert.True(manager.IsCapturing);

        // Act: 'C' down (0x43)
        IntPtr r2 = manager.ProcessKeyboardEvent(WM_KEYDOWN, 0x43);
        Assert.Equal((IntPtr)1, r2); // Swallowed

        // 3. Assert
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, completed);

        var rule = await tcs.Task;
        Assert.NotNull(rule);
        Assert.Equal(0x43u, rule.VirtualKey);
        Assert.True(rule.ModifiersWin);
        Assert.False(rule.ModifiersCtrl);
        Assert.False(rule.ModifiersAlt);
        Assert.False(rule.ModifiersShift);
        Assert.Equal("Win+C", rule.DisplayText);
        Assert.Equal(KeyMappingType.Shortcut, rule.MappingType);
        Assert.True(rule.SuppressOriginalKey);
        Assert.False(manager.IsCapturing);
    }

    [Fact]
    public async Task CaptureTarget_CtrlShiftAltDown_Then_MDown_CapturesTargetShortcut()
    {
        // 1. Arrange
        using var manager = new KeyRemapManager(installHook: false);
        var tcs = new TaskCompletionSource<(uint vk, bool c, bool a, bool s, bool w, string text)>();

        manager.StartCaptureTarget((vk, c, a, s, w, text) => tcs.SetResult((vk, c, a, s, w, text)));
        Assert.True(manager.IsCapturing);

        // 2. Act: Ctrl down (0xA2 - LCtrl)
        IntPtr rCtrl = manager.ProcessKeyboardEvent(WM_KEYDOWN, 0xA2);
        Assert.Equal((IntPtr)1, rCtrl); // Swallowed

        // Shift down (0xA0 - LShift)
        IntPtr rShift = manager.ProcessKeyboardEvent(WM_KEYDOWN, 0xA0);
        Assert.Equal((IntPtr)1, rShift); // Swallowed

        // Alt down (0xA4 - LAlt via WM_SYSKEYDOWN)
        IntPtr rAlt = manager.ProcessKeyboardEvent(WM_SYSKEYDOWN, 0xA4);
        Assert.Equal((IntPtr)1, rAlt); // Swallowed

        // 'M' down (0x4D via WM_SYSKEYDOWN)
        IntPtr rM = manager.ProcessKeyboardEvent(WM_SYSKEYDOWN, 0x4D);
        Assert.Equal((IntPtr)1, rM); // Swallowed

        // 3. Assert
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, completed);

        var result = await tcs.Task;
        Assert.Equal(0x4Du, result.vk);
        Assert.True(result.c);
        Assert.True(result.s);
        Assert.True(result.a);
        Assert.False(result.w);
        Assert.Equal("Ctrl+Shift+Alt+M", result.text);
        Assert.False(manager.IsCapturing);
    }

    [Fact]
    public void CaptureInput_EscapeAlone_CancelsCaptureImmediately()
    {
        // 1. Arrange
        using var manager = new KeyRemapManager(installHook: false);
        bool cancelledFired = false;
        bool capturedFired = false;

        manager.StartCaptureInput(
            onCaptured: _ => capturedFired = true,
            onCancelled: () => cancelledFired = true);
        Assert.True(manager.IsCapturing);

        // 2. Act: Escape down (0x1B) with no modifiers held
        IntPtr r = manager.ProcessKeyboardEvent(WM_KEYDOWN, 0x1B);
        Assert.Equal((IntPtr)1, r); // Swallowed

        // 3. Assert
        Assert.True(cancelledFired);
        Assert.False(capturedFired);
        Assert.False(manager.IsCapturing);
    }

    [Theory]
    [InlineData(0x7Cu, "F13")]
    [InlineData(0x83u, "Dedicated Mic Key")]
    [InlineData(0xADu, "Mute Key")]
    public async Task CaptureInput_SingleKeyWithoutModifiers_CapturesAsSingleKey(uint vk, string expectedName)
    {
        // 1. Arrange
        using var manager = new KeyRemapManager(installHook: false);
        var tcs = new TaskCompletionSource<KeyBindingRule>();

        manager.StartCaptureInput(rule => tcs.SetResult(rule));
        Assert.True(manager.IsCapturing);

        // 2. Act: Single key down
        IntPtr r = manager.ProcessKeyboardEvent(WM_KEYDOWN, vk);
        Assert.Equal((IntPtr)1, r); // Swallowed

        // 3. Assert
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, completed);

        var rule = await tcs.Task;
        Assert.NotNull(rule);
        Assert.Equal(vk, rule.VirtualKey);
        Assert.False(rule.ModifiersWin);
        Assert.False(rule.ModifiersCtrl);
        Assert.False(rule.ModifiersAlt);
        Assert.False(rule.ModifiersShift);
        Assert.Equal(expectedName, rule.DisplayText);
        Assert.Equal(KeyMappingType.SingleKey, rule.MappingType);
        Assert.False(manager.IsCapturing);
    }

    [Fact]
    public async Task CaptureInput_EscapeWithCtrlModifier_CapturesShortcutCtrlEsc()
    {
        // 1. Arrange
        using var manager = new KeyRemapManager(installHook: false);
        var tcs = new TaskCompletionSource<KeyBindingRule>();

        manager.StartCaptureInput(rule => tcs.SetResult(rule));

        // 2. Act: Ctrl down
        manager.ProcessKeyboardEvent(WM_KEYDOWN, 0x11);
        // Escape down with Ctrl held
        manager.ProcessKeyboardEvent(WM_KEYDOWN, 0x1B);

        // 3. Assert: Escape with modifiers is treated as a trigger key
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, completed);

        var rule = await tcs.Task;
        Assert.Equal(0x1Bu, rule.VirtualKey);
        Assert.True(rule.ModifiersCtrl);
        Assert.Equal("Ctrl+Esc", rule.DisplayText);
        Assert.False(manager.IsCapturing);
    }

    [Fact]
    public void CapturePreviewChanged_FiresLiveUpdatesAsModifiersArePressedAndReleased()
    {
        // 1. Arrange
        using var manager = new KeyRemapManager(installHook: false);
        var previewLog = new List<string>();
        manager.CapturePreviewChanged += preview => previewLog.Add(preview);

        manager.StartCaptureInput(_ => { });

        // 2. Act: Sequence of modifiers down and up
        // Win down
        manager.ProcessKeyboardEvent(WM_KEYDOWN, 0x5B);
        // Ctrl down
        manager.ProcessKeyboardEvent(WM_KEYDOWN, 0x11);
        // Ctrl up
        manager.ProcessKeyboardEvent(WM_KEYUP, 0x11);
        // Win up
        manager.ProcessKeyboardEvent(WM_KEYUP, 0x5B);

        // 3. Assert
        Assert.Contains("Press key... (Esc to cancel)", previewLog);
        Assert.Contains("Win+...", previewLog);
        Assert.Contains("Win+Ctrl+...", previewLog);
        // After releasing all modifiers, returns to prompt
        Assert.Equal("Press key... (Esc to cancel)", previewLog[^1]);
    }

    [Fact]
    public void Capture_ModifiersPressedThenReleased_ThenEscapeAlone_CancelsCapture()
    {
        // 1. Arrange
        using var manager = new KeyRemapManager(installHook: false);
        bool cancelledFired = false;

        manager.StartCaptureInput(_ => { }, onCancelled: () => cancelledFired = true);

        // 2. Act: Press Win down, then release Win
        manager.ProcessKeyboardEvent(WM_KEYDOWN, 0x5B);
        manager.ProcessKeyboardEvent(WM_KEYUP, 0x5B);

        // Now press Escape alone
        manager.ProcessKeyboardEvent(WM_KEYDOWN, 0x1B);

        // 3. Assert: Cancelled cleanly without hanging
        Assert.True(cancelledFired);
        Assert.False(manager.IsCapturing);
    }

    [Fact]
    public void CancelCapture_ExplicitCall_CancelsCaptureAndResetsState()
    {
        using var manager = new KeyRemapManager(installHook: false);
        bool cancelledCalled = false;
        bool endedCalled = false;

        manager.CaptureEnded += () => endedCalled = true;
        manager.StartCaptureInput(_ => { }, onCancelled: () => cancelledCalled = true);
        Assert.True(manager.IsCapturing);

        manager.CancelCapture();

        Assert.False(manager.IsCapturing);
        Assert.True(cancelledCalled);
        Assert.True(endedCalled);
    }

    [Fact]
    public void KeyupsDuringCapture_AreSwallowedWithoutExceptions()
    {
        using var manager = new KeyRemapManager(installHook: false);
        manager.StartCaptureInput(_ => { });

        // Arbitrary keyup during capture
        IntPtr res = manager.ProcessKeyboardEvent(WM_KEYUP, 0x41); // 'A' keyup
        Assert.Equal((IntPtr)1, res); // Swallowed
        Assert.True(manager.IsCapturing);
    }

    [Fact]
    public void ViewModel_StartCaptureInput_ReflectsLivePreviewAndCancel()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"miclock_vm_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsManager(tempFile);
            var startup = new StartupManager();
            using var dummyAudio = new DummyAudioEngine();
            using var remapManager = new KeyRemapManager(installHook: false);
            using var vm = new MainViewModel(dummyAudio, settings, startup, remapManager);

            // Initially, input key text shows Win+C default
            Assert.Equal("Win+C", vm.CurrentInputKeyText);
            Assert.False(vm.IsCapturingInput);
            Assert.False(vm.IsRecordingKey);

            // 1. Start input capture
            vm.StartCaptureInputCommand.Execute(null);
            Assert.True(vm.IsCapturingInput);
            Assert.True(vm.IsRecordingKey);
            Assert.Equal("Press key... (Esc to cancel)", vm.CurrentInputKeyText);
            Assert.Equal("Press key... (Esc to cancel)", vm.LiveCaptureDisplayText);

            // 2. Simulate user holding Win key
            remapManager.ProcessKeyboardEvent(WM_KEYDOWN, 0x5B);
            Assert.Equal("Win+...", vm.CurrentInputKeyText);
            Assert.Equal("Win+...", vm.LiveCaptureDisplayText);

            // 3. User cancels capture via CancelCaptureCommand
            vm.CancelCaptureCommand.Execute(null);
            Assert.False(vm.IsCapturingInput);
            Assert.False(vm.IsRecordingKey);
            Assert.Equal("Win+C", vm.CurrentInputKeyText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ViewModel_ClearInputAndTarget_ResetsToNone()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"miclock_vm_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsManager(tempFile);
            var startup = new StartupManager();
            using var dummyAudio = new DummyAudioEngine();
            using var remapManager = new KeyRemapManager(installHook: false);
            using var vm = new MainViewModel(dummyAudio, settings, startup, remapManager);

            Assert.NotEqual("None", vm.CurrentInputKeyText);
            Assert.NotEqual("None", vm.CurrentRemapTargetText);

            // Clear Input
            vm.ClearInputCommand.Execute(null);
            Assert.Equal("None", vm.CurrentInputKeyText);

            // Clear Target
            vm.ClearTargetCommand.Execute(null);
            Assert.Equal("None", vm.CurrentRemapTargetText);
            Assert.False(vm.IsSendRemapKeysEnabled);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ViewModel_StartCaptureTarget_ReflectsLivePreviewAndKeyDetection()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"miclock_vm_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsManager(tempFile);
            var startup = new StartupManager();
            using var dummyAudio = new DummyAudioEngine();
            using var remapManager = new KeyRemapManager(installHook: false);
            using var vm = new MainViewModel(dummyAudio, settings, startup, remapManager);

            // 1. Start target capture
            vm.StartCaptureTargetCommand.Execute(null);
            Assert.True(vm.IsCapturingTarget);
            Assert.True(vm.IsRecordingKey);
            Assert.Equal("Press keys... (Esc to cancel)", vm.CurrentRemapTargetText);

            // 2. User presses Ctrl
            remapManager.ProcessKeyboardEvent(WM_KEYDOWN, 0x11);
            Assert.Equal("Ctrl+...", vm.CurrentRemapTargetText);

            // 3. User cancels
            vm.CancelCaptureCommand.Execute(null);
            Assert.False(vm.IsCapturingTarget);
            Assert.False(vm.IsRecordingKey);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ViewModel_CycleRemapActionCommand_CyclesThroughActions()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"miclock_vm_test_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsManager(tempFile);
            var startup = new StartupManager();
            using var dummyAudio = new DummyAudioEngine();
            using var remapManager = new KeyRemapManager(installHook: false);
            using var vm = new MainViewModel(dummyAudio, settings, startup, remapManager);

            Assert.Equal("Toggle Mute", vm.SelectedActionShortName);
            vm.CycleRemapActionCommand.Execute(null);
            Assert.Equal("Cycle Mic", vm.SelectedActionShortName);
            vm.CycleRemapActionCommand.Execute(null);
            Assert.Equal("Lock Volume", vm.SelectedActionShortName);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void IsExtendedKey_WinKey_IsNotExtendedKey_MatchingPowerToys()
    {
        // PowerToys invariant: VK_LWIN (0x5B) and VK_RWIN (0x5C) are NOT in IsExtendedKey
        Assert.False(KeyRemapManager.IsExtendedKey(0x5B));
        Assert.False(KeyRemapManager.IsExtendedKey(0x5C));
    }

    [Fact]
    public void NormalMode_Keychron_WinC_Sequence_DisarmsStartMenu_And_ReleasesWinKeyCleanly()
    {
        var sentBatches = new List<KeyRemapManager.INPUT[]>();
        KeyRemapManager.OnSendInputSent = batch => sentBatches.Add(batch);

        var heldPhysicalKeys = new HashSet<uint>();
        KeyRemapManager.PhysicalKeyStateOverride = vk => heldPhysicalKeys.Contains(vk);

        try
        {
            var defaultRule = new KeyBindingRule
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
                RemapModifiersWin = false,
                RemapDisplayText = "Ctrl+Shift+Alt+M",
                SendRemapKeys = true
            };

            using var manager = new KeyRemapManager(new[] { defaultRule }, installHook: false);
            manager.IsRemapEnabled = true;

            // 1. Keychron sends physical Win down (0x5B)
            heldPhysicalKeys.Add(0x5B);
            IntPtr rWinDown = manager.ProcessKeyboardEvent(WM_KEYDOWN, 0x5B);
            Assert.Equal(IntPtr.Zero, rWinDown); // Forwarded to OS

            // 2. Keychron sends physical 'C' down (0x43) while Win is held
            heldPhysicalKeys.Add(0x43);
            IntPtr rCDown = manager.ProcessKeyboardEvent(WM_KEYDOWN, 0x43);
            Assert.Equal((IntPtr)1, rCDown); // Swallowed!

            // Verify SendInput batch on trigger:
            Assert.NotEmpty(sentBatches);
            var triggerBatch = sentBatches[0];
            // Must contain Dummy Key (0xFF), Win keyup (0x5B) WITHOUT Extended flag, and target keys
            var dummyKeys = triggerBatch.Where(k => k.u.ki.wVk == 0xFF).ToList();
            Assert.Equal(2, dummyKeys.Count); // Dummy down + up

            var winKeyUps = triggerBatch.Where(k => k.u.ki.wVk == 0x5B && (k.u.ki.dwFlags & KeyRemapManager.KEYEVENTF_KEYUP) != 0).ToList();
            Assert.Single(winKeyUps);
            // CRITICAL INVARIANT: Win key must NOT have KEYEVENTF_EXTENDEDKEY (matches PowerToys)
            Assert.Equal(0u, winKeyUps[0].u.ki.dwFlags & KeyRemapManager.KEYEVENTF_EXTENDEDKEY);

            // 3. Keychron sends physical 'C' up (0x43)
            heldPhysicalKeys.Remove(0x43);
            IntPtr rCUp = manager.ProcessKeyboardEvent(WM_KEYUP, 0x43);
            Assert.Equal((IntPtr)1, rCUp); // Swallowed!

            // 4. Keychron sends physical Win up (0x5B)
            heldPhysicalKeys.Remove(0x5B);
            int countBeforeWinUp = sentBatches.Count;
            IntPtr rWinUp = manager.ProcessKeyboardEvent(WM_KEYUP, 0x5B);
            Assert.Equal((IntPtr)1, rWinUp); // Swallowed in Case 1

            // Verify release batch in Case 1:
            Assert.True(sentBatches.Count > countBeforeWinUp);
            var releaseBatch = sentBatches.Last();

            // Must contain Dummy Key (0xFF) to disarm Start Menu
            Assert.Contains(releaseBatch, k => k.u.ki.wVk == 0xFF);
            // Must contain explicit Win keyup (0x5B) to ensure Windows kernel modifier state is clean
            var finalWinUps = releaseBatch.Where(k => k.u.ki.wVk == 0x5B && (k.u.ki.dwFlags & KeyRemapManager.KEYEVENTF_KEYUP) != 0).ToList();
            Assert.Single(finalWinUps);
            Assert.Equal(0u, finalWinUps[0].u.ki.dwFlags & KeyRemapManager.KEYEVENTF_EXTENDEDKEY);

            // 5. Subsequent user keypress: User presses 'D' (0x44) to type or work
            sentBatches.Clear();
            IntPtr rDDown = manager.ProcessKeyboardEvent(WM_KEYDOWN, 0x44);
            // Must NOT be swallowed, must NOT inject Win key!
            Assert.Equal(IntPtr.Zero, rDDown); // Forwarded to OS cleanly
            // No injected batches with Win key down!
            Assert.DoesNotContain(sentBatches.SelectMany(b => b), k => k.u.ki.wVk == 0x5B && (k.u.ki.dwFlags & KeyRemapManager.KEYEVENTF_KEYUP) == 0);
        }
        finally
        {
            KeyRemapManager.OnSendInputSent = null;
            KeyRemapManager.PhysicalKeyStateOverride = null;
        }
    }

    private class DummyAudioEngine : IAudioLockEngine
    {
        public bool IsLocked { get; set; }
        public bool IsMicDeviceLocked { get; set; }
        public int TargetVolumePercent { get; set; }
        public AudioDeviceInfo? ActiveDevice => new AudioDeviceInfo { Id = "dummy-id", Name = "Test Mic", IsDefault = true };
        public int CurrentVolumePercent => 80;
        public bool IsMuted => false;

        public event EventHandler<int>? VolumeChanged { add { } remove { } }
        public event EventHandler<bool>? MuteChanged { add { } remove { } }
        public event EventHandler<VolumeRestoredEventArgs>? VolumeRestored { add { } remove { } }
        public event EventHandler<string>? DefaultDeviceRestored { add { } remove { } }
        public event EventHandler<AudioDeviceInfo?>? ActiveDeviceChanged { add { } remove { } }
        public event EventHandler<ActivityLogEntry>? LogEntryAdded { add { } remove { } }

        public IReadOnlyList<AudioDeviceInfo> GetAvailableCaptureDevices() => new[] { ActiveDevice! };
        public bool SelectDevice(string? deviceId) => true;
        public void SetVolume(int percent) { }
        public bool ToggleMute() => false;
        public void SetMute(bool mute) { }
        public void RefreshCurrentState() { }
        public void Dispose() { }
    }
}
