using MicLock.Core.Audio;
using Xunit;

namespace MicLock.Tests;

public class PolicyConfigTests
{
    [Fact]
    public void ERole_EnumValues_MatchWindowsCoreAudioConstants()
    {
        Assert.Equal(0, (int)ERole.Console);
        Assert.Equal(1, (int)ERole.Multimedia);
        Assert.Equal(2, (int)ERole.Communications);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetDefaultEndpoint_InvalidInput_ReturnsFalseSafely(string? invalidId)
    {
        bool result = PolicyConfigClient.SetDefaultEndpoint(invalidId!, ERole.Console);
        Assert.False(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetDefaultDeviceAllRoles_InvalidInput_ReturnsFalseSafely(string? invalidId)
    {
        bool result = PolicyConfigClient.SetDefaultDeviceAllRoles(invalidId!);
        Assert.False(result);
    }

    [Fact]
    public void SetDefaultDeviceAllRoles_OnRealAvailableDevice_DoesNotThrow()
    {
        using var engine = new AudioLockEngine();
        var devices = engine.GetAvailableCaptureDevices();

        if (devices.Count > 0)
        {
            var firstDevice = devices[0];
            // Calling SetDefaultDeviceAllRoles on the existing default/active device should execute cleanly
            var ex = Record.Exception(() =>
            {
                PolicyConfigClient.SetDefaultDeviceAllRoles(firstDevice.Id);
            });
            Assert.Null(ex);
        }
    }
}
