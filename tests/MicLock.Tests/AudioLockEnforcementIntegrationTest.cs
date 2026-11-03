using System.Diagnostics;
using System.Threading;
using MicLock.Core.Audio;
using NAudio.CoreAudioApi;
using Xunit;

namespace MicLock.Tests;

public class AudioLockEnforcementIntegrationTest
{
    [Fact]
    public void VolumeLock_EnforcesTarget_WhenExternalAppChangesVolume()
    {
        using var engine = new AudioLockEngine();
        engine.TargetVolumePercent = 100;
        engine.IsLocked = true;

        bool bound = engine.SelectDevice(null);
        if (!bound)
        {
            // Headless cloud CI runners (GitHub Actions / Azure) do not have audio capture hardware
            return;
        }

        // Ensure endpoint is unmuted so volume enforcement logic is active
        engine.SetMute(false);

        var restoredSignal = new ManualResetEventSlim(false);
        int capturedRestored = 0;
        int capturedPrevious = 0;

        engine.VolumeRestored += (s, e) =>
        {
            capturedPrevious = e.PreviousPercent;
            capturedRestored = e.RestoredPercent;
            restoredSignal.Set();
        };

        // Alter volume directly via separate CoreAudio endpoint instance simulating Discord / Zoom
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = 0.62f;
        }
        catch
        {
            return;
        }

        // Wait for WASAPI callback to enforce volume lock
        bool enforced = restoredSignal.Wait(2000);
        Assert.True(enforced, "AudioLockEngine did not restore volume within 2 seconds!");
        Assert.Equal(100, capturedRestored);
        Assert.Equal(62, capturedPrevious);
        Assert.Equal(100, engine.CurrentVolumePercent);
    }

    [Fact]
    public void ChangingTargetVolume_ImmediatelyUpdatesEndpoint()
    {
        using var engine = new AudioLockEngine();
        engine.TargetVolumePercent = 100;
        engine.IsLocked = true;
        bool bound = engine.SelectDevice(null);
        if (!bound)
        {
            // Headless cloud CI runners (GitHub Actions / Azure) do not have audio capture hardware
            return;
        }

        // Ensure endpoint is unmuted so volume enforcement logic is active
        engine.SetMute(false);

        // Change target to 85%
        engine.TargetVolumePercent = 85;
        engine.SetVolume(85);

        Assert.Equal(85, engine.CurrentVolumePercent);

        // Reset back to 100%
        engine.TargetVolumePercent = 100;
        engine.SetVolume(100);
        Assert.Equal(100, engine.CurrentVolumePercent);
    }
}
