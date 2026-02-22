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
}
