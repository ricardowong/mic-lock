using System.Threading;

namespace MicLock.Core.Sys;

public class SingleInstanceManager : IDisposable
{
    private const string MutexName = @"Global\MicLock_SingleInstance_Mutex_869533af";
    private Mutex? _mutex;
    private bool _hasHandle;

    public bool IsFirstInstance { get; private set; }

    public bool TryAcquire(string customMutexName = MutexName)
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, customMutexName, out bool createdNew);
            _hasHandle = createdNew;
            IsFirstInstance = createdNew;
            return createdNew;
        }
        catch (AbandonedMutexException)
        {
            _hasHandle = true;
            IsFirstInstance = true;
            return true;
        }
        catch
        {
            IsFirstInstance = false;
            return false;
        }
    }

    public void Dispose()
    {
        if (_hasHandle && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch { }
            _hasHandle = false;
        }

        _mutex?.Dispose();
        _mutex = null;
        GC.SuppressFinalize(this);
    }
}
