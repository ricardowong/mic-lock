using MicLock.Core.Sys;
using Xunit;

namespace MicLock.Tests;

public class IpcTests
{
    [Fact]
    public async Task AppIpc_CanSendAndReceiveCommand()
    {
        string pipeName = "MicLock_Test_Pipe_" + Guid.NewGuid().ToString("N");
        using var server = new AppIpcServer(pipeName);

        var tcs = new TaskCompletionSource<string>();
        server.CommandReceived += cmd => tcs.TrySetResult(cmd);
        server.Start();

        await Task.Delay(50);

        bool sent = AppIpcClient.SendCommand("TOGGLE_MUTE", pipeName, 1000);
        Assert.True(sent);

        var received = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Equal(tcs.Task, received);
        Assert.Equal("TOGGLE_MUTE", await tcs.Task);
    }
}
