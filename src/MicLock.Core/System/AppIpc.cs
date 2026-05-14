using System.IO;
using System.IO.Pipes;
using System.Text;

namespace MicLock.Core.Sys;

public class AppIpcServer : IDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    public event Action<string>? CommandReceived;

    public AppIpcServer(string pipeName = "MicLock_IPC_Pipe_869533af")
    {
        _pipeName = pipeName;
    }

    public void Start()
    {
        _listenTask = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipeServer.WaitForConnectionAsync(_cts.Token);

                using var reader = new StreamReader(pipeServer, Encoding.UTF8);
                string? command = await reader.ReadLineAsync(_cts.Token);

                if (!string.IsNullOrWhiteSpace(command))
                {
                    CommandReceived?.Invoke(command.Trim());
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Brief pause before restarting listener on error
                await Task.Delay(100);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}

public static class AppIpcClient
{
    public static bool SendCommand(string command, string pipeName = "MicLock_IPC_Pipe_869533af", int timeoutMs = 400)
    {
        try
        {
            using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            pipeClient.Connect(timeoutMs);

            using var writer = new StreamWriter(pipeClient, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
