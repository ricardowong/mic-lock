namespace MicLock.Core.Audio;

public interface IAudioLockEngine : IDisposable
{
    bool IsLocked { get; set; }
    int TargetVolumePercent { get; set; }
    AudioDeviceInfo? ActiveDevice { get; }
    int CurrentVolumePercent { get; }
    bool IsMuted { get; }

    event EventHandler<int>? VolumeChanged;
    event EventHandler<VolumeRestoredEventArgs>? VolumeRestored;
    event EventHandler<AudioDeviceInfo?>? ActiveDeviceChanged;
    event EventHandler<ActivityLogEntry>? LogEntryAdded;

    IReadOnlyList<AudioDeviceInfo> GetAvailableCaptureDevices();
    bool SelectDevice(string? deviceId);
    void SetVolume(int percent);
    void RefreshCurrentState();
}
