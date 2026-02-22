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
        Assert.True(bound, "Failed to bind to default recording endpoint.");

        var restoredSignal = new ManualResetEventSlim(false);
        int capturedRestored = 0;
        int capturedPrevious = 0;

        engine.VolumeRestored += (s, e) =>
        {
            capturedPrevious = e.PreviousPercent;
            capturedRestored = e.RestoredPercent;
            restoredSignal.Set();
        };

        // Alter volume externally via powershell command simulating Discord / Zoom
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command \"Set-AudioDevice -RecordingVolume 62\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var proc = Process.Start(psi);
        proc?.WaitForExit();

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
        engine.SelectDevice(null);

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
