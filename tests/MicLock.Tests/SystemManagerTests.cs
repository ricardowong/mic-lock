using MicLock.Core.Sys;
using Xunit;

namespace MicLock.Tests;

public class SystemManagerTests
{
    [Fact]
    public void SingleInstanceManager_AllowsFirstInstance_BlocksSecond()
    {
        string uniqueName = @"Global\MicLock_Test_" + Guid.NewGuid().ToString("N");

        using var first = new SingleInstanceManager();
        bool firstAcquired = first.TryAcquire(uniqueName);
        Assert.True(firstAcquired);
        Assert.True(first.IsFirstInstance);

        using var second = new SingleInstanceManager();
        bool secondAcquired = second.TryAcquire(uniqueName);
        Assert.False(secondAcquired);
        Assert.False(second.IsFirstInstance);
    }

    [Fact]
    public void StartupManager_CanSetAndRemoveCustomKey()
    {
        var manager = new StartupManager();
        string testAppName = "MicLock_UnitTest_" + Guid.NewGuid().ToString("N");
        string testPath = @"C:\MockPath\MicLock.exe";

        try
        {
            bool setSuccess = manager.SetStartup(true, testPath, testAppName, "--test");
            Assert.True(setSuccess);

            bool isRegistered = manager.IsRegisteredForStartup(testAppName);
            Assert.True(isRegistered);

            string? cmd = manager.GetStartupCommand(testAppName);
            Assert.Equal($"\"{testPath}\" --test", cmd);

            bool removeSuccess = manager.SetStartup(false, null, testAppName);
            Assert.True(removeSuccess);

            bool isStillRegistered = manager.IsRegisteredForStartup(testAppName);
            Assert.False(isStillRegistered);
        }
        finally
        {
            manager.SetStartup(false, null, testAppName);
        }
    }
}
