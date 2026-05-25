using System.Threading;
using System.Windows.Threading;
using MicLock.Core.UI;
using Xunit;

namespace MicLock.Tests;

public class PersistentOverlayTests
{
    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "STA thread did not complete within 5 seconds.");

        if (exception != null)
        {
            throw exception;
        }
    }

    [Fact]
    public void OsdBannerWindow_DefaultProperties_MatchSoundSwitchSpec()
    {
        RunInSta(() =>
        {
            var window = new OsdBannerWindow();
            Assert.False(window.KeepOverlayWhenMuted);
            Assert.Equal(0.70, window.OverlayOpacityWhenMuted);
            Assert.False(window.IsCurrentlyMuted);
            window.Close();
        });
    }

    [Fact]
    public void OsdBannerWindow_UpdateSettings_UpdatesAndClampsValues()
    {
        RunInSta(() =>
        {
            var window = new OsdBannerWindow();
            window.UpdateSettings(true, 0.85);

            Assert.True(window.KeepOverlayWhenMuted);
            Assert.Equal(0.85, window.OverlayOpacityWhenMuted);

            // Test clamping high
            window.UpdateSettings(false, 1.45);
            Assert.False(window.KeepOverlayWhenMuted);
            Assert.Equal(1.0, window.OverlayOpacityWhenMuted);

            // Test clamping low
            window.UpdateSettings(true, 0.03);
            Assert.True(window.KeepOverlayWhenMuted);
            Assert.Equal(0.10, window.OverlayOpacityWhenMuted);

            window.Close();
        });
    }

    [Fact]
    public void OsdBannerWindow_ShowMuteState_TracksMuteStatus()
    {
        RunInSta(() =>
        {
            var window = new OsdBannerWindow();

            window.ShowMuteState(true, "Test Mic");
            Assert.True(window.IsCurrentlyMuted);

            window.ShowMuteState(false, "Test Mic");
            Assert.False(window.IsCurrentlyMuted);

            window.Close();
        });
    }

    [Fact]
    public void OsdBannerWindow_ShowDeviceSwitched_UpdatesDeviceAndMute()
    {
        RunInSta(() =>
        {
            var window = new OsdBannerWindow();

            window.ShowDeviceSwitched("Samson Go Mic", isMuted: true);
            Assert.True(window.IsCurrentlyMuted);

            window.ShowDeviceSwitched("Samson Go Mic", isMuted: false);
            Assert.False(window.IsCurrentlyMuted);

            window.Close();
        });
    }
}
