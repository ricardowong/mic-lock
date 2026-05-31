using MicLock.Core.Config;
using Xunit;

namespace MicLock.Tests;

public class SettingsManagerTests : IDisposable
{
    private readonly string _tempPath;

    public SettingsManagerTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"miclock_test_{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath))
        {
            try { File.Delete(_tempPath); } catch { }
        }
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaultSettings()
    {
        var manager = new SettingsManager(_tempPath);
        Assert.NotNull(manager.Current);
        Assert.Equal(100, manager.Current.TargetVolumePercent);
        Assert.True(manager.Current.IsLockEnabled);
        Assert.True(manager.Current.StartWithWindows);
        Assert.Null(manager.Current.SelectedDeviceId);
    }

    [Fact]
    public void SaveAndLoad_PersistsValuesCorrectly()
    {
        var manager = new SettingsManager(_tempPath);
        manager.Current.TargetVolumePercent = 85;
        manager.Current.IsLockEnabled = false;
        manager.Current.SelectedDeviceId = "{device-guid-123}";
        manager.Current.SelectedDeviceName = "Samson Go Mic";
        manager.Save();

        var reloaded = new SettingsManager(_tempPath);
        Assert.Equal(85, reloaded.Current.TargetVolumePercent);
        Assert.False(reloaded.Current.IsLockEnabled);
        Assert.Equal("{device-guid-123}", reloaded.Current.SelectedDeviceId);
        Assert.Equal("Samson Go Mic", reloaded.Current.SelectedDeviceName);
    }

    [Fact]
    public void ClampValues_EnforcesBounds()
    {
        var manager = new SettingsManager(_tempPath);
        manager.Current.TargetVolumePercent = 150;
        manager.Save();

        var reloaded = new SettingsManager(_tempPath);
        Assert.Equal(100, reloaded.Current.TargetVolumePercent);

        manager.Current.TargetVolumePercent = -20;
        manager.Save();

        var reloadedNegative = new SettingsManager(_tempPath);
        Assert.Equal(0, reloadedNegative.Current.TargetVolumePercent);
    }

    [Fact]
    public void PersistentOverlaySettings_PersistAndClampCorrectly()
    {
        var manager = new SettingsManager(_tempPath);
        Assert.False(manager.Current.KeepOverlayWhenMuted);
        Assert.Equal(0.70, manager.Current.OverlayOpacityWhenMuted);

        manager.Current.KeepOverlayWhenMuted = true;
        manager.Current.OverlayOpacityWhenMuted = 0.85;
        manager.Save();

        var reloaded = new SettingsManager(_tempPath);
        Assert.True(reloaded.Current.KeepOverlayWhenMuted);
        Assert.Equal(0.85, reloaded.Current.OverlayOpacityWhenMuted);

        // Test clamping bounds
        manager.Current.OverlayOpacityWhenMuted = 1.8;
        manager.Save();
        var reloadedHigh = new SettingsManager(_tempPath);
        Assert.Equal(1.0, reloadedHigh.Current.OverlayOpacityWhenMuted);

        manager.Current.OverlayOpacityWhenMuted = 0.02;
        manager.Save();
        var reloadedLow = new SettingsManager(_tempPath);
        Assert.Equal(0.10, reloadedLow.Current.OverlayOpacityWhenMuted);
    }

    [Fact]
    public void KeyBindingsSettings_PersistAndPopulateDefaults()
    {
        var manager = new SettingsManager(_tempPath);
        Assert.True(manager.Current.IsKeyRemapEnabled);
        Assert.NotNull(manager.Current.KeyBindings);
        Assert.NotEmpty(manager.Current.KeyBindings);

        // Verify default actions exist
        Assert.Contains(manager.Current.KeyBindings, b => b.Action == MicLock.Core.Sys.KeyRemapAction.ToggleMute);
        Assert.Contains(manager.Current.KeyBindings, b => b.Action == MicLock.Core.Sys.KeyRemapAction.CycleRecordingDevice);
        Assert.Contains(manager.Current.KeyBindings, b => b.Action == MicLock.Core.Sys.KeyRemapAction.LockVolume);

        // Customize and verify save
        var toggleRule = manager.Current.KeyBindings.First(b => b.Action == MicLock.Core.Sys.KeyRemapAction.ToggleMute);
        toggleRule.VirtualKey = 0x83; // F20
        toggleRule.ModifiersCtrl = false;
        toggleRule.ModifiersAlt = false;
        toggleRule.ModifiersShift = false;
        toggleRule.DisplayText = "F20";
        manager.Save();

        var reloaded = new SettingsManager(_tempPath);
        var reloadedRule = reloaded.Current.KeyBindings.First(b => b.Action == MicLock.Core.Sys.KeyRemapAction.ToggleMute);
        Assert.Equal(0x83u, reloadedRule.VirtualKey);
        Assert.False(reloadedRule.ModifiersCtrl);
        Assert.Equal("F20", reloadedRule.DisplayText);
    }
}
