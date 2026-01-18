namespace MicLock.Core.Config;

public class AppSettings
{
    public string? SelectedDeviceId { get; set; } = null;
    public string? SelectedDeviceName { get; set; } = null;
    public int TargetVolumePercent { get; set; } = 100;
    public bool IsLockEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool StartMinimized { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public int MaxLogEntries { get; set; } = 100;

    public void ClampValues()
    {
        if (TargetVolumePercent < 0) TargetVolumePercent = 0;
        if (TargetVolumePercent > 100) TargetVolumePercent = 100;
        if (MaxLogEntries < 10) MaxLogEntries = 10;
        if (MaxLogEntries > 1000) MaxLogEntries = 1000;
    }
}
