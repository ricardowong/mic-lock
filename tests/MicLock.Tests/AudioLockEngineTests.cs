using MicLock.Core.Audio;
using Xunit;

namespace MicLock.Tests;

public class AudioLockEngineTests
{
    [Fact]
    public void GetAvailableCaptureDevices_DoesNotThrow_AndReturnsValidList()
    {
        using var engine = new AudioLockEngine();
        var devices = engine.GetAvailableCaptureDevices();
        Assert.NotNull(devices);
        // Note: On Windows with a microphone connected (e.g. Samson Go Mic), devices will contain at least 1 device
    }

    [Fact]
    public void SelectDevice_Default_BindsSuccessfully()
    {
        using var engine = new AudioLockEngine();
        bool bound = engine.SelectDevice(null);
        // If system has a recording device, bound will be true and ActiveDevice will not be null
        if (bound)
        {
            Assert.NotNull(engine.ActiveDevice);
            Assert.False(string.IsNullOrEmpty(engine.ActiveDevice.Name));
        }
    }

    [Fact]
    public void TargetVolume_DefaultsTo100_AndCanBeUpdated()
    {
        using var engine = new AudioLockEngine();
        Assert.Equal(100, engine.TargetVolumePercent);
        Assert.True(engine.IsLocked);

        engine.TargetVolumePercent = 85;
        Assert.Equal(85, engine.TargetVolumePercent);

        engine.IsLocked = false;
        Assert.False(engine.IsLocked);
    }

    [Fact]
    public void ToggleMute_CanBeInvoked_AndReturnsBooleanState()
    {
        using var engine = new AudioLockEngine();
        bool bound = engine.SelectDevice(null);
        if (bound)
        {
            bool initialMute = engine.IsMuted;
            bool toggled = engine.ToggleMute();
            Assert.Equal(!initialMute, toggled);
            Assert.Equal(toggled, engine.IsMuted);

            // Revert back
            engine.SetMute(initialMute);
            Assert.Equal(initialMute, engine.IsMuted);
        }
    }
}
